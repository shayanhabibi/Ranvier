// Gate 1 of tools/verify-trace.fsx compiles this file twice into a throwaway project, with and without the
// LABEL define, against the untraced library. The two builds must compile to the same IL and the same JS.
#if INTERACTIVE
#r "../src/Ranvier/bin/Release/net10.0/Ranvier.dll"
#endif

module LabelZeroCost

open Ranvier

let build (graph: Graph) (i: int) =
    use _ = graph.Activate ()
    let count = createSignal i
    let total = createMemo (fun () -> count.Value * 2)

#if LABEL
    Trace.label (graph, total, $"total {i} of {count.Value}")
#endif

    count.Value <- 3
    total.Value
