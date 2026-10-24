open System.Diagnostics
open Ranvier

let time (n: int) (op: int -> unit) =
    for i in 1 .. n / 10 do
        op i

    let sw = Stopwatch.StartNew ()

    for i in 1..n do
        op i

    sw.Elapsed.TotalNanoseconds / float n

let signalCase n =
    let g = new Graph ()
    let s = Signal (g, 0)
    let m = g.Run (fun () -> createMemo (fun _ -> s.Value + 1))
    let mutable sink = 0
    g.Run (fun () -> createEffect (fun () -> sink <- m.Value))
    time n (fun i -> s.Value <- i)

let memoCase n =
    let g = new Graph ()
    let s = Signal (g, 0)
    let a = g.Run (fun () -> createMemo (fun _ -> s.Value + 1))
    let b = g.Run (fun () -> createMemo (fun _ -> a.Value + 1))
    let c = g.Run (fun () -> createMemo (fun _ -> b.Value + 1))
    let d = g.Run (fun () -> createMemo (fun _ -> c.Value + 1))
    let mutable sink = 0

    time n (fun i ->
        s.Value <- i
        sink <- d.Value)

[<EntryPoint>]
let main _ =
    let n = 200_000

    let best f =
        Array.init 5 (fun _ -> f n) |> Array.min

    printfn "%f %f" (best signalCase) (best memoCase)
    0
