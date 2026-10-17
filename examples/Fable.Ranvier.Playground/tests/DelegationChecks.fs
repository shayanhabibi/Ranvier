module DelegationChecks

open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

module H = Ranvier.View.Foundation.Generated.Html

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private element (tag: string) =
    globalThis.document.createElement tag

let private mount graph host view =
    Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

[<Emit("(() => { const counts = { add: 0, remove: 0 }; const add = $0.addEventListener.bind($0), remove = $0.removeEventListener.bind($0); $0.addEventListener = (type, listener, options) => { if (type === 'click') counts.add++; add(type, listener, options); }; $0.removeEventListener = (type, listener, options) => { if (type === 'click') counts.remove++; remove(type, listener, options); }; return counts; })()")>]
let private spyClickListeners (host: HTMLElement) : obj = jsNative

[<Emit("[$0.add, $0.remove]")>]
let private listenerCounts (counts: obj) : int array = jsNative

[<Emit("$0.dispatchEvent(new FocusEvent('focus'))")>]
let private dispatchFocus (node: HTMLElement) : unit = jsNative

let private detachedHost () =
    use graph = new Graph ()
    let host = element "main"
    let mutable current: obj = null

    let view =
        H.Program.button [| H.Events.onClick (fun event -> current <- box event.currentTarget.Value) |] [||]

    let mounted = mount graph host view

    let button =
        host.querySelector "button"
        |> unbox<HTMLButtonElement>

    button.click ()
    equal true (obj.ReferenceEquals (current, button))
    mounted.Dispose ()

let private nested stop =
    use graph = new Graph ()
    let host = element "main"
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let rowClicks, outerClicks = ResizeArray<int>(), ResizeArray<int>()

    let view =
        H.Program.div
            [| H.Events.onClick (fun _ -> outerClicks.Add 0) |]
            [|
                Keyed.list Program.ops id (fun () -> rows.Value) (fun current ->
                    H.Program.button
                        [|
                            H.Events.onClick (fun event ->
                                rowClicks.Add (current ())

                                if stop then
                                    event.stopPropagation ())
                        |]
                        [||])
            |]

    let mounted = mount graph host view

    let second =
        host.querySelectorAll("button").item 1.
        |> unbox<HTMLButtonElement>

    second.click ()
    equal [ 2 ] (List.ofSeq rowClicks)
    equal (if stop then 0 else 1) outerClicks.Count
    mounted.Dispose ()

let private nonBubbling () =
    use graph = new Graph ()
    let host = element "main"
    let mutable focused = 0
    let view = H.Program.input [| H.Events.onFocus (fun _ -> focused <- focused + 1) |]
    let mounted = mount graph host view

    let input =
        host.querySelector "input"
        |> unbox<HTMLInputElement>

    dispatchFocus input
    equal 1 focused
    mounted.Dispose ()
    dispatchFocus input
    equal 1 focused

let private moved () =
    use graph = new Graph ()
    let first, second = element "main", element "main"
    let counts = spyClickListeners first
    let mutable clicks = 0

    let view =
        H.Program.button [| H.Events.onClick (fun _ -> clicks <- clicks + 1) |] [||]

    let mounted = mount graph first view

    let button =
        first.querySelector "button"
        |> unbox<HTMLButtonElement>

    mounted.MoveBefore (second, None)
    equal [| 1; 1 |] (listenerCounts counts)
    button.click ()
    equal 1 clicks
    mounted.Dispose ()

let private sharedHost () =
    use graph = new Graph ()
    let host = element "main"
    let counts = spyClickListeners host
    let view = H.Program.button [| H.Events.onClick ignore |] [||]
    let first, second = mount graph host view, mount graph host view
    equal [| 1; 0 |] (listenerCounts counts)
    first.Dispose ()
    equal [| 1; 0 |] (listenerCounts counts)
    second.Dispose ()
    equal [| 1; 1 |] (listenerCounts counts)

let private button (host: Node) index =
    (host :?> HTMLElement).querySelectorAll("button").item index
    |> unbox<HTMLButtonElement>

let private independentHosts () =
    use graph = new Graph ()
    let first, second = element "main", element "main"
    let counts = spyClickListeners second
    let mutable clicks = 0
    let a = mount graph first (H.Program.button [| H.Events.onClick ignore |] [||])

    let b =
        mount graph second (H.Program.button [| H.Events.onClick (fun _ -> clicks <- clicks + 1) |] [||])

    a.Dispose ()
    (button second 0.).click()
    equal 1 clicks
    equal [| 1; 0 |] (listenerCounts counts)
    b.Dispose ()
    equal [| 1; 1 |] (listenerCounts counts)

