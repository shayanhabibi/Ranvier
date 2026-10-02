module App

open System
open Fable.Core.TS.Dom
open Fable.Ranvier
open global.Ranvier

let private node tag modifiers children : Node = Dom.element tag modifiers children
let private css value = Dom.attribute "class" value
let private testId value = Dom.attribute "data-testid" value

let private setInputValue (element: HTMLElement) value =
    let input = unbox<HTMLInputElement> element
    if input.value <> value then input.value <- value

let private setDisabled (element: HTMLElement) value =
    (unbox<HTMLButtonElement> element).disabled <- value

let start () : unit -> unit =
    let graph = new Graph()
    try
        let host =
            Dom.document.getElementById("app")
            |> Option.defaultWith (fun () -> invalidOp "The playground needs an #app host.")
        let count, name, mounted, queued = graph.Run(fun () -> createSignal 0, createSignal "world", createSignal true, createSignal false)
        let demoHost = Dom.createElement "div"
        let mutable demo: DomMount option = None

        let mountDemo () =
            let options = { Scheduling = (if queued.Value then Microtask else Synchronous); BatchEvents = queued.Value }
            demo <- Some (Mount.mountWith options graph demoHost (fun () ->
                let doubled = createMemo (fun _ -> count.Value * 2)
                node "section"
                    [css "demo-grid"; testId "demo-root"
                     Dom.bindAttribute "data-parity" (fun () -> Some (if count.Value % 2 = 0 then "even" else "odd"))]
                    [node "article" [css "panel counter-panel"]
                        [node "div" [css "panel-heading"]
                            [node "span" [css "eyebrow"] [Dom.text "01 / Counter"]
                             node "span" [css "tag"] [Dom.text "signal → text"]]
                         node "output" [css "count"; testId "count"; Dom.attribute "aria-live" "polite"]
                            [Dom.reactiveText (fun () -> string count.Value)]
                         node "div" [css "derived"]
                            [node "span" [] [Dom.text "Doubled"]
                             node "output" [testId "doubled"] [Dom.reactiveText (fun () -> string doubled.Value)]]
                         node "div" [css "actions"]
                            [node "button" [css "primary"; testId "increment"; Dom.attribute "type" "button"
                                            Dom.on "click" (fun _ -> count.Value <- count.Value + 1)]
                                [Dom.text "Increment +1"]
                             node "button" [css "secondary"; testId "reset"; Dom.attribute "type" "button"
                                            Dom.bindProperty (fun () -> count.Value = 0) setDisabled
                                            Dom.on "click" (fun _ -> count.Value <- 0)] [Dom.text "Reset"]
                             node "button" [css "secondary"; testId "burst"; Dom.attribute "type" "button"
                                            Dom.on "click" (fun _ ->
                                                for _ in 1 .. 100 do count.Value <- count.Value + 1)] [Dom.text "Burst +100"]]
                         node "p" [css "note"] [Dom.text "The output changes. The DOM nodes stay."]]
                     node "article" [css "panel name-panel"]
                        [node "div" [css "panel-heading"]
                            [node "span" [css "eyebrow"] [Dom.text "02 / Input"]
                             node "span" [css "tag"] [Dom.text "event → signal"]]
                         node "label" [Dom.attribute "for" "name-field"] [Dom.text "Your name"]
                         node "input" [Dom.attribute "id" "name-field"; Dom.attribute "type" "text"
                                       Dom.attribute "autocomplete" "off"; testId "name"
                                       Dom.bindProperty (fun () -> name.Value) setInputValue
                                       Dom.on "input" (fun event ->
                                           name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)] []
                         node "p" [css "greeting"; testId "greeting"; Dom.attribute "aria-live" "polite"]
                            [Dom.reactiveText (fun () -> $"Hello, {name.Value}")]
                         node "p" [css "note"] [Dom.text "Type anywhere in the field. Focus and selection are preserved."]]]))

        let toggle () =
            match demo with
            | Some current ->
                current.Dispose()
                demo <- None
                mounted.Value <- false
            | None ->
                mountDemo ()
                mounted.Value <- true

        let toggleScheduling () =
            queued.Value <- not queued.Value
            match demo with
            | Some current ->
                current.Dispose()
                demo <- None
                mountDemo ()
            | None -> ()

        let shell = Mount.mount graph host (fun () ->
            node "div" [css "playground"]
                [node "header" [css "page-heading"]
                    [node "div" [css "brand"]
                        [node "span" [css "brand-mark"] [Dom.text "r"]
                         node "span" [] [Dom.text "Fable.Ranvier"]]
                     node "span" [css "preview"] [Dom.text "DOM PoC"]]
                 node "section" [css "intro"]
                    [node "p" [css "eyebrow"] [Dom.text "F# · Fable · Browser DOM"]
                     node "h1" [] [Dom.text "Create once."; node "br" [] []; node "span" [] [Dom.text "Update what changes."]]
                     node "p" [css "lead"] [Dom.text "A small playground for reactive text, properties and events. No component tree rebuild on each edit."]]
                 node "div" [css "mount-bar"]
                    [node "span" [testId "scheduling-status"]
                        [Dom.reactiveText (fun () -> if queued.Value then "Microtask DOM commits · batched events" else "Synchronous DOM commits")]
                     node "button" [css "secondary"; testId "scheduling-toggle"; Dom.attribute "type" "button"; Dom.on "click" (fun _ -> toggleScheduling ())]
                        [Dom.reactiveText (fun () -> if queued.Value then "Use synchronous" else "Use microtasks")]
                     node "button" [css "secondary"; testId "flush-dom"; Dom.attribute "type" "button"
                                    Dom.on "click" (fun _ -> demo |> Option.iter (fun current -> current.Flush()))] [Dom.text "Flush DOM"]]
                 node "div" [css "mount-bar"]
                    [node "div" [css "mount-state"]
                        [node "span" [css "status-dot"; Dom.bindAttribute "data-active" (fun () -> if mounted.Value then Some "true" else None)] []
                         node "span" [testId "mount-status"] [Dom.reactiveText (fun () -> if mounted.Value then "Demo mounted" else "Demo unmounted")]]
                     node "button" [css "secondary toggle"; testId "mount-toggle"; Dom.attribute "type" "button"; Dom.on "click" (fun _ -> toggle ())]
                        [Dom.reactiveText (fun () -> if mounted.Value then "Unmount demo" else "Remount demo")]]
                 demoHost
                 node "section" [css "empty-state"; Dom.bindAttribute "hidden" (fun () -> if mounted.Value then Some "" else None)]
                    [node "h2" [] [Dom.text "The mount is disposed."]
                     node "p" [] [Dom.text "Its nodes, bindings and event listeners have been removed. Remount to continue with the same state."]]
                 node "footer" [] [Dom.text "Built with Xantham DOM bindings and Ranvier scopes. Experimental API; DOM scheduling is opt-in."]])
        mountDemo ()
        let mutable stopped = false
        fun () ->
            if not stopped then
                stopped <- true
                demo |> Option.iter (fun scope -> scope.Dispose())
                demo <- None
                shell.Dispose()
                graph.Dispose()
    with _ ->
        graph.Dispose()
        reraise()
