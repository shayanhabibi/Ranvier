module Ranvier.Tests.Equality

open System
open Expecto
open Ranvier
open Ranvier.Tests.Support

type Point = { X: int; Y: int }

type Opaque(tag: string) =
    member _.Tag = tag

[<Tests>]
let tests =
    testList
        "Equality"
        [
            test "the default policy compares primitives by value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let c = Memo (g, (fun _ -> s.Value + 1))

                Expect.equal c.TryValue (Ready 2) "precondition: c is clean"
                s.Value <- 1
                // Memos are pull-based: the read is what would re-run the body,
                // so the assertion is only meaningful after one.
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "an equal int must cut off"
            }

            test "the default policy compares strings by value" {
                let g = new Graph ()
                // Built at runtime so the two strings cannot be interned to one
                // reference — reference equality would wrongly let this through.
                let s = Signal (g, String ('a', 3))
                let c = Memo (g, (fun _ -> s.Value.Length))

                Expect.equal c.TryValue (Ready 3) "precondition: c is clean"
                s.Value <- String ('a', 3)
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "an equal string must cut off, as `===` does"
            }

            test "the default policy compares records by reference" {
                let g = new Graph ()
                let s = Signal (g, { X = 1; Y = 2 })
                let c = Memo (g, (fun _ -> s.Value.X))

                Expect.equal c.TryValue (Ready 1) "precondition: c is clean"
                s.Value <- { X = 1; Y = 2 }
                c.TryValue |> ignore
                Expect.equal c.Runs 2 "a structurally equal record is a new object, so `===` says changed"
            }

            test "a struct tuple still compares by value" {
                let g = new Graph ()
                let s = Signal (g, struct (1, 2))

                let first () =
                    let struct (x, _) = s.Value in x

                let c = Memo (g, (fun _ -> first ()))

                Expect.equal c.TryValue (Ready 1) "precondition: c is clean"
                s.Value <- struct (1, 2)
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "value types have no identity to distinguish"
            }

            test "the structural policy cuts off on equal records" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = StructuralPolicy ()
                        }
                    )

                let s = Signal (g, { X = 1; Y = 2 })
                let c = Memo (g, (fun _ -> s.Value.X))

                Expect.equal c.TryValue (Ready 1) "precondition: c is clean"
                s.Value <- { X = 1; Y = 2 }
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "structural equality is the whole point of the opt-in"
            }

            test "a reference type with no structural equality is compared by identity" {
                let g = new Graph ()
                let a = Opaque "a"
                let s = Signal (g, a)
                let c = Memo (g, (fun _ -> s.Value.Tag))

                Expect.equal c.TryValue (Ready "a") "precondition: c is clean"
                s.Value <- a
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "the same object must cut off"
                s.Value <- Opaque "a"
                c.TryValue |> ignore
                Expect.equal c.Runs 2 "a different object must not"
            }

            untracedOnly
            <| test "a cutoff test on a value type allocates nothing" {
                // The reason the comparer is typed at all. An IEqualityComparer<obj>
                // boxes both operands on every comparison; at 24 bytes a box that
                // is ~480 KB of garbage for the loop below, against 0 here.
                //
                // Writes of an equal value, so the measured window is the cutoff
                // test and nothing downstream of it.
                let g = new Graph ()
                let s = Signal (g, 7)

                // Warm the paths, so JIT and first-touch allocation land outside
                // the window.
                for _ in 1..100 do
                    s.Value <- 7

                let before = GC.GetAllocatedBytesForCurrentThread ()

                for _ in 1..10_000 do
                    s.Value <- 7

                let allocated = GC.GetAllocatedBytesForCurrentThread () - before

                Expect.isLessThan allocated 8_192L $"10,000 cutoff tests allocated %d{allocated} bytes; a boxing comparer allocates ~480,000"
            }

            untracedOnly
            <| test "notifying observers allocates nothing" {
                // Notification has to walk a copy, because marking a dependent
                // dirty can drop the edge being walked. Taking that copy with
                // Seq.toArray allocated an array per write; ObserverSet reuses one.
                let g = new Graph ()
                let s = Signal (g, 0)
                let a = Memo (g, (fun _ -> s.Value + 1))
                let b = Memo (g, (fun _ -> s.Value + 2))
                let c = Memo (g, (fun _ -> s.Value + 3))

                // Reading is what links the edges; with no observers there would
                // be nothing to notify and the test would be vacuous.
                let read () =
                    a.TryValue |> ignore
                    b.TryValue |> ignore
                    c.TryValue |> ignore

                for i in 1..100 do
                    s.Value <- i
                    read ()

                let before = GC.GetAllocatedBytesForCurrentThread ()

                for i in 1..10_000 do
                    s.Value <- i

                let allocated = GC.GetAllocatedBytesForCurrentThread () - before

                Expect.isLessThan allocated 8_192L $"10,000 notifications over 3 observers allocated %d{allocated} bytes"
            }
        ]
