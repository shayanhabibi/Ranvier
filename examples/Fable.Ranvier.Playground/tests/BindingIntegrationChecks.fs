module BindingIntegrationChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Scheduling
open Ranvier.View.Foundation.Mounting

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private host () : HTMLElement =
    globalThis.document.createElement "div"

[<Emit("(() => { const original = document.createTextNode; document.createTextNode = function(value) { const node = original.call(this, value); Object.defineProperty(node, 'textContent', { set() { throw new Error('setter'); }, configurable: true }); return node; }; return () => { document.createTextNode = original; }; })()")>]
let private failTextWrites () : unit -> unit = jsNative

let private setupFailure queued =
    use graph = new Graph ()
    let host = host ()
    let mutable cleaned, rejected = false, false
    let restore = failTextWrites ()

    try
        let mode =
            if queued then
                QueuedCommits (CollectAndContinue, ignore)
            else
                SynchronousCommits

        let view =
            Tree.ops.Component (fun setup ->
                setup.OnCleanup (fun () -> cleaned <- true)
                Typed.reactiveText Tree.ops (fun () -> "value"))

        try
            mountWith mode graph None ignore host None (fun context -> Tree.run view context.Backend)
            |> ignore
        with error ->
            equal "setter" error.Message
            rejected <- true

        equal (true, true, "") (rejected, cleaned, host.textContent)
    finally
        restore ()

let private boundary (ops: Operations<'view>) run queued =
    use graph = new Graph ()

    let flight =
        graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string>()))

    let mutable pending, failed, bodies = 0, 0, 0
    let host = host ()

    let mode =
        if queued then
            QueuedCommits (CollectAndContinue, ignore)
        else
            SynchronousCommits

    let view =
        ops.Component (fun _ ->
            let result =
                Api.createBoundary
                    (fun _ ->
                        pending <- pending + 1
                        "loading")
                    (fun _ _ ->
                        failed <- failed + 1
                        "failed")
                    (fun () ->
                        bodies <- bodies + 1
                        flight.Value.Value)

            Typed.div
                ops
                [||]
                [|
                    Typed.reactiveText ops (fun () -> result.Value)
                    Typed.reactiveText ops (fun () -> result.Value)
                |])

    let mounted =
        mountWith mode graph None ignore host None (fun context -> run view context.Backend)

    equal "loadingloading" host.textContent
    let nodes = host.firstChild.Value.childNodes
    let first, second = nodes.item 0., nodes.item 1.
    equal (1, 0, 1) (pending, failed, bodies)
    graph.Run (fun () -> flight.Value.Settle "ready")

    if queued then
        equal "loadingloading" host.textContent

    mounted.Flush ()
    equal "readyready" host.textContent
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<string>())
    mounted.Flush ()
    equal "loadingloading" host.textContent
    graph.Run (fun () -> flight.Value.Fail (Exception "reader"))
    mounted.Flush ()
    equal "failedfailed" host.textContent
    equal (2, 1, 4) (pending, failed, bodies)
    graph.Run (fun () -> flight.Value.Settle "recovered")
    mounted.Flush ()
    equal "recoveredrecovered" host.textContent
    equal true (obj.ReferenceEquals (first, nodes.item 0.))
    equal true (obj.ReferenceEquals (second, nodes.item 1.))
    graph.Run (fun () -> flight.Value.Settle "stale")
    mounted.Dispose ()
    let before = bodies
    graph.Run (fun () -> flight.Value.Fail (Exception "disposed"))
    mounted.Flush ()
    equal before bodies
    equal "" host.textContent

let private nested (ops: Operations<'view>) run =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1, "one"; 2, "two" ])

    let view =
        Keyed.list ops fst (fun () -> rows.Value) (fun current -> ops.Component (fun _ -> Typed.reactiveText ops (fun () -> snd (current ()))))

    let host = host ()

    let mounted =
        mountWith (QueuedCommits (CollectAndContinue, ignore)) graph None ignore host None (fun context -> run view context.Backend)

    equal "onetwo" host.textContent
    graph.Run (fun () -> rows.Value <- [ 1, "ONE"; 2, "TWO" ])
    equal "onetwo" host.textContent
    mounted.Flush ()
    equal "ONETWO" host.textContent
    graph.Run (fun () -> rows.Value <- [ 1, "stale"; 2, "next" ])
    graph.Run (fun () -> rows.Value <- [ 2, "next" ])
    mounted.Flush ()
    equal "next" host.textContent
    mounted.Dispose ()

let private independentMounts () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "one")
    let view = Typed.reactiveText Tree.ops (fun () -> source.Value)
    let a, b = host (), host ()

    let attach host =
        mountWith (QueuedCommits (CollectAndContinue, ignore)) graph None ignore host None (fun context -> Tree.run view context.Backend)

    let first, second = attach a, attach b
    graph.Run (fun () -> source.Value <- "two")
    first.Dispose ()
    second.Flush ()
    equal ("", "two") (a.textContent, b.textContent)
    second.Dispose ()

