module Ranvier.Tests.Observers

open Expecto
open Ranvier

/// <summary>
/// A source's observer list is an array with a position index built only once
/// the fan-out is wide enough to want one. Both halves of that have failure
/// modes a narrow test never reaches: a stale index entry after a swap-remove
/// silently stops waking an observer, and a missed dedupe silently appends the
/// same observer on every run until the array is the size of the run count.
/// </summary>
/// <remarks>
/// So every case here is run at a fan-out on both sides of the threshold.
/// </remarks>
let private widths = [ 3; 8; 9; 20 ]

[<Tests>]
let tests =
    testList
        "Observers"
        [
            for width in widths do
                test $"every one of {width} observers is woken" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let seen = ResizeArray ()

                    for i in 1..width do
                        new Effect (g, (fun () -> seen.Add (i, s.Value)))
                        |> ignore

                    seen.Clear ()
                    s.Value <- 1

                    Expect.equal seen.Count width "one run each"

                    Expect.sequenceEqual (seen |> Seq.map fst |> Seq.sort) (Seq.sort [ 1..width ]) "and no observer was skipped"
                }

                test $"a body reading its source twice does not grow the list of {width}" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let memos = ResizeArray<Memo<int>>()

                    // Reading the same source twice in one body is where a
                    // missing dedupe actually accumulates: the source set that
                    // drives detachment holds one entry, so a run removes one
                    // observer entry and adds two. Reading once per run is
                    // self-correcting and hides the bug entirely.
                    for _ in 1..width do
                        memos.Add (Make.Memo (g, (fun _ -> s.Value + s.Value)))

                    for v in 1..50 do
                        s.Value <- v

                        for m in memos do
                            m.Value |> ignore

                    // Counted rather than inferred from behaviour: the
                    // duplication changes nothing the graph computes, because a
                    // second dirty mark on a dirty node is a no-op. It shows up
                    // only as a list that is 50 times longer than it should be.
                    Expect.equal s.ObserverCount width "one entry per reader, however many times each read"
                }

                test $"a disposed observer among {width} stops being woken" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let seen = ResizeArray ()
                    let effects = ResizeArray<Effect>()

                    for i in 1..width do
                        effects.Add (
                            new Effect (
                                g,
                                fun () ->
                                    s.Value |> ignore
                                    seen.Add i
                            )
                        )

                    // The first, the middle and the last: a swap-remove fills
                    // the hole from the end, so removing the last entry and
                    // removing an interior one are different paths.
                    let dropped = [ 0; width / 2; width - 1 ] |> List.distinct

                    for i in dropped do
                        effects[i].Dispose()

                    seen.Clear ()
                    s.Value <- 1

                    let survivors =
                        [ 1..width ]
                        |> List.filter (fun i -> not (List.contains (i - 1) dropped))

                    Expect.sequenceEqual (seen |> Seq.sort) survivors "the survivors, and only the survivors"
                }

                test $"observers among {width} can be dropped in any order" {
                    let g = new Graph ()
                    let s = Signal (g, 0)
                    let seen = ResizeArray ()

                    let effects =
                        [|
                            for i in 1..width ->
                                new Effect (
                                    g,
                                    fun () ->
                                        s.Value |> ignore
                                        seen.Add i
                                )
                        |]

                    // Dropped from alternating ends rather than in order. A
                    // swap-remove moves the last entry into the hole, and above
                    // the index threshold that move has to be written back to
                    // the position index — a failure only observable when
                    // something is removed *after* it has been moved, which
                    // dropping in order never reaches.
                    let order =
                        [ for k in 0 .. width - 1 -> if k % 2 = 0 then k / 2 else width - 1 - k / 2 ]

                    let mutable written = 0
                    let mutable expected = width

                    for i in order do
                        effects[i].Dispose()
                        expected <- expected - 1

                        Expect.equal s.ObserverCount expected $"one fewer observer after dropping effect {i + 1}"

                        seen.Clear ()
                        written <- written + 1
                        s.Value <- written

                        Expect.equal seen.Count expected "and exactly the survivors ran"
                }

                test $"a memo among {width} that drops the branch stops being woken" {
                    let g = new Graph ()
                    let useLeft = Signal (g, true)
                    let left = Signal (g, 1)
                    let memos = ResizeArray<Memo<int>>()

                    for _ in 1..width do
                        memos.Add (Make.Memo (g, (fun _ -> if useLeft.Value then left.Value else 0)))

                    for m in memos do
                        m.TryValue |> ignore

                    useLeft.Value <- false

                    for m in memos do
                        m.TryValue |> ignore

                    let before = memos |> Seq.map (fun m -> m.Runs) |> Seq.toList
                    left.Value <- 99
                    let after = memos |> Seq.map (fun m -> m.Runs) |> Seq.toList

                    Expect.sequenceEqual after before "a source nobody reads wakes nobody"
                }
        ]

