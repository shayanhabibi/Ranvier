module Ranvier.AotSmoke.Program

open System
open System.Collections.Generic
open System.Threading
open Ranvier
open Ranvier.CSharp
open Ranvier.Elmish
open Ranvier.Query

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

    use quiet =
        Reactive.Debounce (TimeSpan.Zero, Func<int>(fun () -> counter.Value), null, null)

    use first =
        Reactive.ThrottleFirst (TimeSpan.Zero, Func<int>(fun () -> counter.Value), null, null)

    use last =
        Reactive.ThrottleLast (TimeSpan.Zero, Func<int>(fun () -> counter.Value), null, null)

    use both =
        Reactive.Throttle (TimeSpan.Zero, Func<int>(fun () -> counter.Value), null, null)

    counter.Value <- 2
    equals "debounce publishes a zero-delay input" "2" (string quiet.Value)
    equals "leading throttle publishes a zero-delay input" "2" (string first.Value)
    equals "trailing throttle publishes a zero-delay input" "2" (string last.Value)
    equals "combined throttle publishes a zero-delay input" "2" (string both.Value)
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

    use client = new QueryClient (g)

    let numbers =
        client.Define (EqualityComparer<int>.Default, fun key _ -> System.Threading.Tasks.Task.FromResult key)

    use query = numbers.Acquire 7
    use shared = numbers.Acquire 7
    equals "query value" "7" (string query.Value)

    let saved =
        client.Mutate (10, (fun value _ -> System.Threading.Tasks.Task.FromResult value), fun value -> [ numbers.UpdateIfLoaded (7, fun _ -> value) ])

    report "mutation applied" (saved.Result = MutationOutcome.Applied 10) "unexpected mutation outcome"
    equals "shared query reconciliation" "10" (string shared.Value)

    let failed =
        client.Mutate ((), (fun () _ -> raise (InvalidOperationException "offline"): System.Threading.Tasks.Task<int>), fun _ -> [])

    report
        "mutation failure"
        (match failed.Result with
         | MutationOutcome.RequestFailed _ -> true
         | _ -> false)
        "unexpected failure outcome"

    query.Dispose ()
    equals "remaining query lease" "10" (string shared.Value)

    if failures = 0 then
        Console.WriteLine "all checks passed"
        0
    else
        Console.WriteLine (string failures + " checks failed")
        1
