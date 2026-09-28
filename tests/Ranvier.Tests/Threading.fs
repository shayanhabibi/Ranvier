module Ranvier.Tests.Threading

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

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

[<Tests>]
let tests =
    testList
        "Threading"
        [
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

            test "an on-thread settle runs inline and queues nothing" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                a.Settle 1

                // The inline fast path is the whole design: the dispatcher's job
                // is to be skipped, not to be fast.
                Expect.equal g.PendingWork 0 "on-thread work must not touch the inbox"
                Expect.equal a.TryValue (Ready 1) "and must be visible immediately"
            }

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

            test "a graph with no ambient context defers" {
                let g = new Graph ()

                Expect.isTrue
                    (g.Dispatcher :? ManualDispatcher)
                    "a console or test thread has no loop to post to, and the library must not invent one"
            }

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

            test "an AsyncMemo settling on the pool lands after a pump" {
                let g = new Graph ()
                let gate = new ManualResetEventSlim (false)

                let query =
                    new AsyncMemo<int> (
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
        ]
