module Ranvier.Tests.Tracing

open System.Reflection
open Expecto
open Ranvier

let private traced () =
    typeof<Graph>.Assembly.GetCustomAttributes(typeof<AssemblyMetadataAttribute>, false)
    |> Array.exists (fun a ->
        let a = a :?> AssemblyMetadataAttribute
        a.Key = "RanvierTrace" && a.Value = "true")

exception private Boom

[<Tests>]
let tests =
    testList
        "Tracing"
        [
            test "the assembly is traced exactly when RANVIER_TRACE is defined" {
#if RANVIER_TRACE
                let expected = true
#else
                let expected = false
#endif
                Expect.equal (traced ()) expected "AssemblyMetadata(\"RanvierTrace\") must match the test build's define"
            }

            test "named returns the thunk's result" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = Trace.named "total" (fun () -> Signal (g, 3))
                Expect.equal s.Value 3 "named must return what the thunk returns"
                Expect.equal (Trace.named "answer" (fun () -> 42)) 42 "named must return a plain value"
            }

#if RANVIER_TRACE
            test "a new graph records GraphNew first, naming the root owner" {
                use g = new Graph ()
                let events = Trace.events g
                Expect.isNonEmpty events "a graph must record its construction"
                let first = events[0]
                Expect.equal first.Kind TraceEventKind.GraphNew "the first event must be GraphNew"
                Expect.equal first.Seq 1 "the clock must start at 1"
                Expect.equal first.Other 1 "the root owner must take owner id 1"
            }

            test "named pops its label when the thunk throws" {
                use g = new Graph ()
                use _ = g.Activate ()

                let result =
                    Trace.named "outer" (fun () ->
                        try
                            Trace.named "inner" (fun () -> raise Boom)
                        with Boom ->
                            ()

                        7)

                Expect.equal result 7 "the outer thunk must complete"

                let labels =
                    Trace.events g
                    |> Array.filter (fun e -> e.Kind = TraceEventKind.Label)
                    |> Array.map (fun e -> e.Node, string e.Payload)

                Expect.equal labels [| 0, "inner"; 0, "outer" |] "each label must close once, innermost first"
            }
#endif
        ]
