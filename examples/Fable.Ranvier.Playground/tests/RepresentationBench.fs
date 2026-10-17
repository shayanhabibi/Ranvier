module RepresentationBench

open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

let private now () : float =
    globalThis.performance.now ()

let private attach (node: Node) : unit =
    globalThis.document.body.appendChild (node)
    |> ignore

let private remove (node: #ChildNode) : unit =
    node.remove ()

[<Emit("globalThis.__ranvierViewSink = $0")>]
let private consume (value: obj) : unit = jsNative

let private runWith (ops: Operations<'view>) run workload sugar phase iterations =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal 0)

    let make () =
        Fixtures.create ops workload sugar (fun () -> string source.Value)

    let view = make ()
    let host = Dom.element "section" [] []
    attach host
    let disposals = ResizeArray<unit -> unit>()

    let report errors =
        failwithf "Disposal errors: %A" errors

    let mount () =
        Browser.mount graph host report None (run view)

    let mutable sink = view

    try
        match phase with
        | "dispose" ->
            for _ in 1..iterations do
                disposals.Add (mount ())
        | "update" -> disposals.Add (mount ())
        | "construct"
        | "mount" -> ()
        | _ -> invalidArg "phase" phase

        let start = now ()

        match phase with
        | "construct" ->
            for _ in 1..iterations do
                sink <- make ()
                consume (box sink)
        | "mount" ->
            for _ in 1..iterations do
                disposals.Add (mount ())
        | "dispose" ->
            for dispose in disposals do
                dispose ()
        | "update" ->
            graph.Run (fun () ->
                for value in 1..iterations do
                    source.Value <- value)
        | _ -> ()

        let elapsed = now () - start

        if
            phase = "update"
            && workload = "reactive"
            && not (host.textContent.EndsWith (string iterations))
        then
            failwith "Final binding value wrong"

        elapsed / float iterations
    finally
        consume null

        for dispose in disposals do
            dispose ()

        remove host

let sample representation workload sugar phase iterations =
    match representation with
    | "tree" -> runWith Tree.ops Tree.run workload sugar phase iterations
    | "program" -> runWith Program.ops Program.run workload sugar phase iterations
    | _ -> invalidArg "representation" representation

let private verifyWith (ops: Operations<'view>) run =
    for workload in [ "static"; "wide"; "fragments"; "reactive" ] do
        let mutable expected = None

        for sugar in [ "direct"; "functions"; "builder" ] do
            use graph = new Graph ()
            let source = graph.Run (fun () -> Api.createSignal 0)
            let view = Fixtures.create ops workload sugar (fun () -> string source.Value)
            let host = Dom.element "section" [] []
            let counts = Browser.metrics ()

            let dispose =
                Browser.mount graph host (fun errors -> failwithf "%A" errors) (Some counts) (run view)

            try
                let result =
                    host.innerHTML, counts.Owners, counts.Setups, counts.Effects, counts.Writes

                match expected with
                | None -> expected <- Some result
                | Some previous ->
                    if previous <> result then
                        failwith "Sugar changed output or resources"

                if
                    counts.Setups <> 1
                    || counts.Owners <> 2
                    || counts.Effects
                       <> (if workload = "reactive" then 1 else 0)
                then
                    failwith "Unexpected resources"

                graph.Run (fun () -> source.Value <- 1)

                if
                    counts.Writes
                    <> (if workload = "reactive" then 2 else 0)
                then
                    failwith "Update touched unrelated bindings"
            finally
                dispose ()

            if host.childNodes.length <> 0. then
                failwith "Disposal retained DOM"

let verify () =
    verifyWith Tree.ops Tree.run
    verifyWith Program.ops Program.run
