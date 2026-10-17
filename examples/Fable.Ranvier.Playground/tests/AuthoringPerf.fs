module AuthoringPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

let private snapshot () =
#if RANVIER_COUNTERS
    Counters.Snapshot ()
    |> Array.map (fun (name, value) -> name, float value)
#else
    [||]
#endif

/// <summary>Retains reusable definitions until the returned cleanup releases them.</summary>
let retain mode width count =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let definitions = ResizeArray<_>()

    for _ in 1..count do
        definitions.Add (AuthoringPerfFixtures.create mode width source disabled ignore)

    fun () ->
        definitions.Clear ()
        graph.Dispose ()

/// <summary>Checks mounted output, counters, event callbacks and disposal for one authoring path.</summary>
let verify mode width =
#if RANVIER_COUNTERS
    Counters.Reset ()
#endif
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let mutable clicks = 0

    let view =
        AuthoringPerfFixtures.create mode width source disabled (fun () -> clicks <- clicks + 1)

    let constructed = snapshot ()
    let host = globalThis.document.createElement "section"

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    let initial = host.innerHTML
    let mountedCounts = snapshot ()
    let retained = host.querySelector ("input")

    (host.querySelector ("button")
     |> unbox<HTMLButtonElement>)
        .click()

    if clicks <> 1 then
        failwith "Listener did not run"

    graph.Run (fun () -> source.Value <- "blocked")

    if host.querySelector ("input") <> retained then
        failwith "Input identity changed"

    if
        (host.querySelector ("input")
         |> unbox<HTMLInputElement>)
            .value
        <> "blocked"
    then
        failwith "Input value did not update"

    if
        not
            (host.querySelector ("button")
             |> unbox<HTMLButtonElement>)
                .disabled
    then
        failwith "Memo property did not update"

    let updated = host.innerHTML
    let updatedCounts = snapshot ()

    let button =
        host.querySelector ("button")
        |> unbox<HTMLButtonElement>

    mounted.Dispose ()
    button.disabled <- false
    button.click ()
    graph.Run (fun () -> source.Value <- "disposed")

    if clicks <> 1 || host.childNodes.length <> 0. then
        failwith "Disposal retained resources"

    initial, updated, constructed, mountedCounts, updatedCounts, snapshot ()

/// <summary>Times construction, mount, independent synchronous updates or disposal.</summary>
let sample mode width phase iterations =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let make () =
        AuthoringPerfFixtures.create mode width source disabled ignore

    let view = make ()
    let host = globalThis.document.createElement "section"

    globalThis.document.body.appendChild host
    |> ignore

    let mounts = ResizeArray<Mounting.Mounted>()
    let definitions = ResizeArray<Program.View>()

    let mount () =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    try
        match phase with
        | "update" -> mounts.Add (mount ())
        | "dispose" ->
            for _ in 1..iterations do
                mounts.Add (mount ())
        | "construct"
        | "mount" -> ()
        | _ -> invalidArg "phase" phase

        let start = globalThis.performance.now ()

        match phase with
        | "construct" ->
            for _ in 1..iterations do
                definitions.Add (make ())
        | "mount" ->
            for _ in 1..iterations do
                mounts.Add (mount ())
        | "update" ->
            for i in 1..iterations do
                graph.Run (fun () -> source.Value <- if i % 2 = 0 then "a" else "blocked")
        | "dispose" ->
            for mounted in mounts do
                mounted.Dispose ()
        | _ -> ()

        let elapsed = globalThis.performance.now () - start

        if
            phase = "update"
            && (host.querySelector ("input")
                |> unbox<HTMLInputElement>)
                .value
               <> (if iterations % 2 = 0 then "a" else "blocked")
        then
            failwith "Final update is wrong"

        elapsed / float iterations
    finally
        for mounted in mounts do
            mounted.Dispose ()

        if host.childNodes.length <> 0. then
            failwith "Retained DOM after cleanup"

        host.remove ()
        definitions.Clear ()
