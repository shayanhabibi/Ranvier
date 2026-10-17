module RangeChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Mounting

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private same a b =
    equal true (obj.ReferenceEquals (a, b))

let private rejected action =
    let mutable caught = false

    try
        action ()
    with :? InvalidOperationException ->
        caught <- true

    equal true caught

[<Emit("$0.remove()")>]
let private remove (node: Node) : unit = jsNative

[<Emit("$0.childNodes[$1]")>]
let private at (node: Node) (index: int) : Node = jsNative

[<Emit("$0.querySelector($1)")>]
let private query (node: Node) (selector: string) : HTMLElement option = jsNative

[<Emit("(() => { const old = $0.insertBefore; $0.insertBefore = function(n,b) { old.call(this,n,b); throw $1; }; return () => $0.insertBefore = old; })()")>]
let private failAttachment (node: Node) (error: exn) : unit -> unit = jsNative

[<Emit("(() => { const f = () => $1(); $0.addEventListener('click', f); return () => $0.removeEventListener('click', f); })()")>]
let private listen (node: HTMLElement) (handler: unit -> unit) : unit -> unit = jsNative

[<Emit("(() => { const old = $0.remove; $0.remove = () => { throw new Error('remove'); }; return () => $0.remove = old; })()")>]
let private failRemoval (node: Node) : unit -> unit = jsNative

let private mounted graph parent host before build =
    mount graph parent ignore host before build

let private tree graph host view =
    mounted graph None host None (fun ctx -> Tree.run view ctx.Backend)

let private sizes () =
    use graph = new Graph ()

    for view, expected in
        [
            Tree.ops.Empty, ""
            Tree.ops.Text "one", "one"
            Tree.ops.Fragment [| Tree.ops.Text "a"; Tree.ops.Empty; Tree.ops.Text "b" |], "ab"
        ] do
        let host = Dom.element "section" [] []
        let sibling = Dom.element "aside" [] []
        host.appendChild sibling |> ignore

        let handle =
            mounted graph None host (Some sibling) (fun ctx ->
                equal SettingUp (ctx.Phase ())
                equal 1. host.childNodes.length
                Tree.run view ctx.Backend
                equal 1. host.childNodes.length)

        equal Attached handle.Phase
        equal expected host.textContent
        same sibling (at host (int host.childNodes.length - 1))
        handle.Dispose ()
        handle.Dispose ()
        equal 1. host.childNodes.length
        same sibling (at host 0)

let private movement () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []
    let handle = tree graph host (Typed.button Tree.ops [||] [| Tree.ops.Text "go" |])
    let button = at host 1
    let sibling = Dom.element "aside" [] []
    other.appendChild sibling |> ignore
    handle.MoveBefore (other, Some sibling)
    equal 0. host.childNodes.length
    same button (at other 1)
    same sibling (at other 3)
    handle.MoveBefore (other, Some (at other 0))
    equal 4. other.childNodes.length
    rejected (fun () -> handle.MoveBefore (button, None))
    same button (at other 1)
    rejected (fun () -> handle.MoveBefore (host, Some sibling))
    same button (at other 1)
    handle.Dispose ()
    equal 1. other.childNodes.length
    rejected (fun () -> handle.MoveBefore (host, None))

let private mutatedBoundaries () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []
    let handle = tree graph host (Tree.ops.Text "owned")
    let foreign = Dom.element "aside" [] []
    host.insertBefore (foreign, at host 2) |> ignore
    rejected (fun () -> handle.MoveBefore (other, None))
    equal 0. other.childNodes.length
    handle.Dispose ()
    equal 1. host.childNodes.length
    same foreign (at host 0)
    let next = tree graph host (Tree.ops.Text "next")
    remove (at host 1)
    rejected (fun () -> next.MoveBefore (other, None))
    next.Dispose ()
    equal 1. host.childNodes.length

let private reparentedOwnership () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []

    let handle =
        tree graph host (Tree.ops.Fragment [| Tree.ops.Text "a"; Tree.ops.Text "b" |])

    other.appendChild (at host 1) |> ignore
    handle.Dispose ()
    equal 0. host.childNodes.length
    equal 0. other.childNodes.length

let private nativePolicies () =
    use graph = new Graph ()

    let original, host, other =
        Dom.element "section" [] [], Dom.element "section" [] [], Dom.element "section" [] []

    let a, b, c =
        Dom.element "button" [] [], Dom.element "button" [] [], Dom.element "aside" [] []

    for node in [ a; b; c ] do
        original.appendChild node |> ignore

    let handle =
        mounted graph None host None (fun ctx ->
            ctx.Native (a, Borrowed)
            ctx.Native (b, Borrowed))

    handle.MoveBefore (other, None)
    handle.Dispose ()
    same a (at original 0)
    same b (at original 1)
    same c (at original 2)
    let again = mounted graph None host None (fun ctx -> ctx.Native (b, Borrowed))
    remove c
    again.Dispose ()
    same b (at original 1)
    let owned = mounted graph None host None (fun ctx -> ctx.Native (a, Owned))
    other.appendChild a |> ignore
    owned.Dispose ()
    equal None a.parentNode
    let detached = Dom.element "button" [] []

    let borrowed =
        mounted graph None host None (fun ctx -> ctx.Native (detached, Borrowed))

    borrowed.Dispose ()
    equal None detached.parentNode

