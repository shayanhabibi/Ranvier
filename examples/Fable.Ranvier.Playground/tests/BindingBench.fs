module BindingBench

open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Scheduling

type private BenchMount =
    {
        Dispose: unit -> unit
        Flush: unit -> unit
    }

let private now () : float =
    globalThis.performance.now ()

let private host () : HTMLElement =
    globalThis.document.createElement "div"

let private attach (node: Node) : unit =
    globalThis.document.body.appendChild (node)
    |> ignore

let private remove (node: #ChildNode) : unit =
    node.remove ()

[<Emit("globalThis.__bindingBenchSink = $0")>]
let private consume (value: obj) : unit = jsNative

let sample path queued width phase iterations =
    if path = "legacy" && queued then
        invalidArg "queued" "Legacy comparison is synchronous"

    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal 0)

    let make () =
        let contents read =
            Tree.ops.Element
                Div
                [||]
                [|
                    for _ in 1..width -> Typed.reactiveText Tree.ops (fun () -> string (read ()))
                |]

        let key value =
            if phase = "replace" then value % 2 else 0

        if path = "branch" then
            Branch.select Tree.ops key (fun () -> source.Value) contents
        elif path = "branch-list" then
            Keyed.list Tree.ops key (fun () -> [ source.Value ]) contents
        else
            Tree.ops.Element
                Div
                [||]
                [|
                    for _ in 1..width do
                        match path with
                        | "legacy" -> Tree.ops.Bind (=) (fun () -> string source.Value)
                        | "direct" -> Tree.ops.Bind (=) (fun () -> string source.Value)
                        | "helper" -> Typed.reactiveText Tree.ops (fun () -> string source.Value)
                        | _ -> invalidArg "path" path
                |]

    let view = make ()
    let host = host ()
    attach host
    let mounts = ResizeArray<BenchMount>()

    let mode =
        if queued then
            QueuedCommits (CollectAndContinue, ignore)
        else
            SynchronousCommits

    let mount () =
        if path = "legacy" then
            let old =
                BindingBaselineMounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
                    Tree.run view context.Backend)

            {
                Dispose = old.Dispose
                Flush = fun () -> graph.Run Api.flush
            }
        else
            let mounted =
                Mounting.mountWith mode graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Tree.run view context.Backend)

            {
                Dispose = mounted.Dispose
                Flush = mounted.Flush
            }

    try
        match phase with
        | "update"
        | "replace"
        | "coalesce" -> mounts.Add (mount ())
        | "dispose" ->
            for _ in 1..iterations do
                mounts.Add (mount ())
        | "construct"
        | "mount" -> ()
        | _ -> invalidArg "phase" phase

        let started = now ()

        match phase with
        | "construct" ->
            for _ in 1..iterations do
                consume (box (make ()))
        | "mount" ->
            for _ in 1..iterations do
                mounts.Add (mount ())
        | "dispose" ->
            for mounted in mounts do
                mounted.Dispose ()
        | "update" ->
            for value in 1..iterations do
                graph.Run (fun () -> source.Value <- value)

                if queued then
                    mounts[0].Flush()
        | "replace" ->
            for value in 1..iterations do
                graph.Run (fun () -> source.Value <- value)

                if queued then
                    mounts[0].Flush()
        | "coalesce" ->
            for value in 1..iterations do
                graph.Run (fun () ->
                    Api.batch (fun () ->
                        for offset in 1..100 do
                            source.Value <- (value - 1) * 100 + offset))

                if queued then
                    mounts[0].Flush()
        | _ -> ()

        let elapsed = now () - started

        if
            phase = "update"
            || phase = "replace"
            || phase = "coalesce"
        then
            let final = if phase = "coalesce" then iterations * 100 else iterations

            if
                host.textContent
                <> String.replicate width (string final)
            then
                failwith "Binding benchmark final text is incorrect"

        if phase = "dispose" && host.childNodes.length <> 0. then
            failwith "Binding benchmark disposal retained nodes"

        elapsed / float iterations
    finally
        for mounted in mounts do
            mounted.Dispose ()

        if host.childNodes.length <> 0. then
            failwith "Binding benchmark cleanup retained nodes"

        consume null
        remove host

let verify () =
    for path, queued in
        [
            "legacy", false
            "direct", false
            "helper", false
            "direct", true
            "helper", true
        ] do
        for width in [ 1; 64 ] do
            for phase in [ "construct"; "mount"; "update"; "coalesce"; "dispose" ] do
                sample path queued width phase 3 |> ignore
