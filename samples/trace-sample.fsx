// Prints the trace dump, the snapshot and two cause chains for a small graph.
// Needs a traced build:
//   dotnet build src/Ranvier -c Release -p:RanvierTrace=true
// Run: dotnet fsi samples/trace-sample.fsx [jsonl|text|why|all]
// Gate 2 of tools/verify-trace.fsx runs it 5 times in jsonl mode and requires byte-identical output.
#if INTERACTIVE
#r "../src/Ranvier/bin/Release/net10.0/Ranvier.dll"
#endif

#if !INTERACTIVE
module TraceSample
#endif

open Ranvier

/// <summary>The sample graph after its two writes, with the nodes the sections report on.</summary>
type Sample =
    {
        Graph: Graph
        Log: INode
        Sum: INode
    }

/// <summary>Builds the sample graph on a new <c>Graph</c> and writes <c>count</c> and <c>items</c> once each.</summary>
let run () : Sample =
    let graph = new Graph ()
    use _ = graph.Activate ()

    let count, items, log, sum =
        createRoot (fun _ ->
            let count = Trace.named "count" (fun () -> createSignal 1)
            let doubled = Trace.named "doubled" (fun () -> createMemo (fun _ -> count.Value * 2))
            let log = Trace.named "log" (fun () -> new Effect (graph, (fun () -> ignore doubled.Value)) :> INode)
            let items = Trace.named "items" (fun () -> createSignal [ "a", 1; "b", 2; "c", 3 ])

            let rows =
                Trace.named "rows" (fun () -> createProjection fst (fun (_, v) -> v * 10) (fun () -> items.Value))

            let sum =
                Trace.named "sum" (fun () -> new Effect (graph, (fun () -> rows.Keys |> Array.sumBy rows.Get |> ignore)) :> INode)

            count, items, log, sum)

    count.Value <- 2
    items.Value <- [ "a", 1; "b", 20; "c", 3 ]
    { Graph = graph; Log = log; Sum = sum }

/// <summary>The text for <c>mode</c>: <c>jsonl</c>, <c>text</c>, <c>why</c> or <c>all</c>.</summary>
let sections (sample: Sample) (mode: string) : string =
    let g = sample.Graph
    use _ = g.Activate ()

    let all =
        [
            "jsonl", (fun () -> Trace.dumpText g)
            "text", (fun () -> Trace.render g (Trace.snapshot g))
            "why", (fun () -> Trace.render g (Trace.whyAt g sample.Log 2) + "\n" + Trace.render g (Trace.whyAt g sample.Sum 2))
        ]

    match mode with
    | "all" -> all |> List.map (fun (title, f) -> $"== %s{title} ==\n%s{f ()}") |> String.concat "\n"
    | mode ->
        match all |> List.tryFind (fun (title, _) -> title = mode) with
        | Some (_, f) -> f ()
        | None -> failwith $"Unknown mode '%s{mode}'. Use jsonl, text, why or all."

#if INTERACTIVE
let mode = fsi.CommandLineArgs |> Array.tryItem 1 |> Option.defaultValue "all"
let sample = run ()
stdout.Write (sections sample mode)
(sample.Graph :> System.IDisposable).Dispose ()
#endif