let private failures attachFailure =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let sibling = Dom.element "aside" [] []
    host.appendChild sibling |> ignore
    let signal = graph.Run (fun () -> Api.createSignal 0)
    let mutable calls, reads, cleaned = 0, 0, false
    let mutable button: HTMLElement option = None
    let error = InvalidOperationException "original"
    let restore = if attachFailure then failAttachment host error else ignore
    let mutable caught = false

    try
        try
            mounted graph None host None (fun ctx ->
                ctx.Backend.Element (
                    Button,
                    [|
                        browserModifier (fun node ->
                            let b = unbox<HTMLElement> node
                            button <- Some b
                            listen b (fun () -> calls <- calls + 1))
                    |],
                    ignore
                )

                ctx.Backend.Bind (
                    (=),
                    fun () ->
                        reads <- reads + 1
                        string signal.Value
                )

                ctx.OnCleanup (fun () -> cleaned <- true)

                if not attachFailure then
                    raise error)
            |> ignore
        with e ->
            same error e
            caught <- true
    finally
        restore ()

    equal true caught
    equal true cleaned
    equal 1. host.childNodes.length
    same sibling (at host 0)
    button.Value.click ()
    equal 0 calls
    let before = reads
    graph.Run (fun () -> signal.Value <- 1)
    equal before reads

let private cleanupOrder () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let trace = ResizeArray<string>()

    let view =
        Tree.ops.Component (fun parent ->
            parent.OnCleanup (fun () ->
                equal "x" host.textContent
                trace.Add "parent")

            Tree.ops.Component (fun child ->
                child.OnCleanup (fun () ->
                    equal "x" host.textContent
                    trace.Add "child")

                Tree.ops.Text "x"))

    let handle = tree graph host view
    handle.Dispose ()
    equal [ "child"; "parent" ] (List.ofSeq trace)
    equal 0. host.childNodes.length

let private parentAndGraph () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let parent = graph.Run (fun () -> Api.createRoot id)
    let a = mounted graph (Some parent) host None (fun ctx -> ctx.Backend.Text "a")
    let b = tree graph host (Tree.ops.Text "b")
    parent.Dispose ()
    equal Disposed a.Phase
    equal "b" host.textContent
    let c = tree graph host (Tree.ops.Text "c")
    b.Dispose ()
    equal "c" host.textContent
    graph.Dispose ()
    equal Disposed c.Phase
    equal 0. host.childNodes.length

    rejected (fun () ->
        mounted graph (Some parent) host None ignore
        |> ignore)

let private retainedCapabilities () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let mutable captured: BuildContext option = None

    let handle =
        mounted graph None host None (fun ctx ->
            captured <- Some ctx
            ctx.Backend.Text "x")

    rejected (fun () -> captured.Value.Backend.Text "late")
    rejected (fun () -> captured.Value.Native (Dom.element "div" [] [], Owned))
    equal "x" host.textContent
    handle.Dispose ()
    rejected (fun () -> captured.Value.OnCleanup ignore)
    rejected (fun () -> captured.Value.Backend.Bind ((=), fun () -> "late"))
    equal 0. host.childNodes.length

let private duplicateNative () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let node = Dom.element "button" [] []

    rejected (fun () ->
        mounted graph None host None (fun ctx ->
            ctx.Native (node, Owned)
            ctx.Native (node, Borrowed))
        |> ignore)

    equal 0. host.childNodes.length

    rejected (fun () ->
        mounted graph None host None (fun ctx -> ctx.Native (host, Borrowed))
        |> ignore)

    equal 0. host.childNodes.length

let private releaseFailures () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let node = Dom.element "button" [] []
    let restore = failRemoval node
    let mutable reports = 0

    let handle =
        mount
            graph
            None
            (fun errors ->
                reports <- reports + 1
                equal 2 errors.Length
                failwith "report")
            host
            None
            (fun ctx ->
                ctx.Native (node, Owned)
                ctx.Backend.Text "removed"
                ctx.OnCleanup (fun () -> failwith "cleanup"))

    try
        handle.Dispose ()
        equal 1 reports
        equal 3 handle.Failures.Length
        equal 1. host.childNodes.length
        same node (at host 0)
        handle.Dispose ()
        equal 1 reports
    finally
        restore ()
        remove node

let private disposedDuringSetup () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let parent = graph.Run (fun () -> Api.createRoot id)

    rejected (fun () ->
        mounted graph (Some parent) host None (fun ctx ->
            ctx.Backend.Text "x"
            parent.Dispose ()
            ctx.Backend.Text "y")
        |> ignore)

    equal 0. host.childNodes.length

