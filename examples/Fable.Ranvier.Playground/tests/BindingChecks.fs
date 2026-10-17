module BindingChecks

open System
open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Scheduling

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private narrowQueued suspend failWrite =
    use graph = new Graph ()
    let queue = Queue (graph, CollectAndContinue, ignore)
    let scope = graph.Run (fun () -> Api.createRoot id)
    let source = graph.Run (fun () -> Api.createSignal 1)
    let pending = graph.Run (fun () -> Api.createSignal false)
    let missing = graph.Run (fun () -> Api.createAsyncSource<int>())
    let writes = ResizeArray<int>()
    let mutable reads = 0

    graph.Run (fun () ->
        Api.createRoot (fun owner ->
            scope.OnCleanup (Action owner.Dispose)

            Binding.startQueued
                (fun cleanup -> scope.OnCleanup (Action cleanup))
                (fun () -> not scope.IsDisposed)
                queue
                (=)
                (fun () ->
                    reads <- reads + 1
                    if pending.Value then missing.Value else source.Value)
                (fun value ->
                    if failWrite && value = 2 then
                        failwith "setter"

                    writes.Add value)))

    equal [ 1 ] (List.ofSeq writes)
    graph.Run (fun () -> source.Value <- 2)

    if suspend then
        graph.Run (fun () -> pending.Value <- true)

    queue.Flush ()
    equal (if suspend || failWrite then [ 1 ] else [ 1; 2 ]) (List.ofSeq writes)
    equal (if failWrite && not suspend then 1 else 0) queue.Errors.Length

    graph.Run (fun () ->
        Api.batch (fun () ->
            pending.Value <- false
            source.Value <- 3))

    queue.Flush ()
    equal 3 writes[writes.Count - 1]
    graph.Run (fun () -> source.Value <- 4)
    scope.Dispose ()
    let before = reads
    graph.Run (fun () -> source.Value <- 5)
    queue.Flush ()
    equal before reads
    equal 3 writes[writes.Count - 1]
    queue.Dispose ()

let private untracked () =
    use graph = new Graph ()
    let queue = Queue (graph, CollectAndContinue, ignore)

    let source, incidental =
        graph.Run (fun () -> Api.createSignal 1, Api.createSignal 0)

    let mutable reads, writes = 0, 0

    graph.Run (fun () ->
        Binding.startQueued
            ignore
            (fun () -> true)
            queue
            (fun a b ->
                incidental.Value |> ignore
                a = b)
            (fun () ->
                reads <- reads + 1
                source.Value)
            (fun _ ->
                incidental.Value |> ignore
                writes <- writes + 1))

    graph.Run (fun () -> source.Value <- 2)
    queue.Flush ()
    graph.Run (fun () -> incidental.Value <- 1)
    queue.Flush ()
    equal (2, 2) (reads, writes)
    queue.Dispose ()

type MicrotaskFixture =
    {
        Update: int -> unit
        Values: unit -> int array
        Dispose: unit -> unit
    }

let microtaskFixture () =
    let graph = new Graph ()
    let scope = graph.Run (fun () -> Api.createRoot id)
    let queue = Microtask.create graph CollectAndContinue
    let source = graph.Run (fun () -> Api.createSignal 1)
    let writes = ResizeArray<int>()

    graph.Run (fun () ->
        Api.createRoot (fun owner ->
            scope.OnCleanup (Action owner.Dispose)

            Binding.startQueued
                (fun cleanup -> scope.OnCleanup (Action cleanup))
                (fun () -> not scope.IsDisposed)
                queue
                (=)
                (fun () -> source.Value)
                writes.Add))

    writes.Clear ()

    {
        Update = fun value -> graph.Run (fun () -> source.Value <- value)
        Values = fun () -> writes.ToArray ()
        Dispose =
            fun () ->
                scope.Dispose ()
                queue.Dispose ()
                graph.Dispose ()
    }

