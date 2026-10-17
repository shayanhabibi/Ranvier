module AuthoringPerfMain

open System
open System.Diagnostics
open System.Text.Json
open global.Ranvier

let private sample allowGc mode width count =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let noGcRegion = not allowGc

    if
        noGcRegion
        && not (GC.TryStartNoGCRegion (64L * 1024L * 1024L))
    then
        failwith "Could not start the allocation-isolated construction sample"

    try
        let beforeCollections =
            [| for generation in 0..2 -> GC.CollectionCount generation |]

        let startBytes = GC.GetAllocatedBytesForCurrentThread ()
        let started = Stopwatch.GetTimestamp ()

        let definitions =
            Array.init count (fun _ -> AuthoringPerfFixtures.create mode width source disabled ignore)

        let elapsed = Stopwatch.GetElapsedTime started

        let bytes =
            GC.GetAllocatedBytesForCurrentThread ()
            - startBytes

        GC.KeepAlive definitions

        let collections =
            Array.init 3 (fun generation ->
                GC.CollectionCount generation
                - beforeCollections[generation])

        if noGcRegion && Array.exists ((<>) 0) collections then
            failwith "Collection occurred during an allocation-isolated sample"

        elapsed.TotalMilliseconds / float count, float bytes / float count, collections
    finally
        if noGcRegion then
            GC.EndNoGCRegion ()

[<EntryPoint>]
let main args =
    let allowGc = Array.contains "--allow-gc" args
    let rows = ResizeArray<_>()

    for width in [ 1; 16 ] do
        let iterations = if width = 1 then 5000 else 1000

        for round in -3 .. 10 do
            for offset in 0..1 do
                let mode =
                    if (round + offset + 4) % 2 = 0 then
                        "explicit"
                    else
                        "overloaded"

                let ms, bytes, collections = sample allowGc mode width iterations

                if round >= 0 then
                    rows.Add
                        {|
                            width = width
                            round = round
                            mode = mode
                            iterations = iterations
                            ms = ms
                            bytes = bytes
                            collections = collections
                        |}

    printfn
        "%s"
        (JsonSerializer.Serialize
            {|
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                noGcRegion = not allowGc
                samples = rows.ToArray ()
            |})

    0