let cases =
    [|
        "staged empty single and fragment ranges preserve siblings", sizes
        "range movement preserves identity and rejects invalid destinations", movement
        "mutated boundaries reject movement without deleting foreign nodes", mutatedBoundaries
        "owned nodes release after external reparenting", reparentedOwnership
        "borrowed native nodes restore original placement", nativePolicies
        "setup failure unwinds acquired listeners and bindings", fun () -> failures false
        "attachment failure after insertion unwinds resources and nodes", fun () -> failures true
        "range cleanup runs child first while DOM stays attached", cleanupOrder
        "parent and graph disposal preserve independent mounts", parentAndGraph
        "retained setup capabilities cannot mutate attached or disposed mounts", retainedCapabilities
        "duplicate or host native ownership is rejected", duplicateNative
        "release and reporter failures cannot strand remaining owned nodes", releaseFailures
        "disposal during setup cannot attach or acquire further nodes", disposedDuringSetup
    |]

[<Emit("(() => { const old = $0.insertBefore; $0.insertBefore = function(n,b) { const result = old.call(this,n,b); $1(); return result; }; return () => $0.insertBefore = old; })()")>]
let private afterAttachment (node: Node) (action: unit -> unit) : unit -> unit = jsNative

let private reactiveMoves () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []
    let source = graph.Run (fun () -> Api.createSignal 0)
    let mutable setups = 0

    let view =
        Tree.ops.Component (fun _ ->
            setups <- setups + 1
            Tree.ops.Bind ((=)) (fun () -> string (source.Value / 2)))

    let a, b = tree graph host view, tree graph other view
    equal 2 setups
    let node = at host 1
    a.MoveBefore (other, None)
    graph.Run (fun () -> source.Value <- 2)
    equal "11" other.textContent
    same node (at other 4)
    a.Dispose ()
    graph.Run (fun () -> source.Value <- 4)
    equal "2" other.textContent
    equal 2 setups
    b.Dispose ()
    equal 0. other.childNodes.length

let private keyedReconciliation () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1, "one"; 2, "two" ])
    let setups = ResizeArray<int>()
    let cleanups = ResizeArray<int>()
    let clicks = ResizeArray<int>()

    let view =
        Keyed.list Tree.ops fst (fun () -> rows.Value) (fun current ->
            Tree.ops.Component (fun context ->
                let key = fst (current ())
                setups.Add key
                context.OnCleanup (fun () -> cleanups.Add key)

                Tree.ops.Element
                    Button
                    [|
                        Attribute ("id", "row-" + string key)
                        browserModifier (fun node -> listen (unbox<HTMLElement> node) (fun () -> clicks.Add key))
                    |]
                    [| Tree.ops.Bind (=) (fun () -> string (snd (current ()))) |]))

    let handle = tree graph host view

    equal
        []
        (graph.Root.Errors
         |> Seq.map _.Message
         |> List.ofSeq)

    let find key =
        query host ("#row-" + string key)
        |> Option.defaultWith (fun () -> failwithf "Missing keyed row %d" key)

    let one, two = find 1, find 2
    equal [ 1; 2 ] (setups |> Seq.sort |> List.ofSeq)
    equal "onetwo" host.textContent

    graph.Run (fun () -> rows.Value <- [ 2, "TWO"; 3, "three"; 1, "ONE" ])
    let three = find 3
    same one (find 1)
    same two (find 2)
    equal [ 1; 2; 3 ] (setups |> Seq.sort |> List.ofSeq)
    equal [] (List.ofSeq cleanups)
    equal "TWOthreeONE" host.textContent

    graph.Run (fun () -> rows.Value <- [ 3, "three!" ])
    equal [ 1; 2 ] (cleanups |> Seq.sort |> List.ofSeq)
    equal "three!" host.textContent
    one.click ()
    two.click ()
    three.click ()
    equal [ 3 ] (List.ofSeq clicks)

    graph.Run (fun () -> rows.Value <- [ 1, "again"; 3, "three!" ])
    let replacement = find 1
    equal false (obj.ReferenceEquals (one, replacement))
    equal [ 1; 2 ] (cleanups |> Seq.sort |> List.ofSeq)
    equal [ 1; 1; 2; 3 ] (setups |> Seq.sort |> List.ofSeq)
    equal "againthree!" host.textContent

    handle.Dispose ()
    equal [ 1; 1; 2; 3 ] (cleanups |> Seq.sort |> List.ofSeq)
    equal 0. host.childNodes.length