let private queuedWaves () =
    use graph = new Graph ()
    let posts = ResizeArray<unit -> unit>()
    let queue = Queue (graph, CollectAndContinue, posts.Add)
    let a, b = queue.Reserve (), queue.Reserve ()
    let writes = ResizeArray<int>()

    queue.Enqueue (
        a,
        fun () ->
            writes.Add 1
            queue.Enqueue (b, fun () -> writes.Add 3)
    )

    queue.Enqueue (b, fun () -> writes.Add 2)
    equal 1 posts.Count
    queue.Flush ()
    equal [ 1 ] (List.ofSeq writes)
    equal 2 posts.Count
    posts[0]()
    equal [ 1 ] (List.ofSeq writes)
    posts[1]()
    equal [ 1; 3 ] (List.ofSeq writes)
    queue.Dispose ()


let private queueFailure failure =
    use graph = new Graph ()
    let queue = Queue (graph, failure, ignore)
    let a, b = queue.Reserve (), queue.Reserve ()
    let writes = ResizeArray<int>()
    queue.Enqueue (a, fun () -> failwith "commit")
    queue.Enqueue (b, fun () -> writes.Add 2)
    queue.Flush ()
    equal (if failure = CollectAndContinue then [ 2 ] else []) (List.ofSeq writes)

    equal
        [ "commit" ]
        (queue.Errors
         |> Array.map _.Message
         |> Array.toList)

    queue.Flush ()
    equal (if failure = CollectAndContinue then [ 2 ] else []) (List.ofSeq writes)
    queue.Enqueue (b, fun () -> writes.Add 3)
    queue.Flush ()
    equal (if failure = CollectAndContinue then [ 2; 3 ] else [ 3 ]) (List.ofSeq writes)
    queue.Dispose ()


let private recursiveFlush () =
    use graph = new Graph ()
    let queue = Queue (graph, CollectAndContinue, ignore)
    let mutable rejected = false

    queue.Enqueue (
        queue.Reserve (),
        fun () ->
            try
                queue.Flush ()
            with :? InvalidOperationException ->
                rejected <- true
    )

    queue.Flush ()
    equal true rejected
    queue.Dispose ()


let private snapshotDisposal () =
    use graph = new Graph ()
    let queue = Queue (graph, CollectAndContinue, ignore)
    let writes = ResizeArray<int>()

    queue.Enqueue (
        queue.Reserve (),
        fun () ->
            writes.Add 1
            queue.Dispose ()
    )

    queue.Enqueue (queue.Reserve (), fun () -> writes.Add 2)
    queue.Flush ()
    equal [ 1 ] (List.ofSeq writes)


let private failFastReplacements () =
    use graph = new Graph ()
    let queue = Queue (graph, FailFastCancel, ignore)
    let a, b, c = queue.Reserve (), queue.Reserve (), queue.Reserve ()
    let writes = ResizeArray<int>()

    queue.Enqueue (
        a,
        fun () ->
            queue.Enqueue (b, fun () -> writes.Add 3)
            queue.Enqueue (c, fun () -> writes.Add 4)
            failwith "commit"
    )

    queue.Enqueue (b, fun () -> writes.Add 2)
    queue.Flush ()
    equal [] (List.ofSeq writes)
    queue.Flush ()
    equal [ 4 ] (List.ofSeq writes)
    queue.Dispose ()


let private comparerDisposal () =
    use graph = new Graph ()
    let scope = graph.Run (fun () -> Api.createRoot id)
    let queue = Queue (graph, CollectAndContinue, ignore)
    let source = graph.Run (fun () -> Api.createSignal 1)
    let writes = ResizeArray<int>()

    graph.Run (fun () ->
        Api.createRoot (fun owner ->
            scope.OnCleanup (Action owner.Dispose)

            Binding.startQueued
                (fun cleanup -> scope.OnCleanup (Action cleanup))
                (fun () -> not scope.IsDisposed)
                queue
                (fun _ _ ->
                    scope.Dispose ()
                    false)
                (fun () -> source.Value)
                writes.Add))

    graph.Run (fun () -> source.Value <- 2)
    queue.Flush ()
    equal [ 1 ] (List.ofSeq writes)
    queue.Dispose ()