let private serverBoundary (ops: Operations<'view>) run =
    use graph = new Graph ()

    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            let source = Api.createAsyncSource<string>()

            let result =
                Api.createBoundary (fun _ -> "<pending>") (fun ex _ -> ex.Message) (fun () -> source.Value)

            let view = Typed.reactiveText ops (fun () -> result.Value)
            equal "&lt;pending&gt;" (Html.render Reject (run view))
            source.Settle "<&"
            equal "&lt;&amp;" (Html.render Reject (run view))
            source.Fail (Exception "<failed>")
            equal "&lt;failed&gt;" (Html.render Reject (run view))))

let private structuralBoundary (ops: Operations<'view>) run queued =
    use graph = new Graph ()

    let fresh () =
        Api.createAsyncSource<string>(), Api.createAsyncSource<string>()

    let flight = graph.Run (fun () -> Api.createSignal (fresh ()))
    let setups, cleanups = ResizeArray<int>(), ResizeArray<int>()
    let host = host ()

    let view =
        ops.Component (fun _ ->
            let selected =
                Api.createBoundary (fun _ -> 0, ("loading", "")) (fun error _ -> 1, (error.Message, "")) (fun () ->
                    let left, right = flight.Value
                    2, (left.Value, right.Value))

            Branch.select ops fst (fun () -> selected.Value) (fun current ->
                ops.Component (fun setup ->
                    let key = fst (current ())
                    setups.Add key
                    setup.OnCleanup (fun () -> cleanups.Add key)

                    Typed.div
                        ops
                        [||]
                        [|
                            Typed.reactiveText ops (fun () -> current () |> snd |> fst)
                            Typed.reactiveText ops (fun () -> current () |> snd |> snd)
                        |])))

    let mode =
        if queued then
            QueuedCommits (CollectAndContinue, ignore)
        else
            SynchronousCommits

    let mounted =
        mountWith mode graph None ignore host None (fun context -> run view context.Backend)

    let content () =
        host.children.item(0.).Value

    let initial = content ()
    equal "loading" host.textContent

    graph.Run (fun () ->
        fst flight.Value
        |> fun source -> source.Settle "one")

    mounted.Flush ()
    equal "loading" host.textContent
    equal true (obj.ReferenceEquals (initial, content ()))
    equal [ 0 ] (List.ofSeq setups)

    graph.Run (fun () ->
        snd flight.Value
        |> fun source -> source.Settle "two")

    mounted.Flush ()
    equal "onetwo" host.textContent
    let ready = content ()
    let first, second = ready.firstChild.Value, ready.lastChild.Value
    equal false (obj.ReferenceEquals (initial, ready))

    graph.Run (fun () ->
        fst flight.Value
        |> fun source -> source.Settle "ONE")

    if queued then
        equal "onetwo" host.textContent

    mounted.Flush ()
    equal "ONEtwo" host.textContent
    equal true (obj.ReferenceEquals (ready, content ()))
    equal true (obj.ReferenceEquals (first, ready.firstChild.Value))
    equal true (obj.ReferenceEquals (second, ready.lastChild.Value))
    equal [ 0; 2 ] (List.ofSeq setups)

    graph.Run (fun () ->
        fst flight.Value
        |> fun source -> source.Settle "stale")

    graph.Run (fun () -> flight.Value <- fresh ())
    mounted.Flush ()
    equal "loading" host.textContent
    equal None ready.parentNode

    equal
        true
        (first.parentNode
         |> Option.exists (fun parent -> obj.ReferenceEquals (parent, ready)))

    equal (Some (if queued then "ONE" else "stale")) first.textContent
    equal (Some "two") second.textContent
    equal [ 0; 2 ] (List.ofSeq cleanups)

    graph.Run (fun () ->
        fst flight.Value
        |> fun source -> source.Fail (Exception "broken"))

    mounted.Flush ()
    equal "broken" host.textContent
    let failed = content ()

    graph.Run (fun () ->
        fst flight.Value
        |> fun source -> source.Fail (Exception "again"))

    mounted.Flush ()
    equal "again" host.textContent
    equal true (obj.ReferenceEquals (failed, content ()))

    graph.Run (fun () ->
        let left, right = flight.Value

        Api.batch (fun () ->
            left.Settle "re"
            right.Settle "covered"))

    mounted.Flush ()
    equal "recovered" host.textContent
    equal false (obj.ReferenceEquals (ready, content ()))
    equal [ 0; 2; 0; 1; 2 ] (List.ofSeq setups)

    graph.Run (fun () ->
        fst flight.Value
        |> fun source -> source.Settle "discarded")

    mounted.Dispose ()
    let disposedSetups = setups.Count
    graph.Run (fun () -> flight.Value <- fresh ())
    mounted.Flush ()
    equal disposedSetups setups.Count
    equal [ 0; 2; 0; 1; 2 ] (List.ofSeq cleanups)
    equal "" host.textContent

let private serverStructuralBoundary (ops: Operations<'view>) run =
    use graph = new Graph ()

    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            let left, right = Api.createAsyncSource<string>(), Api.createAsyncSource<string>()

            let selected =
                Api.createBoundary (fun _ -> 0, ("<loading>", "")) (fun error _ -> 1, (error.Message, "")) (fun () -> 2, (left.Value, right.Value))

            let view =
                Branch.select ops fst (fun () -> selected.Value) (fun current ->
                    Typed.div
                        ops
                        [||]
                        [|
                            Typed.reactiveText ops (fun () -> current () |> snd |> fst)
                            Typed.reactiveText ops (fun () -> current () |> snd |> snd)
                        |])

            let render () =
                Html.render Reject (run view)

            equal "<div>&lt;loading&gt;</div>" (render ())
            left.Settle "<one>"
            equal "<div>&lt;loading&gt;</div>" (render ())
            right.Settle "&two"
            equal "<div>&lt;one&gt;&amp;two</div>" (render ())
            left.Fail (Exception "<failed>")
            equal "<div>&lt;failed&gt;</div>" (render ())))

