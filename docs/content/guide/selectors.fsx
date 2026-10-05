(**
---
title: Selectors
---
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

let graph = new Graph ()
let active = graph.Activate ()
(**


The examples run inside an [active graph](graph.fsx).

`createSelector source` is `createLookup` with `affected = fun prev next -> [ prev; next ]`: `Get key` is
`true` for the selected key and `false` for every other. A selection change wakes only the readers of
the previous and the next key.


*)
let selected = createSignal 1
let isSelected = createSelector (fun () -> selected.Value)
(**


*)
selected.Value <- 3
[ for k in 1..4 -> k, isSelected.Get k ]
(**

```text
[(1, false); (2, false); (3, true); (4, false)]
```

:::details Tests covering this behaviour

Pinned by `createSelector reports membership and wakes only the two ends` and
`only the affected keys are recomputed`
([Lookups.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Lookups.fs)).
:::

Move the selection from 1 to 2. Only those two membership values change; the reader for
key 3 stays quiet.

```fsharp map replay code=collapsed
let selected = createSignal 1
let isSelected = createSelector (fun () -> selected.Value)
let first = createMemo (fun _ -> isSelected.Get 1)
let second = createMemo (fun _ -> isSelected.Get 2)
let third = createMemo (fun _ -> isSelected.Get 3)
createEffect (fun () -> printfn "first = %b" first.Value)
createEffect (fun () -> printfn "second = %b" second.Value)
createEffect (fun () -> printfn "third = %b" third.Value)

controls [
    button "Select 2" (fun () -> selected.Value <- 2)
    |> describe "Keys 1 and 2 change their answers; key 3 stays false."
    |> expect "Keys 1 and 2 change their answers; key 3 stays false." (fun () -> not first.Peek && second.Peek && not third.Peek)
    button "Select 3" (fun () -> selected.Value <- 3)
    |> describe "Keys 2 and 3 change their answers; key 1 stays false."
    |> expect "Keys 2 and 3 change their answers; key 1 stays false." (fun () -> not first.Peek && not second.Peek && third.Peek)
]
```

*)
(*** hide ***)
active.Dispose ()
graph.Dispose ()
