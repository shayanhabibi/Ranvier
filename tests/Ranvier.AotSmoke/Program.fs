module Ranvier.AotSmoke.Program

open System
open System.Collections.Generic
open System.Threading
open Ranvier
open Ranvier.CSharp
open Ranvier.Elmish

let mutable private failures = 0

let private report (name: string) (ok: bool) (detail: string) =
    if ok then
        Console.WriteLine ("ok   " + name)
    else
        failures <- failures + 1
        Console.WriteLine ("FAIL " + name + ": " + detail)

/// <summary>Runs <c>f</c> and reports whether it raised exactly <c>'E</c> with a message starting with <c>prefix</c>.</summary>
let private raises<'E when 'E :> exn> (name: string) (prefix: string) (f: unit -> unit) =
    try
        f ()
        report name false "no exception"
    with ex ->
        let ok =
            ex.GetType () = typeof<'E>
            && ex.Message.StartsWith (prefix, StringComparison.Ordinal)

        report name ok (ex.GetType().FullName + ": " + ex.Message)

let private equals (name: string) (expected: string) (actual: string) =
    report name (actual = expected) ("got \"" + actual + "\"")

[<EntryPoint>]
let main _ =
    use g = new Graph ()
    use _ = g.Activate ()

    let ints = Signal (g, [ 1; 2 ])
    let byInt = Api.createProjection id (fun x -> x * 10) (fun () -> ints.Value)
    equals "an int projection reads a row" "20" (string (byInt.Get 2))

    raises<KeyNotFoundException> "a missing int key" "The projection has no key 99." (fun () -> byInt.Get 99 |> ignore)

    let names = Signal (g, [ "a"; "b" ])

    let byName =
        Reactive.Projection (Func<_>(fun () -> names.Value :> seq<string>), Func<_, _>(fun (s: string) -> s), Func<_, _>(fun (s: string) -> s.Length))

    raises<KeyNotFoundException> "a missing string key" "The projection has no key nope." (fun () -> byName.Get "nope" |> ignore)

    let twice = Signal (g, [ 1; 1 ])
    let dup = Api.createProjection id id (fun () -> twice.Value)

    raises<InvalidOperationException> "a duplicate int key" "The projection produced the key 1 twice" (fun () -> dup.Keys |> ignore)

    let counter = Signal (g, 0)
    let mutable offThread: exn = null

    let writer =
        Thread (fun () ->
            try
                counter.Value <- 1
            with ex ->
                offThread <- ex)

    writer.Start ()
    writer.Join ()

    raises<InvalidOperationException> "an off-thread write" "A signal write ran on thread " (fun () ->
        if not (isNull offThread) then
            raise offThread)

    equals "Ready 3" "Ready 3" ((Ready 3).ToString())
    equals "Ready \"x\"" "Ready \"x\"" ((Ready "x").ToString())
    equals "Pending" "Pending" (Reading<int>.Pending.ToString())
    equals "Guarded" "Guarded" (Guarded.ToString ())
    equals "KeepLatest" "KeepLatest" (KeepLatest.ToString ())

    equals
        "GraphOptions.Default"
        "{ Equality = Ranvier.JsIdentityPolicy\n  FlightPolicy = CancelPrevious\n  ThreadAffinity = Guarded\n  Dispatcher = None }"
        (GraphOptions.Default.ToString ())

    raises<NotReadyException> "a pending read" "NotReadyException " (fun () ->
        let source = AsyncSource<int>(g)
        source.Value |> ignore)

    let app = Mvu.create 0 (fun (step: int) model -> model + step)
    let doubled = app.Select (fun model -> model * 2)
    app.Dispatch 3
    equals "an Mvu selector reads the dispatched model" "6" (string doubled.Value)

    if failures = 0 then
        Console.WriteLine "all checks passed"
        0
    else
        Console.WriteLine (string failures + " checks failed")
        1
