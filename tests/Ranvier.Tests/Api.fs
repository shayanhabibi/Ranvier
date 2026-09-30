module Ranvier.Tests.Api

open System.Threading
open Expecto
open Ranvier

#if !FABLE_COMPILER
// .NET only: JavaScript has one thread.
/// <summary>
/// Runs <c>body</c> on a different thread and waits for it. Same reasoning as
/// <c>Threading.offThread</c>: a task blocked on with <c>.Result</c> can be inlined onto
/// the waiting thread, which would make an assertion about thread-static state
/// vacuous.
/// </summary>
let private offThread (body: unit -> 'T) : 'T =
    let mutable result = Unchecked.defaultof<'T>
    let mutable failure: exn = null

    let thread =
        Thread (
            (fun () ->
                try
                    result <- body ()
                with ex ->
                    failure <- ex),
            IsBackground = true
        )

    thread.Start ()
    thread.Join ()

    if not (isNull failure) then
        raise failure

    result
#endif

[<Tests>]
let tests =
    testList
        "Api"
        [
            test "the functions resolve the active graph" {
                use g = new Graph ()

                let runs =
                    g.Run (fun () ->
                        let s = createSignal 1
                        let doubled = createMemo (fun _ -> s.Value * 2)
                        let seen = ResizeArray ()

                        createEffect (fun () -> seen.Add doubled.Value)

                        s.Value <- 5

                        Expect.sequenceEqual seen [ 2; 10 ] "the effect saw both values through the memo"
                        seen.Count)

                Expect.equal runs 2 "and the body's own result came back out"
            }

            test "a function outside an active graph says so" {
                // The alternative is creating a graph on demand, which would be a
                // graph nobody disposes: every effect in it outlives the scope
                // the caller thinks they wrote, and nothing ever says so.
                Expect.throwsT<System.InvalidOperationException>
                    (fun () -> createSignal 1 |> ignore)
                    "no ambient graph is an error, not an implicit one"
            }

            test "the previous graph comes back after a nested activation" {
                use outer = new Graph ()
                use inner = new Graph ()

                outer.Run (fun () ->
                    inner.Run (fun () -> createSignal 2 |> ignore)

                    // Asserted on identity, not on a value. Everything a leaked
                    // activation builds still *works* — it is simply owned by a
                    // graph the caller never chose, and disposed when that one is.
                    match Graph.TryCurrent with
                    | ValueSome g -> Expect.isTrue (obj.ReferenceEquals (g, outer)) "the outer graph is active again"
                    | ValueNone -> failtest "nothing is active after the nested graph returned")

                Expect.isTrue Graph.TryCurrent.IsNone "and nothing is active once the outer one returns"
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "the active graph is per thread" {
                use g = new Graph ()

                g.Run (fun () ->
                    Expect.isTrue (Graph.TryCurrent.IsSome) "active on the thread that activated it"

                    let seenElsewhere = offThread (fun () -> Graph.TryCurrent.IsSome)

                    // A graph is thread-affine, so an ambient slot shared across
                    // threads would hand this thread a graph it may not touch.
                    Expect.isFalse seenElsewhere "and on no other")
            }

            // .NET only: an out parameter.
            test "TryGetCurrent returns the graph TryCurrent resolves" {
                use g = new Graph ()
                let mutable found = Unchecked.defaultof<Graph>

                g.Run (fun () ->
                    let mutable inside = Unchecked.defaultof<Graph>
                    Expect.isTrue (Graph.TryGetCurrent &inside) "a graph is active"
                    Expect.isTrue (obj.ReferenceEquals (inside, g)) "the active graph")

                Expect.isFalse (Graph.TryGetCurrent &found) "nothing is active outside the run"
                Expect.isNull (box found) "and the out value is null"
            }
#endif

            test "untrack reads without creating an edge" {
                use g = new Graph ()

                g.Run (fun () ->
                    let tracked = createSignal 1
                    let hidden = createSignal 10
                    let m = createMemo (fun _ -> tracked.Value + untrack (fun () -> hidden.Value))

                    Expect.equal m.Value 11 "the untracked read still returns the value"

                    hidden.Value <- 20
                    Expect.equal m.Value 11 "but writing it does not recompute"

                    tracked.Value <- 2
                    Expect.equal m.Value 22 "and the next real recomputation sees the new value")
            }

            test "batch collapses writes into one effect run" {
                use g = new Graph ()

                g.Run (fun () ->
                    let s = createSignal 1
                    let t = createSignal 10
                    let seen = ResizeArray ()

                    createEffect (fun () -> seen.Add (s.Value + t.Value))

                    batch (fun () ->
                        s.Value <- 2
                        t.Value <- 20)

                    Expect.sequenceEqual seen [ 11; 22 ] "the intermediate 12 was never observed")
            }

            test "an effect's cleanup runs before its next run" {
                use g = new Graph ()

                g.Run (fun () ->
                    let s = createSignal 1
                    let log = ResizeArray ()

                    createEffect (fun () ->
                        let v = s.Value
                        log.Add $"run {v}"
                        onCleanup (fun () -> log.Add $"clean {v}"))

                    s.Value <- 2

                    Expect.sequenceEqual log [ "run 1"; "clean 1"; "run 2" ] "the old run is torn down before the new one")
            }

            test "disposing a root stops the effects made inside it" {
                use g = new Graph ()

                g.Run (fun () ->
                    let s = createSignal 1
                    let seen = ResizeArray ()

                    let owner =
                        createRoot (fun owner ->
                            createEffect (fun () -> seen.Add s.Value)
                            owner)

                    s.Value <- 2
                    owner.Dispose ()
                    s.Value <- 3

                    Expect.sequenceEqual seen [ 1; 2 ] "the write after disposal woke nothing")
            }
        ]
