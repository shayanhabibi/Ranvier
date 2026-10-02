(**
---
title: Literate map regression
---
*)
(*** hide ***)
#load "../../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

let graph = new Graph ()
let active = graph.Activate ()

(**
An ordinary F# block gets map rendering and editor type checking.
*)
(*** map replay speed=0.75 show=output ***)
let count = createSignal 1
let doubled = createMemo (fun _ -> count.Value * 2)
createEffect (fun () -> doubled.Value |> ignore)

controls
    [
        sliderSignal "Count" (1, 10) count [ 5 ]
        |> describe "The memo follows the input."
        |> expect "doubled is ten" (fun () -> doubled.Peek = 10)
    ]

(*** hide ***)
let renderMap () =
    SignalMap (Live (fun _ -> controls [])) FlightPolicy.CancelPrevious [||] false Grouping.Expand

let renderSlowMap () =
    SignalMapWithSpeed 0.5 (Replayed (fun _ -> controls [])) FlightPolicy.CancelPrevious [||] true Grouping.Expand

count.Value <- 5

if doubled.Peek <> 10 then
    failwith "The script's memo did not update."

if Trace.events graph |> Array.isEmpty then
    failwith "The helper loaded an untraced engine."

active.Dispose ()
graph.Dispose ()
