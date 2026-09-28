module Ranvier.Tests.SuspensionEdges

open System
open Expecto
open Ranvier

/// <summary>
/// The pending channel is the reason this library exists, so the edges of it
/// are where a bug costs the most: a boundary that catches something it should
/// have let through, a fallback that itself suspends, a recover that throws, a
/// source settled twice. None of these are exotic — a fallback that reads a
/// signal is ordinary UI code, and a <c>recover</c> that re-raises is how you
/// narrow an error boundary to the errors it actually handles.
/// </summary>
[<Tests>]
let tests =
    testList
        "SuspensionEdges"
        [
            test "an inner boundary settling does not leave the outer one waiting" {
                let g = new Graph ()
                let inner = AsyncSource<int> g
                let outer = AsyncSource<int> g

                let b =
                    Boundary<int>
                        .Suspense(
                            g,
                            (fun () ->
                                let i = Boundary<int>.Suspense(g, (fun () -> inner.Value), (fun _ -> -1))

                                i.Value + outer.Value),
                            fun _ -> -2
                        )

                Expect.equal b.TryValue (Ready -2) "the outer is waiting on its own source"

                inner.Settle 1
                Expect.equal b.TryValue (Ready -2) "the inner settling does not release the outer"

                outer.Settle 10
                Expect.equal b.TryValue (Ready 11) "both settled"
            }

            test "a fallback that suspends does not produce a value" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let b = AsyncSource<int> g

                let boundary = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> b.Value))

                // Nothing here can be ready. The interesting question is
                // whether it says so or throws out of the boundary that was
                // supposed to catch.
                Expect.equal boundary.TryValue Pending "a suspended fallback leaves the boundary pending"

                b.Settle 5
                Expect.equal boundary.TryValue (Ready 5) "the fallback becomes available first"

                a.Settle 1
                Expect.equal boundary.TryValue (Ready 1) "and the body takes over when it settles"
            }

            test "a recover that rethrows does not swallow the new exception" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let boundary =
                    Boundary<int>
                        .Errors(
                            g,
                            (fun () ->
                                s.Value |> ignore
                                failwith "original"),
                            fun ex -> raise (InvalidOperationException ("rethrown", ex))
                        )

                match boundary.TryValue with
                | Failed ex -> Expect.stringContains ex.Message "rethrown" "the boundary reports what recover threw"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "a fallback that throws is reported rather than lost" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                let boundary =
                    Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> failwith "fallback blew up"))

                match boundary.TryValue with
                | Failed ex -> Expect.stringContains ex.Message "fallback" "the throw surfaces"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "settling an AsyncSource twice publishes the second value" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let m = Memo (g, (fun _ -> a.Value))

                a.Settle 1
                Expect.equal m.Value 1 "first settle"

                a.Settle 2
                Expect.equal m.Value 2 "a source is a source: settling again is a write"
                Expect.equal m.Runs 2 "which propagates"
            }

            test "settling with the value already there is not cut off" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let m = Memo (g, (fun _ -> a.Value))

                a.Settle 1
                m.Value |> ignore
                a.Settle 1
                m.Value |> ignore

                // `Settle` does not consult the equality policy, and should not
                // be made to without care: a settle carries two things, a value
                // and the fact that a flight ended, and only the first of those
                // can be compared. Cutting off on an equal value would lose the
                // pending-to-ready transition in the case that matters most —
                // the first settle, where the old value is a default that may
                // well equal the real one.
                Expect.equal m.Runs 2 "a settle is an event, so it propagates even when the value repeats"
            }

            test "a failed source can be settled afterwards" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                a.Fail (InvalidOperationException "nope")

                match a.TryValue with
                | Failed _ -> ()
                | other -> failtestf "expected Failed, got %A" other

                a.Settle 7
                Expect.equal a.TryValue (Ready 7) "failure is not terminal"
                Expect.equal a.Status Status.None "and the error flag is cleared"
            }

            test "an error boundary over a suspended body stays suspended" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                let boundary = Boundary<int>.Errors(g, (fun () -> a.Value), (fun _ _ -> -1))

                Expect.equal boundary.TryValue Pending "an error boundary is not a suspense boundary"
                Expect.isFalse (boundary.Status.HasFlag Status.Error) "and it has not recorded an error"

                a.Settle 3
                Expect.equal boundary.TryValue (Ready 3) "and it resolves normally"
            }

            test "a boundary whose body suspends on two sources waits for both" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let b = AsyncSource<int> g

                let boundary =
                    Boundary<int>.Suspense(g, (fun () -> a.Value + b.Value), (fun _ -> -1))

                Expect.equal boundary.TryValue (Ready -1) "waiting"

                a.Settle 1
                Expect.equal boundary.TryValue (Ready -1) "one of two is not enough"

                b.Settle 2
                Expect.equal boundary.TryValue (Ready 3) "and now it is"
            }

            test "a pending read publishes nothing until the source settles" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let m = Memo (g, (fun _ -> a.Value + 100))

                Expect.equal m.TryValue Pending "nothing is published while the source is pending"

                a.Settle 1
                Expect.equal m.TryValue (Ready 101) "the real one is"
            }

            test "a suspended effect leaves no cleanup behind from the aborted run" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let cleanups = ref 0

                new Effect (
                    g,
                    fun () ->
                        a.Value |> ignore
                        g.OnCleanup (fun () -> cleanups.Value <- cleanups.Value + 1)
                )
                |> ignore

                Expect.equal cleanups.Value 0 "nothing has completed, so nothing has been torn down"

                a.Settle 1
                Expect.equal cleanups.Value 0 "the first completed run registers, it does not tear down"

                a.Settle 2
                Expect.equal cleanups.Value 1 "and the second run tears down exactly the one registration"
            }

            // A body that catches the channel in its own `try/with` is pending
            // whatever it returns.
            test "a memo that swallows a pending read is still pending" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                let m =
                    Memo (
                        g,
                        (fun _ ->
                            try
                                a.Value
                            with _ ->
                                -1)
                    )

                Expect.equal m.TryValue Pending "the placeholder is not published"

                a.Settle 5
                Expect.equal m.TryValue (Ready 5) "the settled value is"
            }

            test "an async memo whose task captures a pending read is pending" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let m = new AsyncMemo<int> (g, (fun _ _ -> task { return a.Value * 2 }))

                Expect.equal m.TryValue Pending "a faulted task is not an error when the fault is the channel"

                a.Settle 3
                Expect.equal m.TryValue (Ready 6) "and the settle re-runs it"
            }

            test "a boundary whose body swallows a pending read shows the fallback" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                let boundary =
                    Boundary<int>
                        .Suspense(
                            g,
                            (fun () ->
                                try
                                    a.Value
                                with _ ->
                                    -1),
                            (fun _ -> -2)
                        )

                Expect.equal boundary.TryValue (Ready -2) "the fallback, not the placeholder"

                a.Settle 5
                Expect.equal boundary.TryValue (Ready 5) "the body takes over when it settles"
            }

            test "a fallback that swallows a pending read leaves the boundary pending" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let b = AsyncSource<int> g

                let boundary =
                    Boundary<int>
                        .Suspense(
                            g,
                            (fun () -> a.Value),
                            (fun _ ->
                                try
                                    b.Value
                                with _ ->
                                    -1)
                        )

                Expect.equal boundary.TryValue Pending "the fallback's placeholder is not shown"

                b.Settle 7
                Expect.equal boundary.TryValue (Ready 7) "the fallback's settled value is"
            }

            test "a recover that swallows a pending read leaves the boundary pending" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                let boundary =
                    Boundary<int>
                        .Errors(
                            g,
                            (fun () -> failwith "boom"),
                            (fun _ _ ->
                                try
                                    a.Value
                                with _ ->
                                    -1)
                        )

                Expect.equal boundary.TryValue Pending "the recovery's placeholder is not shown"

                a.Settle 8
                Expect.equal boundary.TryValue (Ready 8) "the recovered value is"
            }

            test "an effect that swallows a pending read is pending" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                let e =
                    new Effect (
                        g,
                        (fun () ->
                            try
                                a.Value |> ignore
                            with _ ->
                                ())
                    )

                Expect.isTrue (e.Status.HasFlag Status.Pending) "the run is pending"
                Expect.isFalse (e.Status.HasFlag Status.Error) "and not an error"
            }

            test "a pending read swallowed inside untrack is a value" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                let m =
                    Memo (
                        g,
                        (fun _ ->
                            g.Untrack (fun () ->
                                try
                                    a.Value
                                with _ ->
                                    -1))
                    )

                Expect.equal m.TryValue (Ready -1) "an untracked read is the body's business"
            }

            test "Value raises a caught channel when its handler suspends or throws" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let b = AsyncSource<int> g
                let suspended = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> b.Value))

                let rethrown =
                    Boundary<int>.Errors(g, (fun () -> failwith "body"), (fun _ _ -> invalidOp "recover"))

                let failedFallback =
                    Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> failwith "fallback"))

                Expect.throwsT<NotReadyException> (fun () -> suspended.Value |> ignore) "a suspended fallback"

                Expect.throwsC (fun () -> failedFallback.Value |> ignore) (fun ex -> Expect.equal ex.Message "fallback" "the fallback's exception")

                Expect.throwsC (fun () -> rethrown.Value |> ignore) (fun ex -> Expect.equal ex.Message "recover" "the recover's exception")
            }

            test "a body that catches two pending reads lists only the last in PendingSources" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let b = AsyncSource<int> g

                let m =
                    Memo (
                        g,
                        fun _ ->
                            let x =
                                try
                                    a.Value
                                with NotReadyException _ ->
                                    0

                            let y =
                                try
                                    b.Value
                                with NotReadyException _ ->
                                    0

                            x + y
                    )

                Expect.equal m.TryValue Pending "the caught reads still suspend the memo"

                Expect.sequenceEqual (m.PendingSources |> Seq.map (fun n -> n.Id)) [ (b :> INode).Id ] "the last pending read is the one recorded"
            }

            test "an uncaught pending read inside untrack stays pending after the source settles" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let m = Memo (g, (fun _ -> g.Untrack (fun () -> a.Value) + 1))
                let mutable effectRuns = 0

                let e =
                    new Effect (
                        g,
                        fun () ->
                            effectRuns <- effectRuns + 1
                            g.Untrack (fun () -> a.Value) |> ignore
                    )

                Expect.equal m.TryValue Pending "the escaped read suspends the memo"
                Expect.isTrue (e.Status.HasFlag Status.Pending) "and the effect"
                a.Settle 1
                Expect.equal m.TryValue Pending "settling the source does not re-run the memo"
                Expect.equal effectRuns 1 "or the effect"
            }
        ]
