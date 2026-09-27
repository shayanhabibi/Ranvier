module Ranvier.Tests.Cutoff

open Expecto
open Ranvier
open Ranvier.Tests.Readings

/// <summary>
/// The equality cutoff decides whether a write propagates at all, which makes
/// it the one policy that can both waste an entire graph's work and lose an
/// update. The interesting inputs are the values where equality is not what a
/// naive reading expects: <c>nan</c>, which is equal to nothing including itself;
/// <c>-0.0</c>, which is equal to <c>0.0</c> while being a different bit pattern; <c>null</c>;
/// and mutation in place, where the value is identical and the contents are
/// not.
/// </summary>
/// <remarks>
/// Solid's <c>===</c> answers all of these a particular way, and the point of
/// <c>JsIdentityPolicy</c> is to answer them the same way. Where we diverge from
/// JavaScript, that is worth knowing on purpose rather than discovering.
/// </remarks>
[<Tests>]
let tests =
    testList
        "Cutoff"
        [
            test "writing nan over nan propagates, because nan equals nothing" {
                let g = new Graph ()
                let s = Signal (g, nan)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                Expect.equal m.Runs 1 "precondition"

                s.Value <- nan
                m.Value |> ignore

                // `===` says false for nan, so this is a real write. Solid does
                // the same. A structural policy would cut it off instead.
                Expect.equal m.Runs 2 "nan is never equal to nan, so the write is not cut off"
            }

            test "writing 0.0 over -0.0 is cut off, because === says they are equal" {
                let g = new Graph ()
                let s = Signal (g, -0.0)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                Expect.equal m.Runs 1 "precondition"

                s.Value <- 0.0
                m.Value |> ignore
                Expect.equal m.Runs 1 "-0.0 = 0.0 under === so nothing propagates"

                // A cutoff is not "store it quietly", it is "this was not a
                // write" — so the *old* value is what remains, sign and all.
                // Any reader relying on the sign of zero therefore sees a value
                // that was never written, which is Solid's behaviour too.
                Expect.equal (1.0 / s.Value) -infinity "the old value is what stayed, sign included"
            }

            test "writing null over null is cut off" {
                let g = new Graph ()
                let s = Signal<string>(g, null)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                s.Value <- null
                m.Value |> ignore
                Expect.equal m.Runs 1 "null is null"
            }

            test "writing a value over null propagates" {
                let g = new Graph ()
                let s = Signal<string>(g, null)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                s.Value <- "x"
                Expect.equal m.Value "x" "and the new value is visible"
                Expect.equal m.Runs 2 "which took a recomputation"
            }

            test "mutating a reference in place changes nothing, because the reference did not" {
                let g = new Graph ()
                let list = ResizeArray [ 1 ]
                let s = Signal (g, list)
                let m = Memo (g, (fun () -> s.Value.Count))

                Expect.equal m.Value 1 "precondition"

                list.Add 2
                s.Value <- list

                // The classic mutable-state trap, and it behaves exactly as it
                // does in Solid: same reference, no write.
                Expect.equal m.Value 1 "the memo still reports the stale count"
                Expect.equal m.Runs 1 "because writing the same reference is not a write"
            }

            test "equal strings are cut off by value, not by reference" {
                let g = new Graph ()
                let s = Signal (g, "ab")
                let m = Memo (g, (fun () -> s.Value.Length))

                m.Value |> ignore
                // Built at runtime, so this is a different object with the same
                // characters. `===` on a JS string compares characters.
                s.Value <- "a" + (string 'b')
                m.Value |> ignore
                Expect.equal m.Runs 1 "a string is compared by its characters"
            }

            test "an option is compared by reference, so Some 1 over Some 1 propagates" {
                let g = new Graph ()
                let s = Signal (g, Some 1)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                s.Value <- Some 1
                m.Value |> ignore

                // `Some 1` allocates, so the two are different objects. This is
                // the shape most likely to surprise an F# caller, who reads
                // `Some 1 = Some 1` as true.
                Expect.equal m.Runs 2 "a fresh Some is a different reference"
            }

            test "None over None is cut off, because None is null" {
                let g = new Graph ()
                let s = Signal<int option>(g, None)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                s.Value <- None
                m.Value |> ignore
                Expect.equal m.Runs 1 "None has no identity to differ in"
            }

            test "the structural policy cuts off an equal Some" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = StructuralPolicy ()
                        }
                    )

                let s = Signal (g, Some 1)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                s.Value <- Some 1
                m.Value |> ignore
                Expect.equal m.Runs 1 "structural equality sees through the allocation"
            }

            test "the structural policy cuts off a nan write, unlike the identity one" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = StructuralPolicy ()
                        }
                    )

                let s = Signal (g, nan)
                let m = Memo (g, (fun () -> s.Value))

                m.Value |> ignore
                s.Value <- nan
                m.Value |> ignore

                // `EqualityComparer<float>.Default` is `Double.Equals`, which
                // says nan equals nan — equivalence-relation semantics, not
                // IEEE. That is the right answer for a policy that exists to be
                // .NET-idiomatic, and the opposite of what `JsIdentityPolicy`
                // gives (see the nan test above). The two disagree on purpose;
                // which one is in force is not visible at the write site, which
                // is the thing to know.
                Expect.equal m.Runs 1 "Double.Equals says nan equals nan, so this is not a write"
            }

            test "an equal write still does not disturb a suspended reader" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let s = Signal (g, 1)

                let b = Boundary<int>.Suspense(g, (fun () -> a.Value + s.Value), (fun _ -> -1))

                Expect.equal b.TryValue (Ready -1) "suspended on the async source"

                s.Value <- 1
                Expect.equal b.Runs 1 "an equal write is not a write, even to something waiting"

                a.Settle 10
                Expect.equal b.TryValue (Ready 11) "and the settle still lands"
            }

            test "a memo that fails with a new exception wakes its dependents" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Memo<int>(g, (fun () -> failwithf "e%d" s.Value))
                let b = Memo (g, (fun () -> a.Value + 1))

                Expect.equal (reason b.TryValue) "e1" "first failure"
                s.Value <- 2
                Expect.equal (reason a.TryValue) "e2" "the source memo moved"
                Expect.equal (reason b.TryValue) "e2" "and the dependent sees the new exception"
            }

            test "an effect re-runs when a memo it reads fails with a new exception" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Memo<int>(g, (fun () -> failwithf "e%d" s.Value))
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add (reason a.TryValue)))
                |> ignore

                s.Value <- 2
                Expect.sequenceEqual seen [ "e1"; "e2" ] "the effect saw both exceptions"
            }

            test "an error boundary recovers from the new exception of a failed memo" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let a = Memo<int>(g, (fun () -> failwithf "e%d" s.Value))

                let b =
                    Boundary<string>.Errors(g, (fun () -> string a.Value), (fun ex _ -> ex.Message))

                Expect.equal b.TryValue (Ready "e1") "first recovery"
                s.Value <- 2
                Expect.equal b.TryValue (Ready "e2") "the boundary recovered from the new exception"
            }

            test "a memo that fails again with the same exception wakes no dependent" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let cached = exn "same"
                let a = Memo<int>(g, (fun () -> ignore s.Value; raise cached))
                let mutable memoRuns = 0
                let mutable effectRuns = 0

                let b =
                    Memo (
                        g,
                        (fun () ->
                            memoRuns <- memoRuns + 1
                            a.Value + 1)
                    )

                new Effect (g, (fun () -> effectRuns <- effectRuns + 1; ignore a.TryValue))
                |> ignore

                Expect.equal (reason b.TryValue) "same" "first failure"
                s.Value <- 2
                s.Value <- 3
                Expect.equal (reason b.TryValue) "same" "still failed"
                Expect.equal memoRuns 1 "the dependent memo is cut off"
                Expect.equal effectRuns 1 "the effect is cut off"
            }

            test "a second AsyncSource failure crosses two memos" {
                let g = new Graph ()
                let src = AsyncSource<int> g
                let mid = Memo (g, (fun () -> src.Value + 1))
                let top = Memo (g, (fun () -> mid.Value + 1))

                src.Fail (exn "boom1")
                Expect.equal (reason top.TryValue) "boom1" "first failure"
                src.Fail (exn "boom2")
                Expect.equal (reason mid.TryValue) "boom2" "the middle memo moved"
                Expect.equal (reason top.TryValue) "boom2" "and the top memo sees the second failure"
            }
        ]
