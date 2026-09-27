module Ranvier.Benchmarks.Program

open BenchmarkDotNet.Running
open Ranvier.Benchmarks

[<EntryPoint>]
let main argv =
    // `--short` swaps the job rather than the filter, so a smoke run covers
    // the same benchmarks as a real one and cannot drift from it.
    let isShort = argv |> Array.contains "--short"
    let config = if isShort then Config.short else Config.full
    let argv = argv |> Array.filter (fun arg -> arg <> "--short")

    BenchmarkSwitcher.FromAssembly(typeof<Signals.SignalBenchmarks>.Assembly).Run(argv, config)
    |> ignore

    0