let private keyedRootMovement () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1, "one"; 2, "two" ])

    let view =
        Tree.ops.Fragment
            [|
                Tree.ops.Text "before:"
                Keyed.list Tree.ops fst (fun () -> rows.Value) (fun current -> Tree.ops.Bind (=) (fun () -> snd (current ())))
                Tree.ops.Text ":after"
            |]

    let handle = tree graph host view
    equal "before:onetwo:after" host.textContent
    handle.MoveBefore (other, None)
    equal "" host.textContent
    equal "before:onetwo:after" other.textContent
    graph.Run (fun () -> rows.Value <- [ 2, "TWO"; 1, "ONE"; 3, "three" ])
    equal "before:TWOONEthree:after" other.textContent
    handle.MoveBefore (host, None)
    equal "" other.textContent
    equal "before:TWOONEthree:after" host.textContent
    handle.Dispose ()
    equal 0. host.childNodes.length

let private keyedForeignMutation () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []

    let view =
        Keyed.list Tree.ops fst (fun () -> [ 1, "one" ]) (fun current -> Tree.ops.Text (snd (current ())))

    let handle = tree graph host view
    let foreign = Dom.element "aside" [] []

    host.insertBefore (foreign, at host 3) |> ignore

    rejected (fun () -> handle.MoveBefore (other, None))
    equal 0. other.childNodes.length
    handle.Dispose ()
    same host foreign.parentNode.Value
    remove foreign

[<Emit("(() => { const old = $0.remove; $0.remove = () => { throw new Error('remove start'); }; return () => $0.remove = old; })()")>]
let private failRegionRemoval (node: Node) : unit -> unit = jsNative

let private keyedReleaseFailure () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []

    let view =
        Keyed.list Tree.ops fst (fun () -> [ 1, "one" ]) (fun current -> Tree.ops.Text (snd (current ())))

    let handle = tree graph host view
    let start, finish = at host 1, at host 5
    let restore = failRegionRemoval start

    try
        handle.Dispose ()
        equal None finish.parentNode
        equal 1. host.childNodes.length
        equal 1 handle.Failures.Length
    finally
        restore ()
        remove start

let private duplicateKeys () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []

    let view =
        Keyed.list Tree.ops fst (fun () -> [ 1, "first"; 1, "duplicate" ]) (fun _ -> Tree.ops.Text "row")

    let mutable caught = false

    try
        tree graph host view |> ignore
    with :? InvalidOperationException ->
        caught <- true

    equal true caught
    equal 0. host.childNodes.length

    use liveGraph = new Graph ()
    let liveHost = Dom.element "section" [] []
    let rows = liveGraph.Run (fun () -> Api.createSignal [ 1, "one"; 2, "two" ])
    let mutable setups = 0

    let liveView =
        Keyed.list Tree.ops fst (fun () -> rows.Value) (fun current ->
            Tree.ops.Component (fun _ ->
                setups <- setups + 1
                Tree.ops.Text (snd (current ()))))

    let liveHandle = tree liveGraph liveHost liveView
    let originalChildCount = liveHost.childNodes.length
    equal "onetwo" liveHost.textContent
    equal 2 setups

    liveGraph.Run (fun () -> rows.Value <- [ 1, "changed"; 1, "duplicate" ])

    equal "onetwo" liveHost.textContent
    equal originalChildCount liveHost.childNodes.length
    equal 2 setups

    liveHandle.Dispose ()

let private attachmentReentry () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let parent = graph.Run (fun () -> Api.createRoot id)
    let restore = afterAttachment host parent.Dispose

    try
        rejected (fun () ->
            mounted graph (Some parent) host None (fun ctx -> ctx.Backend.Text "x")
            |> ignore)

        equal 0. host.childNodes.length
    finally
        restore ()

[<Emit("document.body.appendChild($0)")>]
let private appendToBody (node: Node) : unit = jsNative

let private movedBeforeStaging () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let native = Dom.element "aside" [] []
    let mutable moved: HTMLElement option = None

    rejected (fun () ->
        mounted graph None host None (fun ctx ->
            ctx.Backend.Element (
                Div,
                [|
                    browserModifier (fun node ->
                        let element = unbox<HTMLElement> node
                        moved <- Some element
                        appendToBody element
                        ignore)
                |],
                ignore
            )

            ctx.Native (native, Owned))
        |> ignore)

    equal 0. host.childNodes.length
    equal None moved.Value.parentNode
    equal None native.parentNode

[<Emit("Array.from($0.childNodes, n => n.nodeType === 1 ? n.tagName : n.nodeType === 3 ? 'text:' + n.data : n.data)")>]
let private describe (node: Node) : string array = jsNative

let private stagedAfterDetachedRoot () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let sibling = Dom.element "aside" [] []
    host.appendChild sibling |> ignore

    let view =
        Tree.ops.Fragment
            [|
                Tree.ops.Text "a"
                Keyed.list Tree.ops id (fun () -> [ 1 ]) (fun current -> Tree.ops.Text (string (current ())))
                Tree.ops.Text "b"
            |]

    let handle =
        mounted graph None host (Some sibling) (fun ctx -> Tree.run view ctx.Backend)

    equal
        [|
            "ranvier:start"
            "text:a"
            "ranvier:list:start"
            "ranvier:start"
            "text:1"
            "ranvier:end"
            "ranvier:list:end"
            "text:b"
            "ranvier:end"
            "ASIDE"
        |]
        (describe host)

    handle.Dispose ()
    equal [| "ASIDE" |] (describe host)