let private descendantFailure queued =
    use graph = new Graph ()
    let failing = graph.Run (fun () -> Api.createSignal (false, "ready"))
    let mutable recovered = 0
    let attempts = ResizeArray<bool>()
    let host = host ()

    let view =
        Tree.ops.Component (fun _ ->
            let selection =
                Api.createErrorBoundary
                    (fun _ _ ->
                        recovered <- recovered + 1
                        1)
                    (fun () -> 0)

            Branch.select Tree.ops id (fun () -> selection.Value) (fun _ ->
                Typed.reactiveText Tree.ops (fun () ->
                    let fails, value = failing.Value
                    attempts.Add fails

                    if fails then
                        failwith "descendant reader"

                    value)))

    let mode =
        if queued then
            QueuedCommits (CollectAndContinue, ignore)
        else
            SynchronousCommits

    let mounted =
        mountWith mode graph None ignore host None (fun context -> Tree.run view context.Backend)

    try
        equal "ready" host.textContent
        graph.Run (fun () -> failing.Value <- true, "failed")
        mounted.Flush ()
        equal (0, "ready", 0) (recovered, host.textContent, mounted.CommitErrors.Length)
        equal [ false; true ] (List.ofSeq attempts)
        graph.Run (fun () -> failing.Value <- false, "recovered")
        mounted.Flush ()
        equal "recovered" host.textContent
        equal [ false; true; false ] (List.ofSeq attempts)
    finally
        mounted.Dispose ()

let private collectingMount graph host (reported: ResizeArray<exn>) view =
    mount graph None reported.AddRange host None (fun context -> Program.run view context.Backend)

let private bindingReaderFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let reported = ResizeArray<exn>()
    let host = host ()

    let view =
        Program.element
            Div
            [|
                ReactiveAttribute (
                    "title",
                    fun () ->
                        if source.Value = "bad" then
                            failwith "reader"
                        else
                            Some source.Value
                )
            |]
            [| Program.bind (=) (fun () -> source.Value) |]

    let mounted = collectingMount graph host reported view
    equal 0 reported.Count
    graph.Run (fun () -> source.Value <- "bad")
    equal [ "reader" ] [ for error in reported -> error.Message ]
    equal "bad" host.textContent
    graph.Run (fun () -> source.Value <- "c")
    equal 1 reported.Count
    mounted.Dispose ()

let private sharedMemoFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal 1)

    let failing =
        graph.Run (fun () ->
            Api.createMemo (fun _ ->
                if source.Value = 2 then
                    failwith "memo"
                else
                    string source.Value))

    let reported = ResizeArray<exn>()
    let host = host ()

    let view =
        Program.fragment
            [|
                Program.bind (=) (fun () -> failing.Value)
                Program.bind (=) (fun () -> failing.Value)
            |]

    let mounted = collectingMount graph host reported view
    graph.Run (fun () -> source.Value <- 2)
    equal [ "memo" ] [ for error in reported -> error.Message ]
    mounted.Dispose ()

let private queuedReaderFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let reported = ResizeArray<exn>()
    let host = host ()

    let mounted =
        mountWith (QueuedCommits (CollectAndContinue, ignore)) graph None reported.AddRange host None (fun context ->
            Program.run
                (Program.bind (=) (fun () ->
                    if source.Value = "bad" then
                        failwith "reader"
                    else
                        source.Value))
                context.Backend)

    mounted.Flush ()
    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()
    equal [ "reader" ] [ for error in reported -> error.Message ]
    equal "a" host.textContent
    mounted.Dispose ()