let private selfDisposal () =
    use graph = new Graph ()
    let host = element "main"
    let counts = spyClickListeners host
    let mutable mounted: Mounting.Mounted = Unchecked.defaultof<_>
    let mutable clicks = 0

    mounted <-
        mount
            graph
            host
            (H.Program.button
                [|
                    H.Events.onClick (fun _ ->
                        clicks <- clicks + 1
                        mounted.Dispose ())
                |]
                [||])

    let target = button host 0.
    target.click ()
    equal 1 clicks
    equal Disposed mounted.Phase
    equal [| 1; 1 |] (listenerCounts counts)
    target.click ()
    equal 1 clicks

let private remountDuringDispatch () =
    use graph = new Graph ()
    let host = element "main"
    let counts = spyClickListeners host
    let mutable outer, inner = 0, 0
    let mounts = ResizeArray<Mounting.Mounted>()

    let fresh =
        H.Program.button [| H.Events.onClick (fun _ -> inner <- inner + 1) |] [||]

    let view =
        H.Program.button
            [|
                H.Events.onClick (fun _ ->
                    outer <- outer + 1

                    if mounts.Count = 1 then
                        mounts.Add (mount graph host fresh))
            |]
            [||]

    mounts.Add (mount graph host view)
    (button host 0.).click()
    equal 1 outer
    equal 0 inner
    (button host 1.).click()
    equal 1 outer
    equal 1 inner
    (button host 0.).click()
    equal 2 outer
    equal 1 inner
    equal [| 1; 0 |] (listenerCounts counts)

    for mounted in mounts do
        mounted.Dispose ()

    equal [| 1; 1 |] (listenerCounts counts)

let private nestedHosts stop =
    use graph = new Graph ()
    let host = element "main"
    let mutable outer, inner = 0, 0

    let a =
        mount graph host (H.Program.div [| H.Events.onClick (fun _ -> outer <- outer + 1) |] [| H.Program.section [||] [||] |])

    let innerHost = host.querySelector "section" |> unbox<HTMLElement>

    let b =
        mount
            graph
            innerHost
            (H.Program.button
                [|
                    H.Events.onClick (fun event ->
                        inner <- inner + 1

                        if stop then
                            event.stopPropagation ())
                |]
                [||])

    (button innerHost 0.).click()
    equal 1 inner
    equal (if stop then 0 else 1) outer
    b.Dispose ()
    a.Dispose ()

let private directOnlyMove () =
    use graph = new Graph ()
    let first, second = element "main", element "main"
    let mutable clicks, focused = 0, 0

    let direct =
        mount graph first (H.Program.input [| H.Events.onFocus (fun _ -> focused <- focused + 1) |])

    let delegated =
        mount graph first (H.Program.button [| H.Events.onClick (fun _ -> clicks <- clicks + 1) |] [||])

    direct.MoveBefore (second, None)
    direct.Dispose ()
    (button first 0.).click()
    equal 1 clicks
    equal 0 focused
    delegated.Dispose ()

let private topLevelList () =
    use graph = new Graph ()
    let first, second = element "main", element "main"
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let clicks = ResizeArray<int>()

    let view =
        Keyed.list Program.ops id (fun () -> rows.Value) (fun current ->
            H.Program.button [| H.Events.onClick (fun _ -> clicks.Add (current ())) |] [||])

    let mounted = mount graph first view
    (button first 1.).click()
    equal [ 2 ] (List.ofSeq clicks)
    graph.Run (fun () -> rows.Value <- [ 3; 1; 2 ])
    (button first 0.).click()
    equal [ 2; 3 ] (List.ofSeq clicks)
    mounted.MoveBefore (second, None)
    equal (0., 3.) (first.childNodes.length, (second.querySelectorAll "button").length)
    (button second 1.).click()
    (button second 2.).click()
    equal [ 2; 3; 1; 2 ] (List.ofSeq clicks)
    mounted.Dispose ()
    equal 0. second.childNodes.length

/// <summary>Checks delegated event dispatch from mount hosts.</summary>
let cases =
    [|
        "delegated clicks reach handlers in a detached host with the element as currentTarget", detachedHost
        "nested keyed mounts dispatch a click once per handler", fun () -> nested false
        "stopPropagation stops delegated ancestors", fun () -> nested true
        "non-bubbling events use direct listeners removed on disposal", nonBubbling
        "moved mounts dispatch from their new host", moved
        "a shared host keeps one listener until its last mount is disposed", sharedHost
        "disposing a mount on one host leaves another host's dispatch and listener unchanged", independentHosts
        "a handler disposing its own sole mount removes the delegated listener", selfDisposal
        "a handler mounting a fresh view on its host leaves later clicks dispatching once", remountDuringDispatch
        "an inner host's handler resumes dispatch in the outer mount", fun () -> nestedHosts false
        "stopPropagation in an inner host's handler stops the outer mount's handler", fun () -> nestedHosts true
        "moving and disposing a mount with only direct listeners leaves the host's delegated dispatch once", directOnlyMove
        "rows of a top-level keyed list dispatch clicks after attach, an update and a move", topLevelList
    |]
