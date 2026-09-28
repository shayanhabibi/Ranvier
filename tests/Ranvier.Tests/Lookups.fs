module Ranvier.Tests.Lookups

open Expecto
open Ranvier

type private Cell = { Row: int; Column: int }

[<Tests>]
let tests =
    testList
        "Lookups"
        [
            test "a lookup reads through to the source" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1

                let lookup =
                    createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () -> selected.Value)

                Expect.isTrue (lookup.Get 1) "1 is selected"
                Expect.isFalse (lookup.Get 2) "2 is not"
            }

            test "only the affected keys are recomputed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let computed = ResizeArray<int>()

                let lookup =
                    createLookup
                        (fun s k ->
                            computed.Add k
                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                for k in 1..5 do
                    createEffect (fun () -> lookup.Get k |> ignore)

                computed.Clear ()
                selected.Value <- 3

                Expect.sequenceEqual (List.ofSeq computed |> List.sort) [ 1; 3 ] "1 and 3 only, not all five"
            }

            test "only the affected rows wake" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let runs = System.Collections.Generic.Dictionary<int, int>()

                let lookup =
                    createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () -> selected.Value)

                for k in 1..4 do
                    createEffect (fun () ->
                        lookup.Get k |> ignore

                        runs[k] <-
                            (match runs.TryGetValue k with
                             | true, n -> n + 1
                             | _ -> 1))

                selected.Value <- 2

                Expect.equal runs[1] 2 "deselected"
                Expect.equal runs[2] 2 "selected"
                Expect.equal runs[3] 1 "untouched"
                Expect.equal runs[4] 1 "untouched"
            }

            test "a lookup built and read inside a batch sees the source" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1

                let lookup, inside =
                    batch (fun () ->
                        let lookup =
                            createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () -> selected.Value)

                        lookup, lookup.Get 1)

                Expect.isTrue inside "1 is selected inside the batch"
                Expect.isTrue (lookup.Get 1) "and stays selected after the batch"
                Expect.isFalse (lookup.Get 2) "2 is not selected"
            }

            test "a lookup built and read inside an effect sees the source" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let seen = ResizeArray<bool>()
                let mutable lookup = Unchecked.defaultof<Lookup<int, bool>>

                createEffect (fun () ->
                    if isNull (box lookup) then
                        lookup <- createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () -> selected.Value)

                    seen.Add (lookup.Get 1))

                Expect.sequenceEqual seen [ true ] "1 is selected on the effect's first run"
                Expect.isTrue (lookup.Get 1) "and stays selected"
            }

            test "reference-type state is read before the first cell is computed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let name = createSignal "abc"

                let length =
                    batch (fun () ->
                        let lookup =
                            createLookup (fun (s: string) k -> s.Length + k) (fun _ _ -> [ 0 ]) (fun () -> name.Value)

                        lookup.Get 0)

                Expect.equal length 3 "the state is the source's value, not null"
            }

            test "a read after a write in the same batch sees the write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1

                let lookup =
                    createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () -> selected.Value)

                Expect.isTrue (lookup.Get 1) "1 is selected"

                let now1, now3 =
                    batch (fun () ->
                        selected.Value <- 3
                        lookup.Get 1, lookup.Get 3)

                Expect.isFalse now1 "1 was deselected by the write"
                Expect.isTrue now3 "3 was selected by the write"
            }

            test "a consumer of the source and the lookup sees them agree" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1

                let lookup =
                    createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () -> selected.Value)

                let seen = ResizeArray<int * bool>()
                createEffect (fun () -> seen.Add (selected.Value, lookup.Get 2))

                selected.Value <- 2
                selected.Value <- 3

                for s, isTwo in seen do
                    Expect.equal isTwo (s = 2) $"selected = %d{s} and Get 2 = %b{isTwo}"

                Expect.equal (Seq.last seen) (3, false) "the last run saw the last write"
            }

            test "a failing key leaves the other affected keys current" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let mutable failing = false

                let lookup =
                    createLookup
                        (fun s k ->
                            if failing && k = 1 then
                                failwith "key 1 failed"

                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                let rows = System.Collections.Generic.Dictionary<int, Effect>()

                for k in 1..3 do
                    rows[k] <- new Effect (g, fun () -> lookup.Get k |> ignore)

                failing <- true
                selected.Value <- 2

                Expect.isTrue (lookup.Get 2) "2 was recomputed after 1 threw"
                Expect.equal rows[1].Status Status.Error "the row reading 1 was handed the failure"
                Expect.equal rows[2].Status Status.None "the row reading 2 was not"
                Expect.throws (fun () -> lookup.Get 1 |> ignore) "a read of 1 raises the failure"
            }

            test "a failed key is recomputed on the next transition" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let mutable failing = false

                let lookup =
                    createLookup
                        (fun s k ->
                            if failing && k = 1 then
                                failwith "key 1 failed"

                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                let seen = ResizeArray<bool>()
                let row = new Effect (g, fun () -> seen.Add (lookup.Get 1))

                failing <- true
                selected.Value <- 2
                failing <- false
                selected.Value <- 3

                Expect.isFalse (lookup.Get 1) "1 is not selected while 3 is"
                Expect.equal row.Status Status.None "the row recovered"
                Expect.equal (Seq.last seen) false "and was woken with the recomputed value"
            }

            test "a key that fails on its first read is recomputed on the next transition" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let mutable failing = true

                let lookup =
                    createLookup
                        (fun s k ->
                            if failing then
                                failwith "failed"

                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                let seen = ResizeArray<bool>()
                let row = new Effect (g, fun () -> seen.Add (lookup.Get 5))

                Expect.equal row.Status Status.Error "the first read failed"

                failing <- false
                selected.Value <- 2

                Expect.equal row.Status Status.None "the row was woken and recovered"
                Expect.sequenceEqual seen [ false ] "with the value computed against 2"
            }

            test "a failing source reaches the readers of every live cell" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1

                let lookup =
                    createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () ->
                        if selected.Value < 0 then
                            failwith "negative"

                        selected.Value)

                let row = new Effect (g, fun () -> lookup.Get 4 |> ignore)

                selected.Value <- -1
                Expect.equal row.Status Status.Error "a row outside affected learns of the failure"

                selected.Value <- 4
                Expect.equal row.Status Status.None "and recovers with the source"
                Expect.isTrue (lookup.Get 4) "against the recovered value"
            }

            test "a pending source suspends each row once, and the rows recover when it settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int>()

                let lookup =
                    createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () -> source.Value)

                let runs = [| 0; 0 |]
                let seen = [| ResizeArray<bool>(); ResizeArray<bool>() |]

                // The cap stops a runaway flush so the assertions below can report it.
                let row i =
                    new Effect (
                        g,
                        fun () ->
                            runs[i] <- runs[i] + 1

                            if runs[i] <= 100 then
                                seen[i].Add(lookup.Get (i + 1))
                    )

                let rows = [| row 0; row 1 |]

                Expect.isLessThanOrEqual runs[0] 2 "the first row ran a bounded number of times"
                Expect.isLessThanOrEqual runs[1] 2 "the second row ran a bounded number of times"
                Expect.equal rows[0].Status Status.Pending "the first row is suspended, not failed"
                Expect.equal rows[1].Status Status.Pending "the second row is suspended, not failed"

                source.Settle 1

                Expect.equal rows[0].Status Status.None "the first row recovered"
                Expect.equal rows[1].Status Status.None "the second row recovered"
                Expect.sequenceEqual seen[0] [ true ] "the first row saw 1 selected"
                Expect.sequenceEqual seen[1] [ false ] "the second row saw 2 unselected"
            }

            test "a source that becomes pending suspends its rows until it settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let first = createAsyncSource<int>()
                let second = createAsyncSource<int>()
                let useSecond = createSignal false

                let lookup =
                    createLookup (fun s k -> s = k) (fun prev next -> [ prev; next ]) (fun () ->
                        if useSecond.Value then second.Value else first.Value)

                first.Settle 1
                let seen = ResizeArray<bool>()
                let mutable runs = 0

                let row =
                    new Effect (
                        g,
                        fun () ->
                            runs <- runs + 1

                            if runs <= 100 then
                                seen.Add (lookup.Get 1)
                    )

                useSecond.Value <- true
                Expect.equal row.Status Status.Pending "the row is suspended while the new source is pending"
                Expect.isLessThanOrEqual runs 3 "and ran a bounded number of times"

                second.Settle 1
                Expect.equal row.Status Status.None "the row recovered"
                Expect.equal (Seq.last seen) true "with the value computed against the settled source"
            }

            test "createSelector reports membership and wakes only the two ends" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)
                let runs = System.Collections.Generic.Dictionary<int, int>()

                for k in 1..4 do
                    createEffect (fun () ->
                        selector.Get k |> ignore

                        runs[k] <-
                            (match runs.TryGetValue k with
                             | true, n -> n + 1
                             | _ -> 1))

                Expect.isTrue (selector.Get 1) "1 is selected"

                selected.Value <- 3

                Expect.isFalse (selector.Get 1) "1 was deselected"
                Expect.isTrue (selector.Get 3) "3 was selected"
                Expect.equal runs[1] 2 "1 woke"
                Expect.equal runs[3] 2 "3 woke"
                Expect.equal runs[2] 1 "2 never moved"
                Expect.equal runs[4] 1 "4 never moved"
            }

            test "a cell whose observers all go away is evicted" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                let owner =
                    createRoot (fun owner ->
                        createEffect (fun () -> selector.Get 7 |> ignore)
                        owner)

                Expect.equal selector.CellCount 1 "one live cell"

                owner.Dispose ()

                // Eviction happens at the lookup's next transition.
                selected.Value <- 2

                Expect.equal selector.CellCount 0 "the unobserved cell was dropped"
            }

            test "an observed cell survives a transition that names it" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)
                let seen = ResizeArray<bool>()

                createEffect (fun () -> seen.Add (selector.Get 7))

                selected.Value <- 7

                Expect.equal selector.CellCount 1 "the observed cell is kept"
                Expect.sequenceEqual seen [ false; true ] "and its reader saw the transition"
            }

            test "an evicted key is rebuilt against the current source on its next read" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                let owner =
                    createRoot (fun owner ->
                        createEffect (fun () -> selector.Get 7 |> ignore)
                        owner)

                owner.Dispose ()
                selected.Value <- 7
                Expect.equal selector.CellCount 0 "the cell was dropped"

                let seen = ResizeArray<bool>()
                createEffect (fun () -> seen.Add (selector.Get 7))
                selected.Value <- 2

                Expect.sequenceEqual seen [ true; false ] "the rebuilt cell is current and wakes its reader"
            }

            test "an unobserved failed key is evicted on the next transition" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1

                let lookup =
                    createLookup
                        (fun s k ->
                            if k = 9 && s = 1 then
                                failwith "boom"

                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                Expect.throws (fun () -> lookup.Get 9 |> ignore) "9 fails while 1 is selected"
                Expect.equal lookup.CellCount 1 "the failed cell is live"

                selected.Value <- 2

                Expect.equal lookup.CellCount 0 "the failed cell nobody observes was dropped"
            }

            test "Get on a disposed lookup raises and creates no cell" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                selector.Dispose ()

                Expect.throwsT<System.ObjectDisposedException> (fun () -> selector.Get 1 |> ignore) "reading a disposed lookup raises"
                Expect.equal selector.CellCount 0 "and leaves no cell behind"
            }

            test "TryGet on a disposed lookup raises as Get does" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                selector.Dispose ()

                Expect.throwsT<System.ObjectDisposedException> (fun () -> selector.TryGet 1 |> ignore) "reading a disposed lookup raises"
            }

            test "cells of disposed readers are evicted by transitions that never name their keys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                let owners =
                    [
                        for i in 100..1099 ->
                            createRoot (fun owner ->
                                createEffect (fun () -> selector.Get i |> ignore)
                                owner)
                    ]

                Expect.equal selector.CellCount 1000 "one live cell per row"

                for owner in owners do
                    owner.Dispose ()

                for k in 2..5 do
                    selected.Value <- k

                Expect.equal selector.CellCount 0 "every row's cell was dropped"
            }

            test "untracked reads leave no cell behind after the next transition" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                for i in 100..1099 do
                    Expect.isFalse (selector.Get i) "an unselected key reads false"

                Expect.isTrue (selector.Get 1) "the selected key reads true"

                selected.Value <- 2

                Expect.equal selector.CellCount 0 "every untracked cell was dropped"
            }

            test "repeated untracked reads of one key compute it once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let computes = ref 0

                let lookup =
                    createLookup
                        (fun s k ->
                            computes.Value <- computes.Value + 1
                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                for _ in 1..3 do
                    Expect.isFalse (lookup.Get 7) "7 is not selected"

                Expect.equal computes.Value 1 "the unobserved cell is reused by the next read of its key"
            }

            test "a key remounted between transitions keeps its cell" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let computes = ref 0

                let lookup =
                    createLookup
                        (fun s k ->
                            computes.Value <- computes.Value + 1
                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                for _ in 1..3 do
                    let owner =
                        createRoot (fun owner ->
                            createEffect (fun () -> lookup.Get 7 |> ignore)
                            owner)

                    owner.Dispose ()

                Expect.equal computes.Value 1 "each mount reads the cell the previous one left"
            }

            test "a read inserted ahead of Get keeps the cell" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let flag = createSignal false
                let other = createSignal 0
                let computes = ref 0

                let lookup =
                    createLookup
                        (fun s k ->
                            computes.Value <- computes.Value + 1
                            s = k)
                        (fun prev next -> [ prev; next ])
                        (fun () -> selected.Value)

                createEffect (fun () ->
                    if flag.Value then
                        other.Value |> ignore

                    lookup.Get 7 |> ignore)

                flag.Value <- true
                flag.Value <- false
                flag.Value <- true

                Expect.equal computes.Value 1 "the re-runs read the same cell"
            }

            test "untracked reads of distinct keys leave one cell" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                for i in 100..1099 do
                    selector.Get i |> ignore
                    selector.Get i |> ignore

                Expect.equal selector.CellCount 1 "only the key read last keeps its cell"
            }

            test "a kept cell is current after a transition" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let selector = createSelector (fun () -> selected.Value)

                Expect.isFalse (selector.Get 7) "7 is not selected"
                selected.Value <- 7
                Expect.isTrue (selector.Get 7) "the next read sees the transition"
            }

            test "a reader that moves between keys keeps its current cell" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal 1
                let key = createSignal 7
                let selector = createSelector (fun () -> selected.Value)
                let seen = ResizeArray<bool>()

                createEffect (fun () -> seen.Add (selector.Get key.Value))

                key.Value <- 8
                selected.Value <- 3

                Expect.equal selector.CellCount 1 "only the key being read is live"

                selected.Value <- 8

                Expect.sequenceEqual seen [ false; false; true ] "the reader wakes when its current key is selected"
            }

            test "a selector over record keys matches them by structure" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal { Row = 1; Column = 1 }
                let selector = createSelector (fun () -> selected.Value)
                let seen = ResizeArray<bool>()

                createEffect (fun () -> seen.Add (selector.Get { Row = 2; Column = 1 }))

                selected.Value <- { Row = 2; Column = 1 }
                selected.Value <- { Row = 1; Column = 1 }

                Expect.sequenceEqual seen [ false; true; false ] "an equal record wakes the reader of its cell"
                Expect.equal selector.CellCount 1 "one cell serves every equal record"
            }

            test "a lookup keeps None and Some cells apart" {
                use g = new Graph ()
                use _ = g.Activate ()
                let selected = createSignal (None: (int * int) option)
                let selector = createSelector (fun () -> selected.Value)

                Expect.isTrue (selector.Get None) "None is selected"
                Expect.isFalse (selector.Get (Some (1, 2))) "Some is not"

                selected.Value <- Some (1, 2)

                Expect.isFalse (selector.Get None) "None is no longer selected"
                Expect.isTrue (selector.Get (Some (1, 2))) "an equal tuple is"
            }
        ]
