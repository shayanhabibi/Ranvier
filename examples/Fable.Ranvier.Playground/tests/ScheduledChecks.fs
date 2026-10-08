module ScheduledChecks

open System
open global.Ranvier
open Fable.Ranvier
open Fable.Core.TS.Dom

type Fixture =
    {
        Host: HTMLElement
        Root: HTMLElement
        Set: int -> unit
        SetMode: int -> unit
        Resolve: string -> unit
        SetName: string -> unit
        SetOther: int -> unit
        Other: unit -> int
        RegisterCleanup: unit -> unit
        Cleanups: unit -> int
        Batch: (unit -> unit) -> unit
        Writes: unit -> int
        Computes: unit -> int
        Errors: unit -> string array
        OnWrite: (string -> unit) -> unit
        Flush: unit -> unit
        Dispose: unit -> unit
        DisposeGraph: unit -> unit
    }

let private createOnGraph (graph: Graph) (synchronous: bool) (batchEvents: bool) =
    let source, mode, name, pending =
        graph.Run (fun () -> createSignal 0, createSignal 0, createSignal "Ada", createAsyncSource<string>())

    let host = Dom.createElement "div"
    let mutable root = Unchecked.defaultof<HTMLElement>
    let mutable writes = 0
    let mutable computes = 0
    let mutable cleanups = 0
    let other = graph.Run (fun () -> createSignal 0)
    let mutable onWrite: string -> unit = ignore

    let read () =
        match mode.Value with
        | 1 -> pending.Value
        | 2 -> failwith "reader failed"
        | _ -> string source.Value

    let write (node: HTMLElement) value =
        if value = "13" then
            failwith "setter failed"

        writes <- writes + 1
        node.setAttribute ("data-value", value)
        onWrite value

    let options =
        {
            Scheduling = (if synchronous then Synchronous else Microtask)
            BatchEvents = batchEvents
        }

    let mounted =
        Mount.mountWith options graph host (fun () ->
            let doubled =
                createMemo (fun _ ->
                    computes <- computes + 1
                    source.Value * 2)

            root <-
                Dom.element
                    "div"
                    [
                        Dom.bindProperty read write
                        Dom.bindAttribute "title" (fun () ->
                            if source.Value < 0 then
                                None
                            else
                                Some (string source.Value))
                        Dom.bindProperty (fun () -> doubled.Value) (fun node value -> node.setAttribute ("data-doubled", string value))
                    ]
                    [
                        Dom.reactiveText read
                        Dom.element
                            "button"
                            [
                                Dom.on "click" (fun _ ->
                                    source.Value <- 1
                                    source.Value <- 2)
                            ]
                            [ Dom.text "update" ]
                        Dom.element
                            "input"
                            [
                                Dom.bindProperty (fun () -> name.Value) (fun node value ->
                                    let input = unbox<HTMLInputElement> node

                                    if input.value <> value then
                                        input.value <- value)
                                Dom.on "input" (fun event -> name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                            ]
                            []
                    ]

            root :> Node)

    {
        Host = host
        Root = root
        Set = fun value -> source.Value <- value
        SetMode = fun value -> mode.Value <- value
        Resolve = pending.Settle
        SetName = fun value -> name.Value <- value
        SetOther = fun value -> other.Value <- value
        Other = fun () -> other.Value
        RegisterCleanup = fun () -> onCleanup (fun () -> cleanups <- cleanups + 1)
        Cleanups = fun () -> cleanups
        Batch = fun body -> graph.Run (fun () -> batch body)
        Writes = fun () -> writes
        Computes = fun () -> computes
        Errors =
            fun () ->
                mounted.Errors
                |> Array.map (fun error -> error.Message)
        OnWrite = fun handler -> onWrite <- handler
        Flush = mounted.Flush
        Dispose = fun () -> mounted.Dispose ()
        DisposeGraph = fun () -> graph.Dispose ()
    }

let create synchronous batchEvents =
    createOnGraph (new Graph ()) synchronous batchEvents

let createShared () =
    let graph = new Graph ()
    [| createOnGraph graph false false; createOnGraph graph false false |]

let createBatched () =
    let graph = new Graph ()
    graph.Run (fun () -> batch (fun () -> createOnGraph graph false false))