let private retrySameValue () =
    use graph = new Graph ()
    let queue = Queue (graph, CollectAndContinue, ignore)
    let source = graph.Run (fun () -> Api.createSignal 1)
    let writes = ResizeArray<int>()
    let mutable reject = true

    graph.Run (fun () ->
        Binding.startQueued ignore (fun () -> true) queue (=) (fun () -> source.Value) (fun value ->
            if value = 2 && reject then
                failwith "setter"
            else
                writes.Add value))

    graph.Run (fun () -> source.Value <- 2)
    queue.Flush ()
    reject <- false
    graph.Run (fun () -> source.Value <- 0)
    graph.Run (fun () -> source.Value <- 2)
    queue.Flush ()
    equal [ 1; 2 ] (List.ofSeq writes)
    queue.Dispose ()

let private writerCleanup () =
    use graph = new Graph ()
    let scope = graph.Run (fun () -> Api.createRoot id)
    let queue = Queue (graph, CollectAndContinue, ignore)
    let source = graph.Run (fun () -> Api.createSignal 1)
    let mutable cleaned = 0

    graph.Run (fun () ->
        Api.createRoot (fun owner ->
            scope.OnCleanup (Action owner.Dispose)

            Binding.startQueued
                (fun cleanup -> scope.OnCleanup (Action cleanup))
                (fun () -> not scope.IsDisposed)
                queue
                (=)
                (fun () -> source.Value)
                (fun _ -> Api.onCleanup (fun () -> cleaned <- cleaned + 1))))

    graph.Run (fun () -> source.Value <- 2)
    equal 1 cleaned
    queue.Flush ()
    scope.Dispose ()
    equal 2 cleaned
    queue.Dispose ()

let private reentrantCommit () =
    use graph = new Graph ()
    let queue = Queue (graph, CollectAndContinue, ignore)
    let a, b = graph.Run (fun () -> Api.createSignal 0, Api.createSignal 0)
    let writes = ResizeArray<string>()

    graph.Run (fun () ->
        Binding.startQueued ignore (fun () -> true) queue (=) (fun () -> a.Value) (fun value ->
            writes.Add ("a" + string value)

            if value = 1 then
                Api.batch (fun () ->
                    a.Value <- 2
                    b.Value <- 2)

                Api.flush ())

        Binding.startQueued ignore (fun () -> true) queue (=) (fun () -> b.Value) (fun value -> writes.Add ("b" + string value)))

    writes.Clear ()
    graph.Run (fun () -> a.Value <- 1)
    graph.Run (fun () -> b.Value <- 1)
    queue.Flush ()
    equal [ "a1" ] (List.ofSeq writes)
    queue.Flush ()
    equal [ "a2"; "b2" ] (writes |> Seq.skip 1 |> Seq.sort |> List.ofSeq)
    queue.Dispose ()

let pureCases =
    [|
        "comparer disposal prevents queued writes", comparerDisposal
        "failed setters can retry an equal previously rejected value", retrySameValue
        "queued writers belong to the effect run's cleanup scope", writerCleanup
        "reentrant queued commits invalidate stale snapshot values", reentrantCommit
        "queued text coalesces and follows scope disposal", fun () -> narrowQueued false false
        "suspended readers cancel stale queued text", fun () -> narrowQueued true false
        "failed queued setters retain committed equality", fun () -> narrowQueued false true
        "queued comparer and writer reads are untracked", untracked
        "queue snapshots invalidate replaced work", queuedWaves
        "collect-and-continue preserves later work", fun () -> queueFailure CollectAndContinue
        "fail-fast cancels remaining snapshot work", fun () -> queueFailure FailFastCancel
        "recursive queue flush is rejected", recursiveFlush
        "queue disposal during flush cancels remaining work", snapshotDisposal
        "fail-fast cancels replacements but preserves independent next wave", failFastReplacements
    |]

let cases = pureCases
