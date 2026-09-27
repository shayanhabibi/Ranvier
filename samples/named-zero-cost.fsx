// Gate 1 of tools/verify-trace.fsx compiles this file twice into a throwaway project, with and without the
// NAMED define, against the untraced library. The two builds must compile to the same IL and the same JS.
#if INTERACTIVE
#r "../src/Ranvier/bin/Release/net10.0/Ranvier.dll"
#endif

module NamedZeroCost

open Ranvier

let build (graph: Graph) =
    use _ = graph.Activate ()
    let count = createSignal 1

    let total =
#if NAMED
        Trace.named "total" (fun () -> createMemo (fun () -> count.Value * 2))
#else
        createMemo (fun () -> count.Value * 2)
#endif

    count.Value <- 3
    total.Value
