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
                    Make.AsyncMemo<int> (
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
        ]
