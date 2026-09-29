module Ranvier.Tests.Equality

open System
open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Tests.Support

type Point = { X: int; Y: int }

type Opaque(tag: string) =
    member _.Tag = tag

[<Struct>]
type StructPoint = { SX: int; SY: int }

/// <summary>
/// Compares every value by its <c>string</c> rendering, ignoring case.
/// </summary>
type CaseInsensitivePolicy() =
    interface IEqualityPolicy with
        member _.Comparer<'T>() =
            { new IEqualityComparer<'T> with
                member _.Equals(a, b) =
                    String.Equals (string (box a), string (box b), StringComparison.OrdinalIgnoreCase)

                member _.GetHashCode a =
                    (string (box a)).ToLowerInvariant().GetHashCode ()
            }

let private structural =
    { GraphOptions.Default with
        Equality = StructuralPolicy ()
    }

/// <summary>
/// The number of times an effect reading a signal holding <c>first</c> re-runs when <c>second</c> is written.
/// </summary>
let private wakes (options: GraphOptions) (first: 'T) (second: 'T) =
    let g = new Graph (options)
    let s = Signal (g, first)
    let runs = ref 0

    let _effect =
        new Effect (
            g,
            fun () ->
                s.Value |> ignore
                runs.Value <- runs.Value + 1
        )

    s.Value <- second
    runs.Value - 1

/// <summary>
/// <c>dotnet</c> on .NET, <c>fable</c> under Fable.
/// </summary>
let private onTarget (dotnet: int) (fable: int) =
#if FABLE_COMPILER
    fable
#else
    dotnet
#endif

let private date () =
    DateTime (2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)

let private dateOffset () =
    DateTimeOffset (2024, 1, 2, 3, 4, 5, TimeSpan.FromHours 2.0)

let private money () = Decimal.Parse "1.5"

#if !FABLE_COMPILER
/// <summary>
/// Structural equality whose <c>int</c> comparer throws while <c>Armed</c> is set. Comparers of every other type never throw.
/// </summary>
type ThrowingIntPolicy() =
    member val Armed = false with get, set

    interface IEqualityPolicy with
        member this.Comparer<'T>() =
            let inner = EqualityComparer<'T>.Default
            let throws = typeof<'T> = typeof<int>

            { new IEqualityComparer<'T> with
                member _.Equals(a, b) =
                    if throws && this.Armed then
                        raise (InvalidOperationException "comparer")

                    inner.Equals (a, b)

                member _.GetHashCode a =
                    inner.GetHashCode a
            }

let private throwingGraph () =
    let policy = ThrowingIntPolicy ()

    let g =
        new Graph (
            { GraphOptions.Default with
                Equality = policy
            }
        )

    g, policy

