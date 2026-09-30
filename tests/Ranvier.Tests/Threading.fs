module Ranvier.Tests.Threading

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

#if !FABLE_COMPILER
// .NET only: JavaScript has one thread.
/// <summary>
/// Runs <c>body</c> on a different thread and waits for it, so a test can say
/// "from another thread" without the result depending on scheduling.
/// </summary>
let private offThread (body: unit -> 'T) : 'T =
    // Deliberately a thread, not Task.Run. A task blocked on with .Result can
    // be inlined onto the waiting thread by the TPL, which runs the supposedly
    // off-thread work on the test's own thread and makes every assertion in
    // this file vacuous. A dedicated thread cannot be inlined.
    let mutable result = Unchecked.defaultof<'T>
    let mutable failure: exn = null

    let thread =
        Thread (
            (fun () ->
                try
                    result <- body ()
                with ex ->
                    failure <- ex),
            IsBackground = true
        )

    thread.Start ()
    thread.Join ()

    if not (isNull failure) then
        raise failure

    result

/// <summary>
/// A synchronisation context that records what was posted to it instead of
/// running it, so a test can assert the marshalling happened without needing a
/// message loop.
/// </summary>
type private RecordingContext() =
    inherit SynchronizationContext()
    let posted = ResizeArray<SendOrPostCallback * obj>()
    member _.Posted = posted

    member _.Drain() =
        let items = posted.ToArray ()
        posted.Clear ()

        for (callback, state) in items do
            callback.Invoke state

    override this.Post(callback, state) =
        posted.Add (callback, state)

/// <summary>
/// What an async body that activates <c>graph</c> and awaits onto the thread pool observes.
/// </summary>
type private AcrossAwait =
    {
        /// <summary>The starting thread sees <c>graph</c> as ambient while the body is suspended.</summary>
        LeakedToStarter: bool
        /// <summary>The body sees <c>graph</c> as ambient after it resumes.</summary>
        AmbientAfterAwait: bool
    }

let private isAmbient (graph: Graph) =
    match Graph.TryCurrent with
    | ValueSome current -> obj.ReferenceEquals (current, graph)
    | ValueNone -> false

let private activationAcrossAwait (graph: Graph) : AcrossAwait =
    use resumed = new ManualResetEventSlim (false)
    use release = new ManualResetEventSlim (false)
    let ambientAfterAwait = ref false

    let body =
        task {
            use _ = graph.Activate ()
            do! Task.Yield ()
            ambientAfterAwait.Value <- isAmbient graph
            resumed.Set ()
            release.Wait ()
        }

    resumed.Wait ()
    let leaked = isAmbient graph
    release.Set ()
    body.Wait ()

    {
        LeakedToStarter = leaked
        AmbientAfterAwait = ambientAfterAwait.Value
    }

/// <summary>
/// A <c>Serialised</c> graph constructed while <c>context</c> is current, or with no context when it is null.
/// </summary>
let private serialisedUnder (context: SynchronizationContext) =
    let previous = SynchronizationContext.Current

    try
        SynchronizationContext.SetSynchronizationContext context
        new Graph (GraphOptions.Default.WithThreadAffinity Serialised)
    finally
        SynchronizationContext.SetSynchronizationContext previous

/// <summary>
/// Runs <c>body</c> with <c>context</c> current on the calling thread.
/// </summary>
let private within (context: SynchronizationContext) (body: unit -> 'T) : 'T =
    let previous = SynchronizationContext.Current

    try
        SynchronizationContext.SetSynchronizationContext context
        body ()
    finally
        SynchronizationContext.SetSynchronizationContext previous

/// <summary>
/// A flush held open on <c>Worker</c>, a thread running under the graph's context. The worker stays inside an effect
/// until <c>Release</c> is set.
/// </summary>
type private HeldFlush =
    {
        Worker: Thread
        Release: ManualResetEventSlim
    }

/// <summary>
/// Starts a flush of <c>g</c>, a <c>Serialised</c> graph constructed under <c>context</c>, on another thread, and
/// returns once that thread is inside the graph.
/// </summary>
let private holdFlush (context: SynchronizationContext) (g: Graph) : HeldFlush =
    let trigger = within context (fun () -> Signal (g, 0))
    let entered = new ManualResetEventSlim (false)
    let release = new ManualResetEventSlim (false)

    within context (fun () ->
        g.Run (fun () ->
            createEffect (fun () ->
                if trigger.Value = 1 then
                    entered.Set ()
                    release.Wait (TimeSpan.FromSeconds 5.0) |> ignore)))

    let worker =
        Thread ((fun () -> within context (fun () -> trigger.Value <- 1)), IsBackground = true)

    worker.Start ()
    entered.Wait (TimeSpan.FromSeconds 5.0) |> ignore

    { Worker = worker; Release = release }
#endif

[<Tests>]
let tests =
    testList
        "Threading"
        [
#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "an off-thread write raises instead of corrupting the graph" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let caught =
                    offThread (fun () ->
                        try
                            s.Value <- 2
                            None
                        with ex ->
                            Some ex)

                match caught with
                | Some ex ->
                    Expect.stringContains ex.Message "owned by thread" "the message must name the problem"
                    Expect.equal s.Peek 1 "and the write must not have landed"
                | None -> failtest "an off-thread write must not be allowed to proceed"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "an equal off-thread write raises too" {
                let g = new Graph ()
                let s = Signal (g, 1)

                // The cutoff would swallow this one silently, hiding the bug
                // until the first write that differs.
                let threw =
                    offThread (fun () ->
                        try
                            s.Value <- 1
                            false
                        with _ ->
                            true)

                Expect.isTrue threw "the guard belongs before the cutoff"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Unchecked affinity lets the write through" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            ThreadAffinity = Unchecked
                        }
                    )

                let s = Signal (g, 1)
                offThread (fun () -> s.Value <- 2)
                Expect.equal s.Peek 2 "the caller took responsibility for affinity"
            }
#endif

            test "an on-thread settle runs inline and queues nothing" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                a.Settle 1

                // The inline fast path is the whole design: the dispatcher's job
                // is to be skipped, not to be fast.
                Expect.equal g.PendingWork 0 "on-thread work must not touch the inbox"
                Expect.equal a.TryValue (Ready 1) "and must be visible immediately"
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "an off-thread settle waits in the inbox until pumped" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                offThread (fun () -> a.Settle 7)

                Expect.equal g.PendingWork 1 "the settle was deferred, not run on the wrong thread"
                Expect.equal a.TryValue Pending "and is not visible yet"

                let ran = g.Pump ()
                Expect.equal ran 1 "the pump ran it"
                Expect.equal a.TryValue (Ready 7) "and now it is visible"
                Expect.equal g.PendingWork 0 "inbox drained"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "flush leaves an off-thread settle in the inbox" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                offThread (fun () -> a.Settle 7)
                g.Run (fun () -> flush ())

                Expect.equal g.PendingWork 1 "flush does not drain the inbox"
                Expect.equal a.TryValue Pending "so the settle is not visible"

                g.Pump () |> ignore
                Expect.equal a.TryValue (Ready 7) "the pump applies it"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "a pump flushes the effects the drained work invalidated" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add a.Value))
                |> ignore

                Expect.isEmpty seen "suspended"

                offThread (fun () -> a.Settle 3)
                Expect.isEmpty seen "still suspended: nothing has drained the inbox"

                g.Pump () |> ignore
                Expect.sequenceEqual seen [ 3 ] "the pump woke the effect"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "the inbox is drained in arrival order" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let log = ResizeArray ()

                for i in 1..5 do
                    offThread (fun () -> g.Dispatch (fun () -> log.Add i))

                Expect.equal g.PendingWork 5 "all five deferred"
                g.Pump () |> ignore
                Expect.sequenceEqual log [ 1; 2; 3; 4; 5 ] "FIFO"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "a throwing continuation does not strand the rest of the inbox" {
                let g = new Graph ()
                let log = ResizeArray ()

                offThread (fun () ->
                    g.Dispatch (fun () -> failwith "boom")
                    g.Dispatch (fun () -> log.Add "ran"))

                let ran = g.Pump ()

                Expect.equal ran 2 "both items were run"
                Expect.sequenceEqual log [ "ran" ] "the survivor still ran"
                Expect.equal (Seq.length g.Root.Errors) 1 "and the failure was recorded"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "pumping from the wrong thread raises" {
                let g = new Graph ()

                let threw =
                    offThread (fun () ->
                        try
                            g.Pump () |> ignore
                            false
                        with _ ->
                            true)

                Expect.isTrue threw "the inbox exists to be drained by its owner"
            }
#endif

            test "a graph with no ambient context defers" {
                let g = new Graph ()

#if FABLE_COMPILER
                // Fable's default: a settle already runs on the only thread.
                Expect.isTrue (g.Dispatcher :? ImmediateDispatcher) "a settle under Fable applies at once"
#else
                Expect.isTrue
                    (g.Dispatcher :? ManualDispatcher)
                    "a console or test thread has no loop to post to, and the library must not invent one"
#endif
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "a graph built under a synchronisation context posts to it" {
                let previous = SynchronizationContext.Current
                let context = RecordingContext ()

                try
                    SynchronizationContext.SetSynchronizationContext context
                    let g = new Graph ()
                    let a = AsyncSource<int>(g)

                    Expect.isTrue (g.Dispatcher :? SynchronizationContextDispatcher) "the ambient context was captured"

                    offThread (fun () -> a.Settle 9)

                    Expect.equal context.Posted.Count 1 "the wake-up was posted to the context"
                    Expect.equal a.TryValue Pending "and nothing ran on the posting thread"

                    // Standing in for the message loop the UI framework would run.
                    context.Drain ()
                    Expect.equal a.TryValue (Ready 9) "the drain applied it on the owning thread"
                finally
                    SynchronizationContext.SetSynchronizationContext previous
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "an AsyncMemo settling on the pool lands after a pump" {
                let g = new Graph ()
                let gate = new ManualResetEventSlim (false)

                let query =
                    Make.AsyncMemo<int>(
                        g,
                        fun _ _ ->
                            Task.Run (fun () ->
                                gate.Wait ()
                                11)
                    )

                Expect.equal query.TryValue Pending "in flight"

                // The real shape of the hole this closes: a task completing on a
                // pool thread used to publish into the graph from that thread.
                gate.Set ()

                let deadline = DateTime.UtcNow.AddSeconds 5.0
                let mutable landed = false

                while not landed && DateTime.UtcNow < deadline do
                    if g.Pump () > 0 then landed <- true else Thread.Sleep 1

                Expect.isTrue landed "the continuation reached the inbox"
                Expect.equal query.TryValue (Ready 11) "and was applied on the owning thread"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "a write from another thread raises even under the graph's synchronisation context" {
                let previous = SynchronizationContext.Current
                let context = SynchronizationContext ()

                try
                    SynchronizationContext.SetSynchronizationContext context
                    let g = new Graph ()
                    let s = Signal (g, 0)

                    let caught =
                        offThread (fun () ->
                            SynchronizationContext.SetSynchronizationContext context

                            try
                                s.Value <- 1
                                None
                            with ex ->
                                Some ex)

                    match caught with
                    | Some ex -> Expect.stringContains ex.Message "owned by thread" "affinity is the thread id"
                    | None -> failtest "a shared synchronisation context must not pass the affinity check"

                    Expect.equal s.Peek 0 "and the write did not land"
                finally
                    SynchronizationContext.SetSynchronizationContext previous
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "an ImmediateDispatcher under Guarded raises on an off-thread settle and keeps the work queued" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ImmediateDispatcher ())
                        }
                    )

                let a = AsyncSource<int>(g)

                let caught =
                    offThread (fun () ->
                        try
                            a.Settle 4
                            None
                        with ex ->
                            Some ex)

                match caught with
                | Some ex -> Expect.stringContains ex.Message "Pump ran on thread" "the drain raised on the settling thread"
                | None -> failtest "an off-thread drain must not run under Guarded"

                Expect.equal g.PendingWork 1 "the settle is still queued"
                g.Pump () |> ignore
                Expect.equal a.TryValue (Ready 4) "and the owning thread applies it"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "during a flush an off-thread write raises, and Dispatch and Settle return without waiting" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let target = Signal (g, 0)
                let a = AsyncSource<int>(g)
                let seen = ResizeArray ()
                let timeout = TimeSpan.FromSeconds 5.0
                use inFlush = new ManualResetEventSlim (false)
                use workerDone = new ManualResetEventSlim (false)
                let writeError = ref null
                let returned = ref false

                let worker =
                    Thread (
                        (fun () ->
                            if inFlush.Wait timeout then
                                try
                                    target.Value <- 1
                                with ex ->
                                    writeError.Value <- ex

                                g.Dispatch (fun () -> target.Value <- 2)
                                a.Settle 3
                                returned.Value <- true

                            workerDone.Set ()),
                        IsBackground = true
                    )

                worker.Start ()

                new Effect (
                    g,
                    fun () ->
                        if trigger.Value = 1 then
                            inFlush.Set ()
                            workerDone.Wait timeout |> ignore
                )
                |> ignore

                new Effect (g, (fun () -> seen.Add target.Value))
                |> ignore

                trigger.Value <- 1
                worker.Join ()

                Expect.isTrue returned.Value "Dispatch and Settle returned while the owning thread was inside an effect"
                Expect.isNotNull writeError.Value "the direct write raised"
                Expect.stringContains writeError.Value.Message "owned by thread" "with the affinity message"
                Expect.equal g.PendingWork 2 "the dispatched write and the settle wait in the inbox"
                Expect.equal target.Peek 0 "and the flush finished without them"

                g.Pump () |> ignore
                Expect.equal target.Peek 2 "the pump applies the dispatched write"
                Expect.equal a.TryValue (Ready 3) "and the settle"
                Expect.sequenceEqual seen [ 0; 2 ] "the effect sees the dispatched write after the pump"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "an off-thread read of a stale memo raises and leaves it stale" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Memo (g, (fun _ -> s.Value * 10))

                Expect.equal m.Value 10 "first read"
                s.Value <- 2

                let caught =
                    offThread (fun () ->
                        try
                            m.Value |> ignore
                            None
                        with ex ->
                            Some ex)

                match caught with
                | Some ex -> Expect.stringContains ex.Message "A stale read ran on thread" "the recompute is guarded"
                | None -> failtest "an off-thread recompute must not run"

                Expect.equal m.Runs 1 "the memo did not recompute off-thread"
                Expect.equal m.Value 20 "and recomputes on the owning thread"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "every off-thread entry point raises on a guarded graph" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Memo (g, (fun _ -> s.Value))
                let e = new Effect (g, (fun () -> s.Value |> ignore))

                let entries: (string * (unit -> unit)) list =
                    [
                        "Creating a node", (fun () -> Signal (g, 0) |> ignore)
                        "A batch", (fun () -> g.Batch (fun () -> ()) |> ignore)
                        "A flush", (fun () -> g.Flush ())
                        "An untracked read", (fun () -> g.Untrack (fun () -> s.Value) |> ignore)
                        "Creating a root", (fun () -> g.CreateRoot (fun _ -> ()) |> ignore)
                        "Registering a cleanup", (fun () -> g.OnCleanup (fun () -> ()))
                        "Disposing a node", (fun () -> m.Dispose ())
                        "Disposing a node", (fun () -> e.Dispose ())
                        "Disposing a graph", (fun () -> g.Dispose ())
                    ]

                for (operation, entry) in entries do
                    let message =
                        offThread (fun () ->
                            try
                                entry ()
                                None
                            with ex ->
                                Some ex.Message)

                    match message with
                    | Some text -> Expect.stringContains text $"%s{operation} ran on thread" $"%s{operation} is guarded"
                    | None -> failtest $"%s{operation} ran off-thread without raising"

                s.Value <- 2
                Expect.equal m.Value 2 "the graph is intact after the rejected calls"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Unchecked affinity lets an off-thread stale read through" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            ThreadAffinity = Unchecked
                        }
                    )

                let s = Signal (g, 1)
                let m = Memo (g, (fun _ -> s.Value * 10))
                m.Value |> ignore
                s.Value <- 2
                Expect.equal (offThread (fun () -> m.Value)) 20 "the caller took responsibility for affinity"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "an activation inside an async body is invisible to the starting thread while suspended" {
                let guarded = activationAcrossAwait (new Graph ())

                Expect.isFalse guarded.LeakedToStarter "a guarded graph stays out of the starting thread"
                Expect.isFalse guarded.AmbientAfterAwait "and is ambient on its activating thread only"

                let unchecked =
                    activationAcrossAwait (
                        new Graph (
                            { GraphOptions.Default with
                                ThreadAffinity = Unchecked
                            }
                        )
                    )

                Expect.isFalse unchecked.LeakedToStarter "an unchecked graph stays out of the starting thread"
                Expect.isTrue unchecked.AmbientAfterAwait "and follows the body across the await"
                Expect.isTrue Graph.TryCurrent.IsNone "nothing is ambient once the bodies end"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "graphs owned by separate threads update in parallel" {
                let threads = max 4 Environment.ProcessorCount
                let writes = 5000
                use start = new Barrier (threads)
                let results = Array.zeroCreate<int * int> threads
                let failures = Array.zeroCreate<exn> threads

                let workers =
                    Array.init threads (fun i ->
                        Thread (
                            (fun () ->
                                try
                                    use g = new Graph ()
                                    let s = Signal (g, 0)
                                    let doubled = Memo (g, (fun _ -> s.Value * 2))
                                    let settled = AsyncSource<int>(g)
                                    let seen = ref 0
                                    let total = ref 0

                                    g.Run (fun () ->
                                        createEffect (fun () -> seen.Value <- doubled.Value)
                                        createEffect (fun () -> total.Value <- total.Value + settled.Value))

                                    start.SignalAndWait ()

                                    for k in 1..writes do
                                        s.Value <- k

                                        // An off-thread settle per hundred writes keeps every inbox busy
                                        // while the other graphs flush.
                                        if k % 100 = 0 then
                                            offThread (fun () -> settled.Settle k)
                                            g.Pump () |> ignore

                                    results[i] <- (seen.Value, total.Value)
                                with ex ->
                                    failures[i] <- ex),
                            IsBackground = true
                        ))

                for w in workers do
                    w.Start ()

                for w in workers do
                    w.Join ()

                let expectedTotal = [ 100..100..writes ] |> List.sum

                for i in 0 .. threads - 1 do
                    if not (isNull failures[i]) then
                        raise failures[i]

                    Expect.equal results[i] (writes * 2, expectedTotal) $"graph %d{i} saw every write and settle"
            }
#endif

            test "Serialised: writes, reads and effects run as on any graph" {
                let g = new Graph (GraphOptions.Default.WithThreadAffinity Serialised)
                let s = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> s.Value * 10))
                let seen = ResizeArray ()
                g.Run (fun () -> createEffect (fun () -> seen.Add m.Value))

                s.Value <- 2
                g.Batch (fun () -> s.Value <- 3) |> ignore

                Expect.equal m.Value 30 "the memo follows the writes"
                Expect.sequenceEqual seen [ 10; 20; 30 ] "the effect ran once per write"
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Serialised: a free graph accepts a write and a stale read from another thread" {
                let g = serialisedUnder null
                let s = Signal (g, 1)
                let m = Memo (g, (fun _ -> s.Value * 10))
                m.Value |> ignore

                offThread (fun () -> s.Value <- 2)
                Expect.equal (offThread (fun () -> m.Value)) 20 "the other thread brought the memo current"

                s.Value <- 3
                Expect.equal m.Value 30 "the stale read released the graph"
                Expect.isFalse g.IsOnGraphThread "the graph is free between entries"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Serialised: a thread entering while another is inside the graph raises" {
                let context = SynchronizationContext ()
                let g = serialisedUnder context
                let s = within context (fun () -> Signal (g, 0))
                let source = within context (fun () -> Signal (g, 1))
                let stale = within context (fun () -> Memo (g, (fun _ -> source.Value * 10)))
                within context (fun () -> stale.Value |> ignore)
                within context (fun () -> source.Value <- 2)
                let held = holdFlush context g

                let entries: (string * (unit -> unit)) list =
                    [
                        "A signal write", (fun () -> s.Value <- 1)
                        "A stale read", (fun () -> stale.Value |> ignore)
                        "A batch", (fun () -> g.Batch (fun () -> ()) |> ignore)
                        "A flush", (fun () -> g.Flush ())
                        "Creating a node", (fun () -> Signal (g, 0) |> ignore)
                        "Creating a root", (fun () -> g.CreateRoot (fun _ -> ()) |> ignore)
                        "Pump", (fun () -> g.Pump () |> ignore)
                    ]

                let messages =
                    try
                        [
                            for (operation, entry) in entries ->
                                operation,
                                within context (fun () ->
                                    try
                                        entry ()
                                        None
                                    with ex ->
                                        Some ex.Message)
                        ]
                    finally
                        held.Release.Set ()
                        held.Worker.Join ()

                for (operation, message) in messages do
                    match message with
                    | Some text ->
                        Expect.stringContains text $"%s{operation} ran on thread" $"%s{operation} is named"
                        Expect.stringContains text "was inside this Serialised graph" "with the concurrent entry"
                    | None -> failtest $"%s{operation} entered a graph another thread was inside"

                Expect.equal s.Peek 0 "the rejected write did not land"
                within context (fun () -> s.Value <- 2)
                Expect.equal s.Peek 2 "the graph accepts writes once the other thread leaves"
                Expect.equal (within context (fun () -> stale.Value)) 20 "and stale reads"
                Expect.isFalse g.IsOnGraphThread "the rejected entries left the graph free"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Serialised: disposing a root while another thread is inside the graph raises and leaves the root live" {
                let context = SynchronizationContext ()
                let g = serialisedUnder context
                let s = within context (fun () -> Signal (g, 0))
                let runs = ref 0

                let root =
                    within context (fun () ->
                        g.Run (fun () ->
                            createRoot (fun owner ->
                                createEffect (fun () ->
                                    s.Value |> ignore
                                    runs.Value <- runs.Value + 1)

                                owner)))

                let held = holdFlush context g

                let caught =
                    try
                        within context (fun () ->
                            try
                                root.Dispose ()
                                None
                            with ex ->
                                Some ex.Message)
                    finally
                        held.Release.Set ()
                        held.Worker.Join ()

                match caught with
                | Some text -> Expect.stringContains text "Disposing a root ran on thread" "the operation is named"
                | None -> failtest "a root was disposed while another thread was inside the graph"

                Expect.isFalse root.IsDisposed "the rejected dispose left the root live"
                within context (fun () -> s.Value <- 1)
                Expect.equal runs.Value 2 "the root's effect still runs"

                within context (fun () -> root.Dispose ())
                Expect.isTrue root.IsDisposed "a retry disposes the root"
                within context (fun () -> s.Value <- 2)
                Expect.equal runs.Value 2 "the disposed root's effect no longer runs"
                Expect.isFalse g.IsOnGraphThread "the graph is free after the teardown"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Serialised: an entry outside the construction context raises" {
                let context = SynchronizationContext ()
                let g = serialisedUnder context
                let s = within context (fun () -> Signal (g, 0))

                let caught =
                    offThread (fun () ->
                        try
                            s.Value <- 1
                            None
                        with ex ->
                            Some ex)

                match caught with
                | Some ex -> Expect.stringContains ex.Message "outside the synchronisation context" "the message names the context"
                | None -> failtest "a write without the graph's context must not proceed"

                Expect.equal s.Peek 0 "the write did not land"
                offThread (fun () -> within context (fun () -> s.Value <- 2))
                Expect.equal s.Peek 2 "any thread on the context may write"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Serialised: a settle from outside the graph is queued, even on the graph's context" {
                let context = RecordingContext ()
                let g = serialisedUnder context
                let a = within context (fun () -> AsyncSource<int>(g))

                within context (fun () -> a.Settle 5)

                Expect.equal g.PendingWork 1 "the settle waits in the inbox"
                Expect.equal context.Posted.Count 1 "and a drain was posted to the context"
                Expect.equal a.TryValue Pending "nothing applied it inline"

                offThread (fun () -> within context (fun () -> context.Drain ()))
                Expect.equal a.TryValue (Ready 5) "the drain applied it, on whichever thread ran the context"
                Expect.equal g.PendingWork 0 "inbox drained"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Serialised: work posted from inside the graph runs inline" {
                let g = serialisedUnder null
                let s = Signal (g, 0)
                let a = AsyncSource<int>(g)
                let inside = ResizeArray ()

                g.Run (fun () ->
                    createEffect (fun () ->
                        if s.Value = 1 then
                            inside.Add g.IsOnGraphThread
                            g.Dispatch (fun () -> a.Settle 8)))

                s.Value <- 1

                Expect.sequenceEqual inside [ true ] "the effect's thread holds the graph"
                Expect.equal g.PendingWork 0 "nothing was queued"
                Expect.equal a.TryValue (Ready 8) "the settle applied inline"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "Serialised: a drain that finds the graph held leaves the work, and the holder posts it again" {
                let context = RecordingContext ()
                let g = serialisedUnder context
                let a = within context (fun () -> AsyncSource<int>(g))
                let held = holdFlush context g

                try
                    within context (fun () -> a.Settle 6)
                    Expect.equal context.Posted.Count 1 "the settle posted a drain"

                    within context (fun () -> context.Drain ())
                    Expect.equal g.PendingWork 1 "the drain left the work for the holder"
                finally
                    held.Release.Set ()
                    held.Worker.Join ()

                Expect.equal context.Posted.Count 1 "the holder's release posted a new drain"
                within context (fun () -> context.Drain ())
                Expect.equal a.TryValue (Ready 6) "and that drain applied the settle"
            }
#endif
        ]
