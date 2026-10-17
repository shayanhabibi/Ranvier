module SnapshotBenchMain

open System
open System.Diagnostics
open System.Text.Json

[<EntryPoint>]
let main _ =
    SnapshotBench.verify ()
    let rows = ResizeArray<obj>()

    for width in [ 1; 16; 256; 1024 ] do
        for phase in [ "adapt"; "traverse"; "lookup" ] do
            let iterations =
                if phase = "lookup" then
                    max 16 (1048576 / (width * width))
                else
                    max 256 (1048576 / width)

            for round in -3 .. 10 do
                let modes = [| "list"; "array"; "array-copy" |]

                for offset in 0..2 do
                    let storage = modes[(offset + round + 3) % 3]
                    let run = SnapshotBench.workload storage phase width
                    let before = GC.GetAllocatedBytesForCurrentThread ()
                    let started = Stopwatch.GetTimestamp ()

                    for _ in 1..iterations do
                        run ()

                    let elapsed = Stopwatch.GetElapsedTime started
                    let allocated = GC.GetAllocatedBytesForCurrentThread () - before

                    if round >= 0 then
                        rows.Add (
                            box
                                {|
                                    width = width
                                    phase = phase
                                    storage = storage
                                    round = round
                                    milliseconds = elapsed.TotalMilliseconds / float iterations
                                    bytes = float allocated / float iterations
                                |}
                        )

    printfn
        "%s"
        (JsonSerializer.Serialize (
            {|
                runtime = Environment.Version.ToString ()
                configuration = "Release"
                samples = rows
            |}
        ))

    0