[<Emit("(() => { const old = $0.insertBefore; let done = false; $0.insertBefore = function(n,b) { const result = old.call(this,n,b); if (!done) { done = true; $1.remove(); } return result; }; return () => $0.insertBefore = old; })()")>]
let private removeAnchorAfterFirstInsert (host: Node) (anchor: Node) : unit -> unit = jsNative

[<Emit("$0.name")>]
let private errorName (error: exn) : string = jsNative

let private anchorRemovedDuringAttach () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let kept, anchor = Dom.element "header" [] [], Dom.element "aside" [] []
    host.appendChild kept |> ignore
    host.appendChild anchor |> ignore
    let restore = removeAnchorAfterFirstInsert host anchor
    let mutable cleaned = false
    let mutable name = ""

    try
        try
            mounted graph None host (Some anchor) (fun ctx ->
                ctx.OnCleanup (fun () -> cleaned <- true)
                ctx.Backend.Text "x")
            |> ignore
        with error ->
            name <- errorName error
    finally
        restore ()

    equal "NotFoundError" name
    equal true cleaned
    equal [| "HEADER" |] (describe host)

let private overlappingMounts () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []
    let node = Dom.element "button" [] []
    let a = mounted graph None host None (fun ctx -> ctx.Native (node, Owned))

    try
        rejected (fun () ->
            mounted graph None other None (fun ctx -> ctx.Native (node, Borrowed))
            |> ignore)

        same node (at host 1)
        equal 0. other.childNodes.length
        let child = Dom.element "span" [] []
        node.appendChild child |> ignore

        rejected (fun () ->
            mounted graph None other None (fun ctx -> ctx.Native (child, Owned))
            |> ignore)

        same node child.parentNode.Value
    finally
        a.Dispose ()

let private equalityDisposal () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let parent = graph.Run (fun () -> Api.createRoot id)
    let source = graph.Run (fun () -> Api.createSignal 0)

    let handle =
        mounted graph (Some parent) host None (fun ctx ->
            ctx.Backend.Bind (
                (fun _ _ ->
                    parent.Dispose ()
                    false),
                fun () -> string source.Value
            ))

    let node = at host 1
    graph.Run (fun () -> source.Value <- 1)
    equal Disposed handle.Phase
    equal "0" node.textContent.Value
    equal 0. host.childNodes.length

let private childReporter () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let mutable mountReports = 0
    let parent = graph.Run (fun () -> Api.createRoot id)

    let handle =
        mount
            graph
            (Some parent)
            (fun errors ->
                equal [ "cleanup" ] (errors |> List.map _.Message)
                mountReports <- mountReports + 1)
            host
            None
            (fun ctx ->
                ctx.Backend.Text "x"
                ctx.OnCleanup (fun () -> failwith "cleanup"))

    handle.Dispose ()
    equal 1 mountReports
    equal 0. host.childNodes.length
    parent.Dispose ()

let private failedMovementRollback () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []
    let source = graph.Run (fun () -> Api.createSignal 0)
    let mutable reads = 0

    let handle =
        mounted graph None host None (fun ctx ->
            ctx.Backend.Bind (
                (=),
                fun () ->
                    reads <- reads + 1
                    string source.Value
            ))

    let restoreHost = failAttachment host (InvalidOperationException "rollback")
    let restoreOther = failAttachment other (InvalidOperationException "move")

    try
        let mutable caught = false

        try
            handle.MoveBefore (other, None)
        with error ->
            equal "Range movement and rollback failed" error.Message
            caught <- true

        equal true caught
        equal Disposed handle.Phase
        equal 0. host.childNodes.length
        equal 0. other.childNodes.length
        let before = reads
        graph.Run (fun () -> source.Value <- 1)
        equal before reads
    finally
        restoreHost ()
        restoreOther ()
        handle.Dispose ()

let private reparentedGeneratedDescendants () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []

    let handle =
        tree graph host (Typed.div Tree.ops [||] [| Typed.button Tree.ops [||] [| Tree.ops.Text "x" |] |])

    let wrapper = at host 1
    let button = at wrapper 0
    other.appendChild button |> ignore
    let adopted = mounted graph None other None (fun ctx -> ctx.Native (button, Owned))

    try
        handle.Dispose ()
        equal 0. host.childNodes.length
        equal "x" other.textContent
        adopted.Dispose ()
        equal 0. other.childNodes.length
    finally
        adopted.Dispose ()
        handle.Dispose ()

