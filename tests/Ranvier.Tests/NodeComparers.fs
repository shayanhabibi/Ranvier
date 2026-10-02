module Ranvier.Tests.NodeComparers

open Expecto
open System
open System.Collections.Generic
open Ranvier
open Ranvier.Tests.Support

[<Tests>]
let tests =
    let behavior =
        ComparerCases.cases
        |> Array.map (fun (name, run) -> testCase name (fun _ -> run ()))
        |> Array.toList
    let allocation =
#if !FABLE_COMPILER
        [ for custom in [false; true] do
            untracedOnly <| testCase $"typed equal writes allocate nothing (custom={custom})" (fun _ ->
                use graph = new Graph()
                graph.Run(fun () ->
                    let signal =
                        if custom then createSignalWithComparer EqualityComparer<int>.Default 7
                        else createSignal 7
                    for _ in 1 .. 1000 do signal.Value <- 7
                    let before = GC.GetAllocatedBytesForCurrentThread()
                    for _ in 1 .. 10000 do signal.Value <- 7
                    let allocated = GC.GetAllocatedBytesForCurrentThread() - before
                    Expect.equal allocated 0L "typed cutoff adds no boxing or per-write allocations")) ]
#else
        []
#endif
    testList "Node comparers" (behavior @ allocation)
