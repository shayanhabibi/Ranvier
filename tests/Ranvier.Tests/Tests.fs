module Ranvier.Tests.Diamond

open Expecto
open Ranvier

/// <summary>
/// The diamond: <c>s</c> feeds both an async source and a memo that reads both.
/// </summary>
/// <remarks>
/// <para>
///     s ───────────────┐
///     └──> a (async) ──┴──> c = s + a
/// </para>
/// <para>
/// <c>a</c> is requested while s = 1 but resolves late, carrying the value that
/// belongs to the s = 2 written mid-flight. The only states the graph is ever
/// in are (s=1, a=10) => c=11 and (s=2, a=20) => c=22. A c of 21 pairs a
/// prefix read at T0 with a suffix read at T1 and corresponds to no state the
/// graph was ever in — which is why a suspended body is re-run from the top
/// rather than resumed. See docs/.ai/RESEARCH-suspension-mechanism.md §A.
/// </para>
/// </remarks>
[<Tests>]
let tests =
    testList
        "Diamond"
        [
            test "a pending source suspends its dependents" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = AsyncSource<int>(g)

                let c = Memo (g, (fun () -> s.Value + a.Value))

                Expect.equal c.TryValue Pending "c must suspend while a is in flight"
                Expect.equal c.Status Status.Pending "the pending channel must reach c"

                Expect.sequenceEqual
                    (c.PendingSources |> Seq.map (fun n -> n.Id))
                    [ (a :> INode).Id ]
                    "c must record exactly the source it is waiting on"
            }

            test "a suspended read still links the edge" {
                // Upstream bug #2893: a read that suspends without first linking
                // leaves the consumer stranded — it never learns of the settle.
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let c = Memo (g, (fun () -> a.Value))

                Expect.equal c.TryValue Pending "precondition: c is suspended"
                a.Settle 10
                Expect.equal c.TryValue (Ready 10) "the settle must wake c"
            }

            test "a late settle yields a state the graph was actually in" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = AsyncSource<int>(g)

                let c = Memo (g, (fun () -> s.Value + a.Value))

                Expect.equal c.TryValue Pending "precondition: c is suspended on a"

                // s moves while a is still in flight; a then settles carrying the
                // value belonging to the *new* s.
                s.Value <- 2
                a.Settle 20

                Expect.equal c.TryValue (Ready 22) "c must not pair s=1 with a=20"
            }

            test "pending propagates through an intermediate memo" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                let mid = Memo (g, (fun () -> a.Value * 2))

                let outer = Memo (g, (fun () -> mid.Value + 1))

                Expect.equal outer.TryValue Pending "pending must cross a memo boundary"

                a.Settle 5
                Expect.equal outer.TryValue (Ready 11) "outer must settle once the chain does"
            }

            test "a failure settles as Failed, not as pending" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let c = Memo (g, (fun () -> a.Value))

                Expect.equal c.TryValue Pending "precondition: c is suspended"
                a.Fail (exn "boom")

                match c.TryValue with
                | Failed e -> Expect.equal e.Message "boom" "the original error must survive"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "a clean memo is not re-run" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let c = Memo (g, (fun () -> s.Value + 1))

                Expect.equal c.TryValue (Ready 2) "first read computes"
                Expect.equal c.TryValue (Ready 2) "second read is cached"
                Expect.equal c.Runs 1 "a cached read must not execute the body"

                s.Value <- 5
                Expect.equal c.TryValue (Ready 6) "a write invalidates"
                Expect.equal c.Runs 2 "exactly one re-run"
            }

            test "an equal write does not invalidate" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let c = Memo (g, (fun () -> s.Value + 1))

                Expect.equal c.TryValue (Ready 2) "precondition: c is clean"
                s.Value <- 1
                Expect.equal c.TryValue (Ready 2) "value unchanged"
                Expect.equal c.Runs 1 "cutoff must stop the write at the signal"
            }
        ]