let private keyedReportsAndRecovery () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1, "one"; 2, "two" ])
    let reports = ResizeArray<exn>()
    let mutable setups = 0

    let view =
        Keyed.list Tree.ops fst (fun () -> rows.Value) (fun current ->
            Tree.ops.Component (fun _ ->
                setups <- setups + 1
                Tree.ops.Bind (=) (fun () -> snd (current ()))))

    let handle =
        mount graph None (List.iter reports.Add) host None (fun ctx -> Tree.run view ctx.Backend)

    let one = at host 3
    graph.Run (fun () -> rows.Value <- [ 1, "changed"; 1, "duplicate" ])
    equal "onetwo" host.textContent
    equal 2 setups
    equal 1 reports.Count
    equal true (reports[0] :? InvalidOperationException)
    graph.Run (fun () -> rows.Value <- [ 2, "TWO"; 1, "ONE" ])
    equal "TWOONE" host.textContent
    same one (at host 6)
    equal 2 setups
    equal 1 reports.Count
    handle.Dispose ()
    equal 0. host.childNodes.length

let private keyedSetupRecovery initial =
    use graph = new Graph ()
    let host = Dom.element "section" [] []

    let rows =
        graph.Run (fun () -> Api.createSignal (if initial then [ 1; 2; 3 ] else [ 1 ]))

    let reports = ResizeArray<exn>()
    let cleanups = ResizeArray<int>()
    let mutable fail = true

    let view =
        Keyed.list Tree.ops id (fun () -> rows.Value) (fun current ->
            Tree.ops.Component (fun ctx ->
                let key = current ()
                ctx.OnCleanup (fun () -> cleanups.Add key)

                if key = 3 && fail then
                    failwith "row setup"

                Tree.ops.Text (string key)))

    let build (ctx: BuildContext) =
        Tree.run view ctx.Backend

    if initial then
        let mutable caught = false

        try
            mount graph None (List.iter reports.Add) host None build
            |> ignore
        with error ->
            equal "row setup" error.Message
            caught <- true

        equal true caught
        equal 0. host.childNodes.length
        equal [ 1; 2; 3 ] (cleanups |> Seq.sort |> List.ofSeq)
    else
        let handle = mount graph None (List.iter reports.Add) host None build
        let one = at host 3
        graph.Run (fun () -> rows.Value <- [ 2; 1; 3 ])
        equal "1" host.textContent
        same one (at host 3)
        equal [ 2; 3 ] (cleanups |> Seq.sort |> List.ofSeq)
        equal [ "row setup" ] (reports |> Seq.map _.Message |> List.ofSeq)
        fail <- false
        graph.Run (fun () -> rows.Value <- [ 3; 1; 2 ])
        equal "312" host.textContent
        same one (at host 6)
        handle.Dispose ()
        equal [ 1; 2; 2; 3; 3 ] (cleanups |> Seq.sort |> List.ofSeq)
        equal 0. host.childNodes.length

let private keyedLayoutFailure () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let reports = ResizeArray<exn>()
    let cleanups = ResizeArray<int>()

    let view =
        Keyed.list Tree.ops id (fun () -> rows.Value) (fun current ->
            Tree.ops.Component (fun ctx ->
                let key = current ()
                ctx.OnCleanup (fun () -> cleanups.Add key)
                Tree.ops.Text (string key)))

    let handle =
        mount graph None (List.iter reports.Add) host None (fun ctx -> Tree.run view ctx.Backend)

    let mutable inserts = 0

    let restore =
        afterAttachment host (fun () ->
            inserts <- inserts + 1

            if inserts = 1 then
                failwith "insert")

    try
        graph.Run (fun () -> rows.Value <- [ 3; 2; 1 ])
    finally
        restore ()

    equal 1 reports.Count
    equal Disposed handle.Phase
    equal 0. host.childNodes.length
    equal [ 1; 2; 3 ] (cleanups |> Seq.sort |> List.ofSeq)

let private keyedNestedMovement () =
    use graph = new Graph ()
    let host, other = Dom.element "section" [] [], Dom.element "section" [] []

    let rows =
        graph.Run (fun () -> Api.createSignal [ 1, [ 10, "a"; 11, "b" ]; 2, [ 20, "c" ] ])

    let setups = ResizeArray<int>()
    let cleanups = ResizeArray<int>()

    let view =
        Keyed.list Tree.ops fst (fun () -> rows.Value) (fun outer ->
            Keyed.list Tree.ops fst (fun () -> snd (outer ())) (fun inner ->
                Tree.ops.Component (fun ctx ->
                    let key = fst (inner ())
                    setups.Add key
                    ctx.OnCleanup (fun () -> cleanups.Add key)

                    Tree.ops.Fragment
                        [|
                            Tree.ops.Element
                                Button
                                [| Attribute ("id", "nested-" + string key) |]
                                [| Tree.ops.Bind (=) (fun () -> snd (inner ())) |]
                            Tree.ops.Text "!"
                        |])))

    let handle = tree graph host view
    equal "a!b!c!" host.textContent

    let a, b, c =
        query host "#nested-10", query host "#nested-11", query host "#nested-20"

    handle.MoveBefore (other, None)
    graph.Run (fun () -> rows.Value <- [ 2, [ 20, "C"; 21, "d" ]; 1, [ 11, "B"; 10, "A" ] ])
    equal "C!d!B!A!" other.textContent
    same a.Value (query other "#nested-10").Value
    same b.Value (query other "#nested-11").Value
    same c.Value (query other "#nested-20").Value
    equal [ 10; 11; 20; 21 ] (setups |> Seq.sort |> List.ofSeq)
    equal [] (List.ofSeq cleanups)
    handle.MoveBefore (host, None)
    graph.Run (fun () -> rows.Value <- [ 1, [ 10, "last" ] ])
    equal "last!" host.textContent
    same a.Value (query host "#nested-10").Value
    equal [ 11; 20; 21 ] (cleanups |> Seq.sort |> List.ofSeq)
    handle.Dispose ()
    equal [ 10; 11; 20; 21 ] (cleanups |> Seq.sort |> List.ofSeq)
    equal 0. host.childNodes.length
    equal 0. other.childNodes.length