let private recoveredFailureRecurs () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let failure = Exception "cached"
    let reported = ResizeArray<exn>()

    let mounted =
        collectingMount
            graph
            (host ())
            reported
            (Program.bind (=) (fun () ->
                if source.Value.StartsWith "bad" then
                    raise failure
                else
                    source.Value))

    graph.Run (fun () -> source.Value <- "bad")
    graph.Run (fun () -> source.Value <- "bad!")
    equal 1 reported.Count
    graph.Run (fun () -> source.Value <- "ok")
    graph.Run (fun () -> source.Value <- "bad")
    equal 2 reported.Count
    mounted.Dispose ()

let private keyedRowReaderFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal 2)

    let failing =
        graph.Run (fun () ->
            Api.createMemo (fun _ ->
                if source.Value = 2 then
                    failwith "memo"
                else
                    string source.Value))

    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let reported = ResizeArray<exn>()

    let view =
        Keyed.list Program.ops id (fun () -> rows.Value) (fun _ -> Program.bind (=) (fun () -> failing.Value))

    let mounted = collectingMount graph (host ()) reported view
    equal [ "memo"; "memo" ] [ for error in reported -> error.Message ]
    graph.Run (fun () -> rows.Value <- [ 1; 2; 3 ])
    equal [ "memo"; "memo"; "memo" ] [ for error in reported -> error.Message ]
    mounted.Dispose ()

let portableCases =
    [|
        "tree HTML serializes the boundary's escaped result", fun () -> serverBoundary Tree.ops Tree.run
        "program HTML serializes the boundary's escaped result", fun () -> serverBoundary Program.ops Program.run
        "tree HTML selects structural boundary snapshots", fun () -> serverStructuralBoundary Tree.ops Tree.run
        "program HTML selects structural boundary snapshots", fun () -> serverStructuralBoundary Program.ops Program.run
    |]

let private componentCleanupFailures () =
    use graph = new Graph ()
    let host = host ()
    let reports = ResizeArray<exn list>()
    let ran = ResizeArray<string>()

    let view =
        Tree.ops.Component (fun setup ->
            setup.OnCleanup (fun () ->
                ran.Add "first"
                failwith "first")

            setup.OnCleanup (fun () ->
                ran.Add "second"
                failwith "second")

            Typed.reactiveText Tree.ops (fun () -> "value"))

    let mounted =
        mount graph None reports.Add host None (fun context -> Tree.run view context.Backend)

    equal 0 reports.Count
    mounted.Dispose ()
    equal (2, 1) (ran.Count, reports.Count)
    equal [ "first"; "second" ] (reports[0] |> List.map _.Message |> List.sort)

let cases =
    Array.append
        portableCases
        [|
            "initial synchronous DOM setter failure unwinds setup", fun () -> setupFailure false
            "initial queued DOM setter failure unwinds setup", fun () -> setupFailure true
            "tree synchronous text shares core pending/error boundary", fun () -> boundary Tree.ops Tree.run false
            "tree queued text shares core pending/error boundary", fun () -> boundary Tree.ops Tree.run true
            "program synchronous text shares core pending/error boundary", fun () -> boundary Program.ops Program.run false
            "program queued text shares core pending/error boundary", fun () -> boundary Program.ops Program.run true
            "tree keyed components share queue and cancel removed rows", fun () -> nested Tree.ops Tree.run
            "program keyed components share queue and cancel removed rows", fun () -> nested Program.ops Program.run
            "mounts own independent queues on a shared graph", independentMounts
            "tree synchronous structural selection shares a core boundary", fun () -> structuralBoundary Tree.ops Tree.run false
            "tree queued structural selection shares a core boundary", fun () -> structuralBoundary Tree.ops Tree.run true
            "program synchronous structural selection shares a core boundary", fun () -> structuralBoundary Program.ops Program.run false
            "program queued structural selection shares a core boundary", fun () -> structuralBoundary Program.ops Program.run true
            "computation boundary leaves synchronous descendant read failures on the effect", fun () -> descendantFailure false
            "computation boundary leaves queued descendant read failures on the effect", fun () -> descendantFailure true
            "a throwing binding reader is reported once and other bindings update", bindingReaderFailure
            "a failed memo read by two bindings is reported once", sharedMemoFailure
            "a throwing reader is reported under queued commits", queuedReaderFailure
            "a recurring failure object is reported again after its binding recovers", recoveredFailureRecurs
            "a keyed row reading a failed memo reports the failure", keyedRowReaderFailure
            "two throwing component cleanups are reported together on disposal", componentCleanupFailures
        |]