let private message (reading: Reading<'T>) =
    match reading with
    | Ready v -> $"Ready %A{v}"
    | Pending -> "Pending"
    | Failed ex -> $"Failed %s{ex.Message}"

let private attempt (read: unit -> 'T) =
    try
        $"Ready %A{read ()}"
    with ex ->
        $"Failed %s{ex.Message}"
#endif

[<Tests>]
let tests =
    testList
        "Equality"
        [
            test "the default policy compares primitives by value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let c = Make.Memo (g, (fun _ -> s.Value + 1))

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
                let c = Make.Memo (g, (fun _ -> s.Value.Length))

                Expect.equal c.TryValue (Ready 3) "precondition: c is clean"
                s.Value <- String ('a', 3)
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "an equal string must cut off, as `===` does"
            }

            test "the default policy compares records by reference" {
                let g = new Graph ()
                let s = Signal (g, { X = 1; Y = 2 })
                let c = Make.Memo (g, (fun _ -> s.Value.X))

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

                let c = Make.Memo (g, (fun _ -> first ()))

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
                let c = Make.Memo (g, (fun _ -> s.Value.X))

                Expect.equal c.TryValue (Ready 1) "precondition: c is clean"
                s.Value <- { X = 1; Y = 2 }
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "structural equality is the whole point of the opt-in"
            }

            testList
                "an equal write, by type"
                [
                    // Each case writes a value equal to the one held and counts the reader's re-runs:
                    // 0 is a cutoff, 1 a propagation.
                    let case name (defaultWakes: int) (structuralWakes: int) (measure: GraphOptions -> int) =
                        test name {
                            Expect.equal (measure GraphOptions.Default) defaultWakes "under JsIdentityPolicy"
                            Expect.equal (measure structural) structuralWakes "under StructuralPolicy"
                        }

                    case "int" 0 0 (fun o -> wakes o 7 (3 + 4))
                    case "float" 0 0 (fun o -> wakes o 1.5 (3.0 / 2.0))
                    case "float nan" 1 (onTarget 0 1) (fun o -> wakes o nan (0.0 / 0.0))
                    case "string" 0 0 (fun o -> wakes o (String ('a', 3)) (String ('a', 3)))
                    case "DateTime" (onTarget 0 1) 0 (fun o -> wakes o (date ()) (date ()))
                    case "DateTimeOffset" (onTarget 0 1) 0 (fun o -> wakes o (dateOffset ()) (dateOffset ()))
                    case "decimal" (onTarget 0 1) 0 (fun o -> wakes o (money ()) (money ()))
                    case "record" 1 0 (fun o -> wakes o { X = 1; Y = 2 } { X = 1; Y = 2 })
                    case "tuple" 1 0 (fun o -> wakes o (1, "a") (1, "a"))
                    case "struct tuple" (onTarget 0 1) 0 (fun o -> wakes o (struct (1, "a")) (struct (1, "a")))
                    case "Some of int" (onTarget 1 0) 0 (fun o -> wakes o (Some 1) (Some 1))
                    case "Some of record" 1 0 (fun o -> wakes o (Some { X = 1; Y = 2 }) (Some { X = 1; Y = 2 }))
                    case "None" 0 0 (fun o -> wakes o (None: int option) None)
                    case "struct record" (onTarget 0 1) 0 (fun o -> wakes o { SX = 1; SY = 2 } { SX = 1; SY = 2 })
                    case "list" 1 0 (fun o -> wakes o [ 1; 2 ] (List.map id [ 1; 2 ]))
                    case "the same class instance" 0 0 (fun o -> let a = Opaque "a" in wakes o a a)
                    case "an equal class instance" 1 1 (fun o -> wakes o (Opaque "a") (Opaque "a"))
                ]

            test "a custom equality policy decides the cutoff for signals and memos" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = CaseInsensitivePolicy ()
                        }
                    )

                let s = Signal (g, "abc")
                let exclaimed = Make.Memo (g, (fun _ -> s.Value + "!"))
                let seen = ResizeArray<string>()
                let _effect = new Effect (g, (fun () -> seen.Add exclaimed.Value))

                s.Value <- "ABC"
                Expect.sequenceEqual seen [ "abc!" ] "a write equal ignoring case is cut off"
                Expect.equal s.Peek "abc" "a cut-off write leaves the value unchanged"

                s.Value <- "abd"
                Expect.sequenceEqual seen [ "abc!"; "abd!" ] "a write the policy calls different propagates"
            }

            test "a custom equality policy cuts off a memo that recomputes to an equal value" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = CaseInsensitivePolicy ()
                        }
                    )

                let s = Signal (g, 1)
                let label = Make.Memo (g, (fun _ -> if s.Value = 1 then "one" else "ONE"))
                let seen = ResizeArray<string>()
                let _effect = new Effect (g, (fun () -> seen.Add label.Value))

                s.Value <- 2
                Expect.equal label.Runs 2 "the memo recomputed"
                Expect.sequenceEqual seen [ "one" ] "its reader stays asleep: \"ONE\" equals \"one\" under the policy"
            }

            test "a reference type with no structural equality is compared by identity" {
                let g = new Graph ()
                let a = Opaque "a"
                let s = Signal (g, a)
                let c = Make.Memo (g, (fun _ -> s.Value.Tag))

                Expect.equal c.TryValue (Ready "a") "precondition: c is clean"
                s.Value <- a
                c.TryValue |> ignore
                Expect.equal c.Runs 1 "the same object must cut off"
                s.Value <- Opaque "a"
                c.TryValue |> ignore
                Expect.equal c.Runs 2 "a different object must not"
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no allocation counter.
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
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no allocation counter.
            untracedOnly
            <| test "notifying observers allocates nothing" {
                // Notification has to walk a copy, because marking a dependent
                // dirty can drop the edge being walked. Taking that copy with
                // Seq.toArray allocated an array per write; ObserverSet reuses one.
                let g = new Graph ()
                let s = Signal (g, 0)
                let a = Make.Memo (g, (fun _ -> s.Value + 1))
                let b = Make.Memo (g, (fun _ -> s.Value + 2))
                let c = Make.Memo (g, (fun _ -> s.Value + 3))

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
#endif

#if !FABLE_COMPILER
            // .NET only: a custom comparer goes through GraphOptions.
            test "a throwing memo comparer fails the memo and its dependents" {
                let g, policy = throwingGraph ()
                let s = Signal (g, "1")
                let source = Make.Memo (g, (fun _ -> int s.Value))
                let dependent = Make.Memo (g, (fun _ -> string (source.Value + 1000)))
                let seen = ResizeArray<string>()
                let effect = new Effect (g, (fun () -> seen.Add dependent.Value))

                Expect.sequenceEqual seen [ "1001" ] "precondition: the effect ran"
                policy.Armed <- true
                s.Value <- "99"

                Expect.equal (message source.TryValue) "Failed comparer" "the comparer's exception fails the memo"
                Expect.equal (message dependent.TryValue) "Failed comparer" "a dependent reads the failure, not \"1001\""
                Expect.equal effect.Status Status.Error "the effect reran and read the failure"
                Expect.sequenceEqual seen [ "1001" ] "the effect saw no stale value"

                policy.Armed <- false
                s.Value <- "100"

                Expect.equal (message source.TryValue) "Ready 100" "the memo recovers on the next change"
                Expect.equal (message dependent.TryValue) "Ready \"1100\"" "and so does its dependent"
                Expect.sequenceEqual seen [ "1001"; "1100" ] "the effect saw the recovered value"
            }

            test "a memo failed by its comparer passes its previous value to the next run" {
                let g, policy = throwingGraph ()
                let s = Signal (g, "1")
                let arguments = ResizeArray<int voption>()

                let m =
                    Make.Memo (
                        g,
                        fun last ->
                            arguments.Add last
                            int s.Value
                    )

                m.TryValue |> ignore
                m.TryValue |> ignore
                policy.Armed <- true
                s.Value <- "2"
                m.TryValue |> ignore
                policy.Armed <- false
                s.Value <- "3"

                Expect.equal (message m.TryValue) "Ready 3" "the memo recovers"
                Expect.sequenceEqual arguments [ ValueNone; ValueSome 1; ValueSome 1 ] "the failed run published nothing"
            }

            test "a throwing boundary comparer fails the boundary without calling recover" {
                let g, policy = throwingGraph ()
                let s = Signal (g, "1")
                let recovered = ref 0

                let b =
                    Boundary<int>
                        .Errors(
                            g,
                            (fun () -> int s.Value),
                            fun _ _ ->
                                recovered.Value <- recovered.Value + 1
                                -1
                        )

                let dependent = Make.Memo (g, (fun _ -> string (b.Value + 1000)))
                Expect.equal (message dependent.TryValue) "Ready \"1001\"" "precondition"

                policy.Armed <- true
                s.Value <- "99"

                Expect.equal (attempt (fun () -> b.Value)) "Failed comparer" "the boundary fails"
                Expect.equal (message dependent.TryValue) "Failed comparer" "its dependent reads the failure"
                Expect.equal recovered.Value 0 "recover handles the body's exceptions only"

                policy.Armed <- false
                s.Value <- "100"
                Expect.equal (message dependent.TryValue) "Ready \"1100\"" "the boundary recovers on the next change"
            }

            test "a throwing effectOn comparer fails the effect without acting" {
                let g, policy = throwingGraph ()
                use _ = g.Activate ()
                let s = createSignal "1"
                let acted = ResizeArray<int>()
                let seen = ResizeArray<string>()

                createEffectOn (fun () -> int s.Value) acted.Add
                createEffect (fun () -> seen.Add s.Value)

                policy.Armed <- true
                s.Value <- "2"

                Expect.sequenceEqual acted [ 1 ] "act does not run with a value the comparer could not test"
                Expect.sequenceEqual seen [ "1"; "2" ] "the effect queued behind it still runs"

                policy.Armed <- false
                s.Value <- "3"
                Expect.sequenceEqual acted [ 1; 3 ] "the next change acts"
            }

            test "a throwing lookup comparer fails the key's cell" {
                let g, policy = throwingGraph ()
                use _ = g.Activate ()
                let selected = createSignal "a"

                let lookup =
                    createLookup (fun (s: string) (k: string) -> if s = k then 1 else 0) (fun prev next -> [ prev; next ]) (fun () -> selected.Value)

                let seen = ResizeArray<string>()
                createEffect (fun () -> seen.Add (attempt (fun () -> lookup.Get "a")))

                policy.Armed <- true
                selected.Value <- "b"

                Expect.equal (attempt (fun () -> lookup.Get "a")) "Failed comparer" "the cell fails"
                Expect.sequenceEqual seen [ "Ready 1"; "Failed comparer" ] "its reader wakes to the failure"

                policy.Armed <- false
                selected.Value <- "c"

                Expect.equal (attempt (fun () -> lookup.Get "a")) "Ready 0" "the failed cell recomputes on the next change"
                Expect.sequenceEqual seen [ "Ready 1"; "Failed comparer"; "Ready 0" ] "and its reader sees it"
            }

            test "a throwing row comparer fails the projection row and a fold over it" {
                let g, policy = throwingGraph ()
                use _ = g.Activate ()
                let source = createSignal [ "a", 1; "b", 2 ]
                let rows = createProjection fst snd (fun () -> source.Value)
                let total = rows |> Projection.foldGroup (+) (-) 0
                let seen = ResizeArray<string>()
                createEffect (fun () -> seen.Add (attempt (fun () -> total.Value)))

                policy.Armed <- true
                source.Value <- [ "a", 1; "b", 20 ]

                Expect.equal (attempt (fun () -> rows.Get "b")) "Failed comparer" "the row fails"
                Expect.equal (attempt (fun () -> rows.Get "a")) "Ready 1" "the other row is unaffected"
                Expect.sequenceEqual seen [ "Ready 3"; "Failed comparer" ] "the fold's reader wakes to the failure"

                policy.Armed <- false
                source.Value <- [ "a", 1; "b", 30 ]

                Expect.sequenceEqual seen [ "Ready 3"; "Failed comparer"; "Ready 31" ] "the fold recovers on the next change"
            }
#endif
        ]