let private keyedDisposal point =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let parent = graph.Run (fun () -> Api.createRoot id)
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let reports = ResizeArray<exn>()
    let cleanups = ResizeArray<int>()
    let mutable updating = false
    let mutable setups = 0

    let dispose atPoint =
        if updating && point = atPoint then
            parent.Dispose ()

    let view =
        Keyed.list
            Tree.ops
            id
            (fun () ->
                dispose "read"
                rows.Value)
            (fun current ->
                Tree.ops.Component (fun ctx ->
                    let key = current ()
                    setups <- setups + 1

                    ctx.OnCleanup (fun () ->
                        cleanups.Add key
                        dispose "cleanup")

                    dispose "setup"
                    Tree.ops.Bind (=) (fun () -> string (current ()))))

    let handle =
        mount graph (Some parent) (List.iter reports.Add) host None (fun ctx -> Tree.run view ctx.Backend)

    updating <- true
    let restore = afterAttachment host (fun () -> dispose "move")

    try
        graph.Run (fun () -> rows.Value <- [ 3; 2; 4 ])
    finally
        restore ()

    equal Disposed handle.Phase
    equal 0. host.childNodes.length

    let expected =
        if point = "read" then [ 1; 2 ]
        elif point = "setup" then [ 1; 2; 3 ]
        else [ 1; 2; 3; 4 ]

    equal expected (cleanups |> Seq.sort |> List.ofSeq)
    equal 0 reports.Count
    let before = setups
    graph.Run (fun () -> rows.Value <- [ 4; 5 ])
    equal before setups
    equal 0. host.childNodes.length

let private keyedReporter throws =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let unrelated = graph.Run (fun () -> Api.createSignal 0)
    let mutable reports = 0

    let report _ =
        reports <- reports + 1
        unrelated.Value |> ignore

        if throws then
            failwith "reporter"

    let view =
        Keyed.list Tree.ops id (fun () -> rows.Value) (fun current -> Tree.ops.Text (string (current ())))

    let handle =
        mount graph None report host None (fun ctx -> Tree.run view ctx.Backend)

    let one = at host 3
    graph.Run (fun () -> rows.Value <- [ 1; 1 ])
    equal 1 reports
    graph.Run (fun () -> unrelated.Value <- 1)
    equal 1 reports
    equal "12" host.textContent
    graph.Run (fun () -> rows.Value <- [ 2; 1 ])
    equal "21" host.textContent
    same one (at host 6)
    equal 1 reports
    handle.Dispose ()
    equal 0. host.childNodes.length

let private keyedTracksOnlySnapshot () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let unrelated = graph.Run (fun () -> Api.createSignal 0)
    let mutable reads = 0

    let view =
        Keyed.list
            Tree.ops
            id
            (fun () ->
                reads <- reads + 1
                rows.Value)
            (fun current ->
                Tree.ops.Component (fun ctx ->
                    ctx.OnCleanup (fun () -> unrelated.Value |> ignore)
                    Tree.ops.Text (string (current ()))))

    let handle = tree graph host view
    graph.Run (fun () -> rows.Value <- [ 1 ])
    equal "1" host.textContent
    let before = reads
    graph.Run (fun () -> unrelated.Value <- 1)
    equal before reads
    handle.Dispose ()

let private keyedInitialDisposal () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let parent = graph.Run (fun () -> Api.createRoot id)
    let mutable rendered = 0

    let view =
        Keyed.list
            Tree.ops
            id
            (fun () ->
                parent.Dispose ()
                [ 1; 2; 3 ])
            (fun _ ->
                rendered <- rendered + 1
                Tree.ops.Empty)

    rejected (fun () ->
        mounted graph (Some parent) host None (fun ctx -> Tree.run view ctx.Backend)
        |> ignore)

    equal 0 rendered
    equal 0. host.childNodes.length
    equal true parent.IsDisposed

