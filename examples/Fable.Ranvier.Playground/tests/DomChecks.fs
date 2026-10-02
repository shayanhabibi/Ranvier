module DomChecks

open System
open Ranvier
open Fable.Ranvier
open Fable.Core.TS.Dom

let private equal label expected actual =
    if expected <> actual then failwithf "%s: expected %A, got %A" label expected actual

let private same label (expected: obj) (actual: obj) =
    if not (obj.ReferenceEquals(expected, actual)) then failwith label

let staticConstruction () =
    let child = Dom.element "strong" [] [Dom.text "inside"]
    let root = Dom.element "div" [Dom.attribute "id" "sample"] [Dom.text "before "; child]
    equal "nested text" "before inside" root.textContent
    equal "attribute" (Some "sample") (root.getAttribute "id")
    same "existing child appended" child root.lastChild.Value

let reactiveTextKeepsIdentity () =
    use graph = new Graph()
    graph.Run(fun () ->
        let value = createSignal "first"
        let child = Dom.reactiveText (fun () -> value.Value)
        let root = Dom.element "div" [] [child]
        equal "initial text" "first" root.textContent
        value.Value <- "second"
        equal "updated text" "second" root.textContent
        same "text node replaced" child root.firstChild.Value)

let optionalAttributeRemoved () =
    use graph = new Graph()
    graph.Run(fun () ->
        let title = createSignal (Some "ready")
        let root = Dom.element "div" [Dom.bindAttribute "title" (fun () -> title.Value)] []
        equal "initial attribute" (Some "ready") (root.getAttribute "title")
        title.Value <- None
        equal "attribute removed" None (root.getAttribute "title")
        title.Value <- Some "again"
        equal "attribute restored" (Some "again") (root.getAttribute "title"))

let propertyUsesDomProperty () =
    let write (element: HTMLElement) value = (unbox<HTMLInputElement> element).value <- value
    let root = Dom.element "input" [Dom.property write "seed"] []
    equal "input property" "seed" (unbox<HTMLInputElement> root).value
    equal "property is not an attribute" None (root.getAttribute "value")

let derivedPropertyAndIndependentState () =
    use graph = new Graph()
    graph.Run(fun () ->
        let count = createSignal 1
        let unrelated = createSignal "untouched"
        let doubled = createMemo (fun _ -> count.Value * 2)
        let mutable writes = 0
        let write (element: HTMLElement) value =
            writes <- writes + 1
            element.setAttribute("data-value", string value)
        let root = Dom.element "output" [Dom.bindProperty (fun () -> doubled.Value) write] []
        equal "initial derived property" (Some "2") (root.getAttribute "data-value")
        count.Value <- 3
        equal "changed derived property" (Some "6") (root.getAttribute "data-value")
        equal "two DOM writes" 2 writes
        count.Value <- 3
        unrelated.Value <- "changed"
        equal "no redundant binding writes" 2 writes)

let propertyActionDoesNotTrack () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = createSignal 1
        let incidental = createSignal 10
        let mutable writes = 0
        let write (element: HTMLElement) value =
            writes <- writes + 1
            element.setAttribute("data-value", string (value + incidental.Value))
        let root = Dom.element "output" [Dom.bindProperty (fun () -> source.Value) write] []
        incidental.Value <- 20
        equal "action read must not subscribe" 1 writes
        source.Value <- 2
        equal "subscribed source changes" 2 writes
        equal "action sees current incidental value" (Some "22") (root.getAttribute "data-value"))

let eventsAndScopeCleanup () =
    let graph = new Graph()
    let mutable calls = 0
    let root = graph.Run(fun () -> Dom.element "button" [Dom.on "click" (fun _ -> calls <- calls + 1)] [])
    root.click()
    equal "event handler called" 1 calls
    graph.Dispose()
    root.click()
    equal "handler removed by owner cleanup" 1 calls

let mountPreservesHostAndStopsBindings () =
    use graph = new Graph()
    let count = graph.Run(fun () -> createSignal 0)
    let host = Dom.createElement "div"
    let existing = Dom.text "preserved"
    host.appendChild existing |> ignore
    let mutable child = Unchecked.defaultof<Node>
    let mutable button = Unchecked.defaultof<HTMLElement>
    let handle = Mount.mount graph host (fun () ->
        child <- Dom.reactiveText (fun () -> string count.Value)
        button <- Dom.element "button" [Dom.on "click" (fun _ -> count.Value <- count.Value + 1)] [child]
        button :> Node)
    button.click()
    equal "mounted handler updates state" 1 count.Peek
    equal "mounted text updates" (Some "1") child.textContent
    handle.Dispose()
    handle.Dispose()
    same "preserved host child" existing host.firstChild.Value
    equal "only preserved content remains" "preserved" host.textContent
    count.Value <- 2
    equal "disposed text no longer updates" (Some "1") child.textContent
    button.click()
    equal "disposed event no longer writes" 2 count.Peek

let sharedGraphMountsAreIndependent () =
    use graph = new Graph()
    let count = graph.Run(fun () -> createSignal 0)
    let host = Dom.createElement "div"
    let mutable left = Unchecked.defaultof<Node>
    let mutable right = Unchecked.defaultof<Node>
    let first = Mount.mount graph host (fun () -> left <- Dom.reactiveText (fun () -> string count.Value); left)
    use second = Mount.mount graph host (fun () -> right <- Dom.reactiveText (fun () -> string count.Value); right)
    first.Dispose()
    count.Value <- 5
    equal "first scope inert" (Some "0") left.textContent
    equal "second scope still active" (Some "5") right.textContent
    same "only second mount remains" right host.firstChild.Value

