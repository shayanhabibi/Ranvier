#r "../../src/Ranvier/bin/Release/net10.0/Ranvier.dll"
#r "../../examples/Fable.Ranvier.Playground/tests/bin/Release/net10.0/DomTests.dll"

open System
open System.Text.Json

for name, test in ComponentPortableChecks.cases do
    test ()
    printfn "PASS %s" name

let samples =
    [|
        for representation in [ "tree"; "program" ] do
            for reactive in [ false; true ] do
                for helper in [ false; true ] do
                    ComponentBench.definitions representation helper reactive 1000
                    |> ignore

                    for sample in 1..7 do
                        GC.Collect ()
                        let before = GC.GetAllocatedBytesForCurrentThread ()
                        let values = ComponentBench.definitions representation helper reactive 100000
                        let bytes = GC.GetAllocatedBytesForCurrentThread () - before
                        GC.KeepAlive values

                        yield
                            {|
                                representation = representation
                                reactive = reactive
                                helper = helper
                                sample = sample
                                bytesPerDefinition = float bytes / 100000.
                            |}
    |]

printfn
    "%s"
    (JsonSerializer.Serialize
        {|
            runtime = Environment.Version.ToString ()
            count = 100000
            samples = samples
        |})
