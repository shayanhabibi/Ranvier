Compiler and replay regressions for automatically labelled bindings.

```fsharp map replay
let values = createSignal [ 1, 2; 2, 3 ]
let rows = createProjection fst snd (fun () -> values.Value)
let total = rows |> Projection.sumBy id // sum
let count =
    rows
    |> Projection.countBy (fun _ -> true) // count
let any = rows |> Projection.exists (fun value -> value > 2)
let all = rows |> Projection.forall (fun value -> value > 0)
let folded = rows |> Projection.fold (+) 0
let grouped = rows |> Projection.foldGroup (+) (-) 0
let selected = // choose a key
    createSelector (fun () -> 1) // lookup
let editable = createEditable (fun _ -> "https://example.test") // editable
let draft = // preserve comment-like string contents
    createDraft (fun _ -> @"https://example.test") // draft
let multiline = createDraft (fun _ -> """first
// second
""
last""") // multiline
let quoted = createDraft (fun _ -> '"') // character
createEffect (fun () ->
    printfn "%d %d %b %b %d %d %b %s %s" total.Value count.Value any.Value all.Value folded.Value grouped.Value (selected.Get 1) editable.Value draft.Value)
controls [
    button "Change rows" (fun () -> values.Value <- [ 1, 4 ])
    |> expect "aggregates still update after labelling" (fun () ->
        total.Peek = 4 && count.Peek = 1 && any.Peek && all.Peek && folded.Peek = 4 && grouped.Peek = 4)
    |> expect "creation wrappers preserve string seeds" (fun () ->
        editable.Value = "https://example.test" && draft.Value = "https://example.test" &&
        multiline.Value = "first\n// second\n\"\"\nlast" && quoted.Value = '"')
    |> expect "every supported binding has a trace label" (fun () ->
        [ "total"; "count"; "any"; "all"; "folded"; "grouped"; "selected"; "editable"; "draft"; "multiline"; "quoted" ]
        |> List.forall (fun name ->
            Trace.events graph'
            |> Array.exists (fun event -> event.Kind = TraceEventKind.Label && event.Node <> 0 && string event.Payload = name)))
]
```