let failedFactoryCleansItsOwner () =
    use graph = new Graph()
    let count = graph.Run(fun () -> createSignal 0)
    let host = Dom.createElement "div"
    let existing = Dom.text "preserved"
    host.appendChild existing |> ignore
    let mutable child = Unchecked.defaultof<Node>
    let mutable button = Unchecked.defaultof<HTMLElement>
    let mutable threw = false
    try
        Mount.mount graph host (fun () ->
            child <- Dom.reactiveText (fun () -> string count.Value)
            button <- Dom.element "button" [Dom.on "click" (fun _ -> count.Value <- count.Value + 1)] []
            raise (InvalidOperationException "factory failed")) |> ignore
    with :? InvalidOperationException as error ->
        equal "original failure" "factory failed" error.Message
        threw <- true
    equal "factory exception escapes" true threw
    same "failure leaves host content" existing host.firstChild.Value
    count.Value <- 2
    equal "failed factory text is inert" (Some "0") child.textContent
    button.click()
    equal "failed factory event is inert" 2 count.Peek

let detachedOrMovedRootCleanup () =
    use graph = new Graph()
    let host = Dom.createElement "div"
    let movedHost = Dom.createElement "div"
    let mutable calls = 0
    let root = Dom.element "button" [] []
    let scope = Mount.mount graph host (fun () -> Dom.on "click" (fun _ -> calls <- calls + 1) root; root :> Node)
    movedHost.appendChild root |> ignore
    scope.Dispose()
    equal "moved root removed from its current parent" false (movedHost.hasChildNodes())
    root.click()
    equal "moved root handler removed" 0 calls
    let detached = Dom.element "div" [] []
    let scope2 = Mount.mount graph host (fun () -> detached :> Node)
    host.removeChild detached |> ignore
    scope2.Dispose()
    equal "already detached root stays detached" None detached.parentNode

let graphDisposalCleansMount () =
    let graph = new Graph()
    let host = Dom.createElement "div"
    let root = Dom.element "div" [] []
    let scope = Mount.mount graph host (fun () -> root :> Node)
    graph.Dispose()
    scope.Dispose()
    equal "graph disposal removes mounted DOM" false (host.hasChildNodes())

let eventRestoresOwnerAndGraph () =
    use graph = new Graph()
    let count = graph.Run(fun () -> createSignal 0)
    let host = Dom.createElement "div"
    let mutable cleanupCalls = 0
    let mutable root = Unchecked.defaultof<HTMLElement>
    let mutable registered = false
    let scope = Mount.mount graph host (fun () ->
        root <- Dom.element "button" [Dom.on "click" (fun _ ->
            if not registered then
                let derived = createMemo (fun _ -> count.Value * 2)
                equal "handler has graph context" 0 derived.Value
                onCleanup (fun () -> cleanupCalls <- cleanupCalls + 1)
                registered <- true
            count.Value <- count.Value + 1)] []
        root :> Node)
    root.click()
    equal "handler state write" 1 count.Peek
    scope.Dispose()
    equal "handler resource owned by mount" 1 cleanupCalls

let fragmentRootIsRejectedWithoutLeaks () =
    use graph = new Graph()
    let count = graph.Run(fun () -> createSignal 0)
    let host = Dom.createElement "div"
    let existing = Dom.text "preserved"
    host.appendChild existing |> ignore
    let mutable child = Unchecked.defaultof<Node>
    let mutable button = Unchecked.defaultof<HTMLElement>
    let mutable rejected = false
    try
        Mount.mount graph host (fun () ->
            let fragment = Dom.document.createDocumentFragment()
            child <- Dom.reactiveText (fun () -> string count.Value)
            button <- Dom.element "button" [Dom.on "click" (fun _ -> count.Value <- count.Value + 1)] [child]
            fragment.appendChild button |> ignore
            fragment :> Node) |> ignore
    with :? ArgumentException -> rejected <- true
    equal "fragment root rejected" true rejected
    same "host sibling preserved" existing host.firstChild.Value
    equal "nothing attached" "preserved" host.textContent
    count.Value <- 2
    equal "rejected binding stopped" (Some "0") child.textContent
    button.click()
    equal "rejected listener removed" 2 count.Peek

let cases = [|
    "static nested elements preserve child nodes", staticConstruction
    "reactive text updates without replacing its node", reactiveTextKeepsIdentity
    "optional reactive attribute removes and restores", optionalAttributeRemoved
    "property assignment changes the property rather than an attribute", propertyUsesDomProperty
    "derived bindings skip equal writes and unrelated state", derivedPropertyAndIndependentState
    "DOM write phase does not capture reactive reads", propertyActionDoesNotTrack
    "events are removed with their creation scope", eventsAndScopeCleanup
    "mount preserves host siblings and stops bindings and listeners", mountPreservesHostAndStopsBindings
    "mounts sharing a graph dispose independently", sharedGraphMountsAreIndependent
    "failed factory cleans bindings and events without changing host", failedFactoryCleansItsOwner
    "moved and detached roots dispose safely", detachedOrMovedRootCleanup
    "graph disposal also cleans mounted DOM", graphDisposalCleansMount
    "event callbacks restore their mount graph and owner", eventRestoresOwnerAndGraph
    "fragment roots are rejected without leaking bindings or listeners", fragmentRootIsRejectedWithoutLeaks
|]