let private keyedSetupAndCleanupFailure () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1 ])
    let reports = ResizeArray<exn list>()

    let view =
        Keyed.list Tree.ops id (fun () -> rows.Value) (fun current ->
            Tree.ops.Component (fun ctx ->
                let key = current ()

                if key = 3 then
                    ctx.OnCleanup (fun () -> failwith "row cleanup")
                    failwith "row setup"

                Tree.ops.Text (string key)))

    let handle =
        mount graph None reports.Add host None (fun ctx -> Tree.run view ctx.Backend)

    let one = at host 3
    graph.Run (fun () -> rows.Value <- [ 2; 1; 3 ])
    equal "1" host.textContent
    same one (at host 3)
    equal 1 reports.Count
    equal [ "row setup"; "row cleanup" ] (reports[0] |> List.map _.Message)
    graph.Run (fun () -> rows.Value <- [ 2; 1 ])
    equal "21" host.textContent
    same one (at host 6)
    handle.Dispose ()
    equal 0. host.childNodes.length

let private arrayKeyMutation () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let slots = [| 1, "one"; 2, "two" |]
    let mutable reads = 0

    let view =
        Keyed.array
            Tree.ops
            (fun (key, _) ->
                if key = 1 then
                    slots[1] <- 9, "changed"

                key)
            (fun () ->
                reads <- reads + 1
                slots)
            (fun current -> Typed.reactiveText Tree.ops (fun () -> snd (current ())))

    let handle = tree graph host view
    equal "onetwo" host.textContent
    equal 1 reads
    handle.Dispose ()
    equal 0. host.childNodes.length

let private authoredSwitch () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let selected = graph.Run (fun () -> Api.createSignal (1, "a"))
    let log = ResizeArray<string>()

    let view =
        Branch.select Tree.ops fst (fun () -> selected.Value) (fun current ->
            Tree.ops.Component (fun ctx ->
                let key = fst (current ())
                log.Add ("setup " + string key)
                ctx.OnCleanup (fun () -> log.Add ("cleanup " + string key))
                Tree.ops.Bind (=) (fun () -> snd (current ()))))

    let handle = tree graph host view
    graph.Run (fun () -> selected.Value <- (1, "b"))
    equal "b" host.textContent
    graph.Run (fun () -> selected.Value <- (2, "c"))
    equal "c" host.textContent
    equal [ "setup 1"; "cleanup 1"; "setup 2" ] (List.ofSeq log)
    handle.Dispose ()
    equal 0. host.childNodes.length

let allCases =
    Array.append
        cases
        [|
            "an authored switch disposes the previous arm before the next arm's setup and keeps an equal key", authoredSwitch
            "array key callbacks cannot change unread snapshot slots", arrayKeyMutation
            "reusable bindings retain identity across range movement", reactiveMoves
            "keyed rows preserve identity and current data across reorder and removal", keyedReconciliation
            "top-level keyed regions retain sibling order and move with their mount", keyedRootMovement
            "foreign mutations inside keyed rows reject outer movement", keyedForeignMutation
            "keyed range release attempts both endpoints after a removal failure", keyedReleaseFailure
            "duplicate keys fail before mounting mutates the host", duplicateKeys
            "disposal during attachment cannot revive a mount", attachmentReentry
            "native ownership cannot overlap another live mount", overlappingMounts
            "staging rejects a root moved elsewhere by a browser hook and unwinds", movedBeforeStaging
            "detached roots keep their order and before sibling when a keyed region stages them", stagedAfterDetachedRoot
            "an anchor removed during attachment fails with NotFoundError and unwinds the mount", anchorRemovedDuringAttach
            "equality-triggered disposal prevents a late DOM write", equalityDisposal
            "direct child mount disposal uses its explicit reporter", childReporter
            "failed movement rollback disposes resources instead of stranding bindings", failedMovementRollback
            "reparented generated descendants can be adopted and outlive their mount", reparentedGeneratedDescendants
            "duplicate keyed updates report once and recover without replacing retained rows", keyedReportsAndRecovery
            "initial keyed row setup failure unwinds the entire mount", fun () -> keyedSetupRecovery true
            "failed live row setup retains prior rows and permits recovery", fun () -> keyedSetupRecovery false
            "a failed keyed layout disposes the mount and reports once", keyedLayoutFailure
            "nested keyed rows update and dispose after outer movement", keyedNestedMovement
            "keyed reader disposal prevents further reconciliation", fun () -> keyedDisposal "read"
            "keyed row setup disposal cannot attach new rows", fun () -> keyedDisposal "setup"
            "keyed movement disposal cannot revive row ranges", fun () -> keyedDisposal "move"
            "keyed row cleanup disposal cancels the remaining update", fun () -> keyedDisposal "cleanup"
            "keyed error reporting does not subscribe to reporter reads", fun () -> keyedReporter false
            "throwing keyed reporters cannot prevent later recovery or disposal", fun () -> keyedReporter true
            "keyed reconciliation tracks snapshots but not comparers or cleanup", keyedTracksOnlySnapshot
            "initial keyed reader disposal stops before row callbacks", keyedInitialDisposal
            "keyed setup and its cleanup failure report together and recover", keyedSetupAndCleanupFailure
        |]