/// <summary>
/// The other half of the edge: what a computation records about the sources it
/// read. That list is matched positionally against the next run's reads, so it
/// has failure modes of its own — a stale entry keeps waking a computation that
/// no longer reads it, and a missing duplicate check makes the list as long as
/// the read count.
/// </summary>
[<Tests>]
let sourceTests =
    testList
        "Sources"
        [
            test "a memo that stops reading a source stops recomputing for it" {
                let g = new Graph ()
                let useLeft = Signal (g, true)
                let left = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> if useLeft.Value then left.Value else 0))

                m.Value |> ignore
                useLeft.Value <- false
                m.Value |> ignore

                let before = m.Runs
                left.Value <- 99
                m.Value |> ignore

                // Read *after* the write, unlike the observer-side test: a stale
                // edge marks the memo dirty, and only a read turns that into a
                // recomputation the count can see.
                Expect.equal m.Runs before "the branch it no longer takes cannot wake it"
            }

            test "a memo that reads one source in a loop records one edge" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            let mutable total = 0

                            for _ in 1..20 do
                                total <- total + s.Value

                            total
                    )

                for v in 1..5 do
                    s.Value <- v
                    m.Value |> ignore

                Expect.equal m.SourceCount 1 "one edge, however many times it was read"
                Expect.equal s.ObserverCount 1 "and one observer entry on the other side"
            }

            test "a memo that reorders its reads ends up with both edges" {
                let g = new Graph ()
                let flip = Signal (g, false)
                let a = Signal (g, 1)
                let b = Signal (g, 10)

                // Reversing the read order is the case positional matching
                // cannot reuse: the first read mismatches its slot and the whole
                // list is rebuilt. Both edges still have to be live afterwards.
                let m =
                    Make.Memo (
                        g,
                        (fun _ ->
                            if flip.Value then
                                b.Value * 100 + a.Value
                            else
                                a.Value * 100 + b.Value)
                    )

                Expect.equal m.Value 110 "a then b"
                flip.Value <- true
                Expect.equal m.Value 1001 "b then a"
                Expect.equal m.SourceCount 3 "flip, b, a"

                a.Value <- 2
                Expect.equal m.Value 1002 "the source read last still wakes it"
                b.Value <- 20
                Expect.equal m.Value 2002 "and so does the one read first"
            }

            test "a source read twice keeps its edge when the later read is trimmed" {
                let g = new Graph ()
                let second = Signal (g, false)
                let a = Signal (g, 1)
                let b = Signal (g, 10)
                let c = Signal (g, 100)

                // The first run records [second; a; b; a]. The second run
                // matches [second; a], then trims b and the repeated a.
                let m =
                    Make.Memo (
                        g,
                        (fun _ ->
                            if second.Value then
                                a.Value + c.Value
                            else
                                a.Value + b.Value + a.Value)
                    )

                Expect.equal m.Value 12 "a, b, a"
                second.Value <- true
                Expect.equal m.Value 101 "a, c"
                Expect.equal a.ObserverCount 1 "a still has the memo as an observer"

                a.Value <- 2
                Expect.equal m.Value 102 "a write to a still wakes the memo"
            }
        ]
