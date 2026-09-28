module Ranvier.Tests.Projections

open System
open Expecto
open Ranvier

type private Cell = { Row: int; Column: int }

/// <summary>
/// A projection is N+2 nodes, not one, because it splits a collection into two
/// halves — the key set and the per-key values — each separately observable.
/// These tests pin that separation, so a <c>Memo&lt;'T[]></c> that happened to pass
/// any test that only looked at contents would not pass here.
/// </summary>
[<Tests>]
let tests =
    testList
        "Projections"
        [
            test "the key order follows the source" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)

                Expect.sequenceEqual proj.Keys [ 1; 2; 3 ] "the initial key order"
                Expect.equal proj.Count 3 "and its length"

                items.Value <- [ 3; 1 ]

                Expect.sequenceEqual proj.Keys [ 3; 1 ] "the new order, in source order"
                Expect.equal proj.Count 2 "and its length"
            }

            test "a projection nothing reads is never recomputed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)

                for i in 1..10 do
                    items.Value <- [ i ]

                Expect.equal proj.Runs 0 "pull-based until something observes it"
                Expect.sequenceEqual proj.Keys [ 10 ] "and the first read is up to date"
                Expect.equal proj.Runs 1 "having cost exactly one pass"
            }

            test "reading Keys twice without a write costs one pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x) (fun () -> items.Value)

                proj.Keys |> ignore
                proj.Keys |> ignore
                Expect.equal proj.Runs 1 "the second read is cached"
            }

            test "Get returns the mapped value and TryGet reports absence" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)

                Expect.equal (proj.Get 1) 10 "mapped"
                Expect.equal (proj.TryGet 2) (Some 20) "mapped, as an option"
                Expect.equal (proj.TryGet 99) None "absent keys are None"
                Expect.throwsT<System.Collections.Generic.KeyNotFoundException> (fun () -> proj.Get 99 |> ignore) "and Get throws on them"
            }

            test "editing one row wakes that row and no other" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, "a"); (2, "b") ]
                let proj = createProjection fst snd (fun () -> items.Value)

                let runsOne = ref 0
                let runsTwo = ref 0

                createEffect (fun () ->
                    proj.Get 1 |> ignore
                    runsOne.Value <- runsOne.Value + 1)

                createEffect (fun () ->
                    proj.Get 2 |> ignore
                    runsTwo.Value <- runsTwo.Value + 1)

                Expect.equal runsOne.Value 1 "precondition"
                Expect.equal runsTwo.Value 1 "precondition"

                items.Value <- [ (1, "a!"); (2, "b") ]

                Expect.equal runsOne.Value 2 "row 1 changed, so row 1 woke"
                Expect.equal runsTwo.Value 1 "row 2 did not change, so row 2 did not wake"
            }

            test "a projection with no observed cell stays lazy" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let proj = createProjection id (fun x -> x) (fun () -> items.Value)

                // Untracked reads create no observer, so this must not make the
                // projection eager.
                proj.Keys |> ignore
                let before = proj.Runs

                for i in 2..10 do
                    items.Value <- [ i ]

                Expect.equal proj.Runs before "no observer, no work"
            }

            test "reordering the collection wakes Keys and no row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, "a"); (2, "b") ]
                let proj = createProjection fst snd (fun () -> items.Value)

                let keyRuns = ref 0
                let rowRuns = ref 0

                createEffect (fun () ->
                    proj.Keys |> ignore
                    keyRuns.Value <- keyRuns.Value + 1)

                createEffect (fun () ->
                    proj.Get 1 |> ignore
                    rowRuns.Value <- rowRuns.Value + 1)

                items.Value <- [ (2, "b"); (1, "a") ]

                Expect.equal keyRuns.Value 2 "the order moved, so the key set woke"
                Expect.equal rowRuns.Value 1 "but no value moved, so no row woke"
            }

            test "a removed key's scope is disposed and its cleanups run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let cleaned = ResizeArray<int>()

                let proj =
                    createProjectionWith
                        id
                        (fun item ->
                            let x = item ()
                            onCleanup (fun () -> cleaned.Add x)
                            fun () -> item () * 10)
                        (fun () -> items.Value)

                createEffect (fun () -> proj.Keys |> ignore)
                Expect.sequenceEqual cleaned [] "precondition: nothing removed yet"

                items.Value <- [ 1 ]

                // Rewritten for factory map semantics: this test used to expect
                // [ 1; 2 ], because every pass disposed the survivor's scope and
                // re-ran its map. A survivor now keeps its scope for its lifetime.
                Expect.sequenceEqual cleaned [ 2 ] "the removed key's cleanup, and the survivor's scope untouched"
                Expect.equal (proj.TryGet 2) None "and its cell is gone"
                Expect.equal (proj.TryGet 1) (Some 10) "while the survivor is untouched"
            }

            // Rewritten for factory map semantics: this test was "a surviving
            // key's cleanups are discharged before it is re-mapped", and expected
            // the survivor's scope to be torn down and rebuilt on every item
            // change. The scope now lives until the key is removed.
            test "a surviving key keeps its scope when its item changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, "a") ]
                let log = ResizeArray<string>()

                let proj =
                    createProjectionWith
                        fst
                        (fun item ->
                            let (_, first) = item ()
                            log.Add $"factory %s{first}"
                            onCleanup (fun () -> log.Add $"clean %s{first}")

                            fun () ->
                                let (_, v) = item ()
                                log.Add $"read %s{v}"
                                v)
                        (fun () -> items.Value)

                createEffect (fun () -> proj.Get 1 |> ignore)
                Expect.sequenceEqual log [ "factory a"; "read a" ] "precondition"

                items.Value <- [ (1, "b") ]

                Expect.sequenceEqual log [ "factory a"; "read a"; "read b" ] "the reader re-ran; the scope survived"

                items.Value <- []

                Expect.sequenceEqual log [ "factory a"; "read a"; "read b"; "clean a" ] "and is disposed with the key"
            }

            test "an added key gets a cell without disturbing the others" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let rowRuns = ref 0

                createEffect (fun () ->
                    proj.Get 1 |> ignore
                    rowRuns.Value <- rowRuns.Value + 1)

                items.Value <- [ 1; 2 ]

                Expect.equal (proj.Get 2) 20 "the new key has a cell"
                Expect.equal rowRuns.Value 1 "and the existing row did not wake"
            }

            // The failure mode this guards is a hang, not a wrong answer. The
            // equivalent bug in `Effect` (fixed in 1cc79ec) consumed 43 GB and
            // never terminated, so this test carries its own timeout: a
            // regression must fail, not wedge.
            testCaseAsync "a removed item's cleanup may write the source without hanging"
            <| async {
                let run =
                    async {
                        use g = new Graph ()
                        use _ = g.Activate ()
                        let items = createSignal [ 1; 2; 3 ]
                        let removals = createSignal 0
                        let reentered = ref false

                        let proj =
                            createProjectionWith
                                id
                                (fun item ->
                                    let x = item ()

                                    onCleanup (fun () ->
                                        // The hazard: a cleanup writing a
                                        // signal while the projection is
                                        // mid-disposal.
                                        let n = removals.Peek + 1

                                        // A regression here is a flush loop, and
                                        // the async timeout below cannot interrupt
                                        // one — it times out the waiter, not the
                                        // compute-bound child, which spins until
                                        // the host dies of memory. So the cleanup
                                        // starves the loop rather than throwing:
                                        // past 16 writes it stops writing, which
                                        // removes the invalidation feeding the next
                                        // pass. `failwith` would not work —
                                        // `Owner.DisposeScope` swallows a cleanup's
                                        // exception into `Owner.Errors`, which this
                                        // test cannot reach, and the loop would
                                        // carry on regardless. The flag is what
                                        // makes a regression name itself instead of
                                        // reporting a bare wrong count.
                                        if n > 16 then
                                            reentered.Value <- true
                                        else
                                            removals.Value <- n)

                                    fun () -> x * 10)
                                (fun () ->
                                    // Reading `removals` makes the projection a
                                    // consumer of what its own cleanups write.
                                    removals.Value |> ignore
                                    items.Value)

                        createEffect (fun () -> proj.Keys |> ignore)

                        items.Value <- [ 1 ]

                        Expect.isFalse reentered.Value "the projection re-entered its own disposal phase and only the tripwire stopped it"

                        Expect.sequenceEqual proj.Keys [ 1 ] "the diff completed"

                        // Two writes, and the breakdown is the whole guard:
                        //   pass 2 — keys 2 and 3 removed (1, 2). The removal
                        //            writes are deferred, not suppressed, so they
                        //            buy a pass.
                        //   pass 3 — nothing removed, so nothing is deferred and
                        //            the graph settles.
                        // A count of 3 or more would mean the deferral re-armed
                        // itself; a hang would mean it re-entered. Under the
                        // original map semantics the count was 4: survivor 1's
                        // scope was also torn down on each of the two passes.
                        Expect.equal removals.Peek 2 "each removed key's cleanup ran once, and the deferral bought exactly one extra pass"
                    }

                let! finished = Async.StartChild (run, 5000)
                do! finished
            }

            test "a duplicate key throws rather than dropping an item" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 1 ]
                let proj = createProjection id (fun x -> x) (fun () -> items.Value)

                Expect.throwsT<System.InvalidOperationException> (fun () -> proj.Keys |> ignore) "two items, one key — silently losing one is worse"
            }

            test "the duplicate-key throw lands in an enclosing error boundary" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x) (fun () -> items.Value)

                let caught = ResizeArray<exn>()

                let guarded =
                    createErrorBoundary
                        (fun e _ ->
                            caught.Add e
                            [| -1 |])
                        (fun () -> proj.Keys)

                Expect.sequenceEqual guarded.Value [ 1; 2 ] "precondition: no error yet"

                items.Value <- [ 3; 3 ]

                Expect.sequenceEqual guarded.Value [ -1 ] "the boundary recovered"
                Expect.isGreaterThan caught.Count 0 "the handler saw the failure"

                Expect.all caught (fun e -> e :? System.InvalidOperationException) "the duplicate-key throw, not some other failure"

                // A SECOND failing pass, read through the boundary again. This is
                // the read count that used to wedge it: the first failed pass
                // unwound past the boundary's tracked read of the keys cell, its
                // `EndRun` pruned the edge, and a computation that ends a run with
                // no sources can never be marked dirty again — so the boundary sat
                // on its fallback for ever while `proj.Keys` reported the truth.
                items.Value <- [ 7; 7 ]

                Expect.sequenceEqual guarded.Value [ -1 ] "still on the fallback, one failing pass later"

                items.Value <- [ 4; 5 ]

                Expect.sequenceEqual guarded.Value [ 4; 5 ] "and it recovers once the source is fixed"
                Expect.sequenceEqual proj.Keys [ 4; 5 ] "the projection agrees with the boundary"
            }

            test "an eager failed pass is recorded, not swallowed, with no boundary in sight" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x) (fun () -> items.Value)
                let seen = ResizeArray<int[]>()

                // No boundary: the throw has nowhere to go. It must not break the
                // flush loop, and it must not vanish either.
                use watcher = new Effect (g, (fun () -> seen.Add proj.Keys))

                Expect.equal seen.Count 1 "the effect ran once"

                items.Value <- [ 3; 3 ]

                Expect.equal proj.Status Status.Error "the failed pass is visible on the projection"

                Expect.isTrue (proj.Error :? System.InvalidOperationException) "and it is the duplicate-key throw, readable the way Effect.Error is"

                Expect.equal watcher.Status Status.Error "the reader recorded the failure it was handed"

                Expect.isTrue (watcher.Error :? System.InvalidOperationException) "the duplicate-key throw reached the effect"

                // Self-limiting rather than a livelock: the throwing read never
                // completes, so nothing re-arms.
                Expect.equal seen.Count 1 "the throwing run appended nothing"

                // Self-limiting rather than a livelock, and the counts are the
                // whole mechanism:
                //   effect run 1 / pass 1 — construction, and it succeeds.
                //   pass 2          — the eager retry the write schedules. It
                //                     throws, is swallowed so the flush loop
                //                     survives, and wakes the beacon.
                //   effect run 2 / pass 3 — the woken reader pulls, gets the
                //                     exception, and records it. The beacon skips
                //                     the running reader, so nothing re-arms.
                Expect.equal watcher.Runs 2 "one construction run, one woken by the failed pass"
                Expect.equal proj.Runs 3 "the first pass, the eager retry, and the reader's own pull"
            }

            test "an index projection keeps the cell at a slot and updates its value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ "a"; "b" ]
                let proj = createIndexProjection id (fun () -> items.Value)
                let slotRuns = ref 0

                createEffect (fun () ->
                    proj.Get 0 |> ignore
                    slotRuns.Value <- slotRuns.Value + 1)

                Expect.sequenceEqual proj.Keys [ 0; 1 ] "keys are positions"

                items.Value <- [ "z"; "b" ]

                Expect.equal (proj.Get 0) "z" "slot 0 took the new value"
                Expect.equal slotRuns.Value 2 "and the slot's observer woke"
            }

            test "shortening an index projection drops the trailing slots" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ "a"; "b"; "c" ]
                let proj = createIndexProjection id (fun () -> items.Value)

                Expect.sequenceEqual proj.Keys [ 0; 1; 2 ] "precondition"

                items.Value <- [ "a" ]

                Expect.sequenceEqual proj.Keys [ 0 ] "one slot left"
                Expect.equal (proj.TryGet 2) None "and the trailing cells are gone"
            }

            test "the key set resolves while values are still pending" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]

                let flights =
                    dict [ 1, createAsyncSource<string>(); 2, createAsyncSource<string>() ]

                let proj = createProjection id (fun x -> flights[x].Value) (fun () -> items.Value)

                let rendered = createSuspense (fun _ -> "...") (fun () -> proj.Get 1)

                // Membership is known now; the value is not.
                Expect.sequenceEqual proj.Keys [ 1; 2 ] "N rows can be laid out immediately"
                Expect.equal rendered.Value "..." "while row 1 is still in flight"
                Expect.throwsT<NotReadyException> (fun () -> proj.TryGet 1 |> ignore) "TryGet suspends on a row in flight"
                Expect.equal proj.Status Status.None "a row in flight leaves the pass itself settled"

                flights[1].Settle "one"
                Expect.equal rendered.Value "one" "and settles on its own"
            }

            test "AnyPending and PendingKeys follow the rows in flight" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]

                let flights =
                    dict [ 1, createAsyncSource<string>(); 2, createAsyncSource<string>() ]

                let proj = createProjection id (fun x -> flights[x].Value) (fun () -> items.Value)

                // Rows are lazy: a row enters the summary once it has computed.
                createEffect (fun () ->
                    for key in proj.Keys do
                        try
                            proj.Get key |> ignore
                        with :? NotReadyException ->
                            ())

                Expect.isTrue proj.AnyPending "both rows are in flight"
                Expect.sequenceEqual proj.PendingKeys [ 1; 2 ] "and both are listed, in key order"

                flights[1].Settle "one"
                Expect.isTrue proj.AnyPending "one still in flight"
                Expect.sequenceEqual proj.PendingKeys [ 2 ] "row 2 alone"

                flights[2].Settle "two"
                Expect.isFalse proj.AnyPending "the collection is settled"
                Expect.sequenceEqual proj.PendingKeys [] "and the list is empty"

                items.Value <- [ 1 ]
                Expect.isFalse proj.AnyPending "removing a settled row leaves the collection settled"
            }

            test "an effect reading only AnyPending wakes when the last row settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let flights = dict [ 1, createAsyncSource<int>(); 2, createAsyncSource<int>() ]
                let proj = createProjection id (fun x -> flights[x].Value) (fun () -> items.Value)
                let seen = ResizeArray<bool>()

                // Rows are lazy: each is read once so it enters the summary. The
                // effect below reads neither row.
                for key in proj.Keys do
                    Expect.throwsT<NotReadyException> (fun () -> proj.Get key |> ignore) "precondition: the row is in flight"

                createEffect (fun () -> seen.Add proj.AnyPending)

                flights[1].Settle 10
                flights[2].Settle 20

                Expect.sequenceEqual seen [ true; false ] "woken once, on the transition to settled"
            }

            test "PendingKeys wakes when the set changes at a constant count" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]

                let flights =
                    dict
                        [
                            1, createAsyncSource<int>()
                            2, createAsyncSource<int>()
                            3, createAsyncSource<int>()
                        ]

                let proj = createProjection id (fun x -> flights[x].Value) (fun () -> items.Value)
                let seen = ResizeArray<int[]>()

                // Rows are lazy: the effect reads every row, so row 3 computes
                // when it arrives.
                createEffect (fun () ->
                    for key in proj.Keys do
                        try
                            proj.Get key |> ignore
                        with :? NotReadyException ->
                            ()

                    seen.Add proj.PendingKeys)

                flights[1].Settle 10

                // Row 2 leaves, row 3 arrives in flight: one row pending before and after.
                batch (fun () ->
                    items.Value <- [ 1; 3 ]
                    flights[2].Settle 20)

                Expect.sequenceEqual (Seq.last seen) [ 3 ] "the reader saw the swap"
            }

            test "PendingKeys follows a reorder of the pending rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3 ]
                let flights = dict [ for k in 1..3 -> k, createAsyncSource<int>() ]
                let proj = createProjection id (fun x -> flights[x].Value) (fun () -> items.Value)
                let seen = ResizeArray<int[]>()

                for key in proj.Keys do
                    Expect.throwsT<NotReadyException> (fun () -> proj.Get key |> ignore) "precondition: the row is in flight"

                flights[2].Settle 20
                createEffect (fun () -> seen.Add proj.PendingKeys)
                items.Value <- [ 3; 2; 1 ]

                Expect.sequenceEqual (Seq.last seen) [ 3; 1 ] "the reader saw the new order"
            }

            test "PendingKeys ignores a reorder with one row pending" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let flights = dict [ 1, createAsyncSource<int>(); 2, createAsyncSource<int>() ]
                let proj = createProjection id (fun x -> flights[x].Value) (fun () -> items.Value)
                let seen = ResizeArray<int[]>()

                for key in proj.Keys do
                    Expect.throwsT<NotReadyException> (fun () -> proj.Get key |> ignore) "precondition: the row is in flight"

                flights[1].Settle 10
                createEffect (fun () -> seen.Add proj.PendingKeys)
                items.Value <- [ 2; 1 ]

                Expect.equal seen.Count 1 "a reorder with one row pending does not wake the reader"
            }

            test "a row that goes back into flight suspends its readers again" {
                use g = new Graph ()
                use _ = g.Activate ()
                let first = createAsyncSource<string>()
                first.Settle "a"
                let second = createAsyncSource<string>()
                let current = createSignal first
                let items = createSignal [ 1 ]

                let proj =
                    createProjection id (fun _ -> current.Value.Value) (fun () -> items.Value)

                let seen = ResizeArray<string>()
                let rendered = createSuspense (fun _ -> "...") (fun () -> proj.Get 1)

                createEffect (fun () -> seen.Add rendered.Value)

                current.Value <- second
                Expect.isTrue proj.AnyPending "the row is back in flight"

                second.Settle "b"
                Expect.sequenceEqual seen [ "a"; "..."; "b" ] "settled, suspended, settled"
                Expect.isFalse proj.AnyPending "and the collection is settled again"
            }

            test "a projection over a pending source suspends its readers until the source settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createAsyncSource<int list>()
                let proj = createProjection id id (fun () -> items.Value)
                let seen = ResizeArray<int>()
                let counted = createSuspense (fun _ -> -1) (fun () -> proj.Count)

                createEffect (fun () -> seen.Add counted.Value)

                Expect.equal proj.Status Status.Pending "the pass suspended"

                // An empty key set, the same as the unpublished one: the keys cell
                // does not move, so the wake comes from the source alone.
                items.Settle []

                Expect.sequenceEqual seen [ -1; 0 ] "the reader woke when the source settled"
                Expect.equal proj.Status Status.None "and the pass settled"
            }

            test "a settled projection whose source goes pending wakes its readers" {
                use g = new Graph ()
                use _ = g.Activate ()
                let later = createAsyncSource<int list>()
                let live = createSignal true

                let proj =
                    createProjection id id (fun () -> if live.Value then [ 1; 2 ] else later.Value)

                let seen = ResizeArray<int>()
                let counted = createSuspense (fun _ -> -1) (fun () -> proj.Count)

                createEffect (fun () -> seen.Add counted.Value)

                live.Value <- false
                Expect.equal proj.Status Status.Pending "the pass suspended"

                later.Settle [ 1; 2 ]
                Expect.sequenceEqual seen [ 2; -1; 2 ] "settled, suspended, settled"
            }

            test "a pending pass resolved by another source wakes its readers" {
                use g = new Graph ()
                use _ = g.Activate ()
                let later = createAsyncSource<int list>()
                let live = createSignal true

                let proj =
                    createProjection id id (fun () -> if live.Value then [ 1; 2 ] else later.Value)

                let seen = ResizeArray<int>()
                let counted = createSuspense (fun _ -> -1) (fun () -> proj.Count)

                createEffect (fun () -> seen.Add counted.Value)

                live.Value <- false
                live.Value <- true

                Expect.sequenceEqual seen [ 2; -1; 2 ] "settled, suspended, settled over an unchanged key set"
            }

            test "a projection pending from its first pass wakes its readers when another source resolves it" {
                use g = new Graph ()
                use _ = g.Activate ()
                let later = createAsyncSource<int list>()
                let live = createSignal false

                let proj =
                    createProjection id id (fun () -> if live.Value then [ 1; 2 ] else later.Value)

                let seen = ResizeArray<int>()
                let counted = createSuspense (fun _ -> -1) (fun () -> proj.Count)

                createEffect (fun () -> seen.Add counted.Value)

                live.Value <- true

                Expect.sequenceEqual seen [ -1; 2 ] "the reader recovers with the awaited source still pending"
            }

            test "a row reader wakes when a suspended keyOf is switched back to synchronous" {
                use g = new Graph ()
                use _ = g.Activate ()
                let gate = createAsyncSource<int>()
                let useGate = createSignal false
                let items = createSignal [ 1; 2 ]

                let proj =
                    createProjection (fun x -> if useGate.Value && x = 2 then gate.Value + x else x) (fun x -> x * 10) (fun () -> items.Value)

                let seen = ResizeArray<int>()
                let row = createSuspense (fun _ -> -1) (fun () -> proj.Get 1)

                createEffect (fun () -> seen.Add row.Value)

                useGate.Value <- true
                useGate.Value <- false

                Expect.sequenceEqual seen [ 10; -1; 10 ] "settled, suspended, settled over an unchanged row"
            }

            test "a reader of a failed projection wakes when the failure clears" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let boom = createSignal false
                // The failure is in keyOf, so it fails the pass. A throw from map
                // fails its row alone, and leaves AnyPending readable.
                let proj =
                    createProjection (fun x -> if boom.Value && x = 2 then failwith "boom" else x) id (fun () -> items.Value)

                let seen = ResizeArray<string>()

                createEffect (fun () ->
                    seen.Add (
                        try
                            string proj.AnyPending
                        with e ->
                            e.Message
                    ))

                boom.Value <- true
                boom.Value <- false

                Expect.sequenceEqual seen [ "False"; "boom"; "False" ] "settled, failed, settled over unchanged rows"
            }

            test "a reader of a failed projection sees the exception of a later pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let boom = createSignal 0

                let proj =
                    createProjection
                        (fun x ->
                            if boom.Value > 0 && x = 2 then
                                failwithf "e%d" boom.Value
                            else
                                x)
                        id
                        (fun () -> items.Value)

                let seen = ResizeArray<string>()

                createEffect (fun () ->
                    seen.Add (
                        try
                            string proj.AnyPending
                        with e ->
                            e.Message
                    ))

                boom.Value <- 1
                boom.Value <- 2

                Expect.sequenceEqual seen [ "False"; "e1"; "e2" ] "each failure is seen"
            }

            test "a failed projection whose upstream memo resolves unchanged wakes no reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let m = createMemo (fun () -> s.Value / 10)
                let items = createSignal [ 1; 2 ]
                let mutable passes = 0

                let proj =
                    createProjection id id (fun () ->
                        passes <- passes + 1
                        ignore m.Value
                        items.Value)

                let mutable a = 0
                let mutable b = 0

                let reader (count: unit -> int) =
                    createEffect (fun () ->
                        if count () <= 50 then
                            try
                                proj.Keys |> ignore
                            with _ ->
                                ())

                reader (fun () ->
                    a <- a + 1
                    a)

                reader (fun () ->
                    b <- b + 1
                    b)

                items.Value <- [ 3; 3 ]
                Expect.equal (a, b) (2, 2) "precondition: one run each on the failure"
                let before = passes

                s.Value <- 1
                s.Value <- 2

                Expect.equal (a, b) (2, 2) "an unchanged memo re-runs no reader"
                Expect.equal (passes - before) 2 "one retry per write"
            }

            test "a failed projection fed by a memo sees the memo's change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let m = createMemo (fun () -> s.Value)

                let proj =
                    createProjection (fun x -> if x > 0 then failwithf "e%d" x else x) id (fun () -> [ m.Value ])

                let seen = ResizeArray<string>()

                createEffect (fun () ->
                    seen.Add (
                        try
                            string proj.Keys.Length
                        with e ->
                            e.Message
                    ))

                s.Value <- 1
                s.Value <- 2

                Expect.sequenceEqual seen [ "1"; "e1"; "e2" ] "each failure is seen"
            }

            test "a pass that suspends on keyOf creates no rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let gate = createAsyncSource<int>()
                let mutable mounts = 0
                let mutable cleanups = 0
                let items = createSignal [ 1; 2; 3 ]

                let proj =
                    createProjectionWith
                        (fun x -> if x = 3 then gate.Value + x else x)
                        (fun item ->
                            mounts <- mounts + 1
                            onCleanup (fun () -> cleanups <- cleanups + 1)
                            item)
                        (fun () -> items.Value)

                for _ in 1..5 do
                    Expect.throwsT<NotReadyException> (fun () -> proj.Keys |> ignore) "the pass suspends on item 3"

                // Factories run in the diff, after the pass enumerates the whole
                // source. The original semantics mapped rows during enumeration
                // and disposed them when the pass suspended.
                Expect.equal mounts 0 "a suspended pass runs no factory"

                gate.Settle 0
                Expect.sequenceEqual proj.Keys [ 1; 2; 3 ] "the pass completes once keyOf settles"
                Expect.equal mounts 3 "one factory run per key"

                items.Value <- []
                Expect.sequenceEqual proj.Keys [] "precondition: every key removed"
                Expect.equal cleanups mounts "every mount has its cleanup"
            }

            // Rewritten for factory map semantics: this test was "a row that reads
            // a node its own map created is re-mapped when that node settles", and
            // pinned the livelock of the original semantics. An unowned source
            // created by the reader still resets on every run; an owned node
            // throws (see MapSemantics).
            test "a reader that creates an async source creates a new one on every run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let created = ResizeArray<AsyncSource<int>>()

                let proj =
                    createProjection
                        id
                        (fun _ ->
                            let flight = createAsyncSource<int>()
                            created.Add flight
                            flight.Value)
                        (fun () -> items.Value)

                createEffect (fun () ->
                    try
                        proj.Get 1 |> ignore
                    with :? NotReadyException ->
                        ())

                Expect.isTrue proj.AnyPending "precondition: the row is in flight"

                created[0].Settle 10

                Expect.equal created.Count 2 "the settle re-ran the reader, which created a second flight"
                Expect.isTrue proj.AnyPending "and the row waits on the new flight"
            }

            test "two readers of a failing projection each run once per failure" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id id (fun () -> items.Value)
                let mutable a = 0
                let mutable b = 0

                let reader (count: unit -> int) =
                    createEffect (fun () ->
                        if count () <= 50 then
                            try
                                proj.Keys |> ignore
                            with _ ->
                                ())

                reader (fun () ->
                    a <- a + 1
                    a)

                reader (fun () ->
                    b <- b + 1
                    b)

                items.Value <- [ 3; 3 ]
                Expect.equal (a, b) (2, 2) "one run each on the failure"

                items.Value <- [ 4; 4 ]
                Expect.equal (a, b) (3, 3) "one run each on a second failure"

                items.Value <- [ 4; 5 ]
                Expect.equal (a, b) (4, 4) "one more run each on the recovery"
                Expect.sequenceEqual proj.Keys [ 4; 5 ] "the projection recovers"
            }
            test "Snapshot is untracked, so enumerating it subscribes to nothing" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let runs = ref 0

                createEffect (fun () ->
                    proj.Snapshot |> Seq.length |> ignore
                    runs.Value <- runs.Value + 1)

                Expect.equal runs.Value 1 "precondition"

                items.Value <- [ 1; 3 ]

                Expect.equal runs.Value 1 "a snapshot reader is not a subscriber"
                Expect.equal proj.Snapshot[3] 30 "and a later snapshot is current"
            }

            test "Snapshot lists the rows in key order" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 3; 1; 2 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)

                Expect.sequenceEqual (proj.Snapshot |> Seq.map (fun p -> p.Key, p.Value)) [ 3, 30; 1, 10; 2, 20 ] "key order"
            }

            test "Snapshot holds a None key beside Some keys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ Some 2; None; Some 1 ]
                let proj = createProjection id (Option.defaultValue 0) (fun () -> items.Value)

                let snapshot = proj.Snapshot

                Expect.equal snapshot.Count 3 "every row is present"
                Expect.isTrue (snapshot.ContainsKey None) "the None row is present"
                Expect.isFalse (snapshot.ContainsKey (Some 3)) "an absent key is absent"
                Expect.equal snapshot[None] 0 "the indexer finds the None row"
                Expect.equal snapshot[Some 2] 2 "and a Some row"

                let found, value = snapshot.TryGetValue None
                Expect.equal (found, value) (true, 0) "TryGetValue finds the None row"

                let found, _ = snapshot.TryGetValue (Some 3)
                Expect.isFalse found "TryGetValue misses an absent key"

                Expect.throwsT<System.Collections.Generic.KeyNotFoundException>
                    (fun () -> snapshot[Some 3] |> ignore)
                    "the indexer raises for an absent key"

                Expect.sequenceEqual snapshot.Keys [ Some 2; None; Some 1 ] "keys in key order"
                Expect.sequenceEqual snapshot.Values [ 2; 0; 1 ] "values in key order"

                Expect.sequenceEqual (snapshot |> Seq.map (fun p -> p.Key, p.Value)) [ Some 2, 2; None, 0; Some 1, 1 ] "pairs in key order"
            }

            test "AsObservableCollection follows the projection" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)

                let view = proj.AsObservableCollection ()
                Expect.sequenceEqual view [ 10; 20 ] "the initial contents"

                items.Value <- [ 2; 1; 3 ]
                Expect.sequenceEqual view [ 20; 10; 30 ] "and it follows, in key order"
            }

            test "a pending row shows its last settled value, and a row that never settled is left out" {
                use g = new Graph ()
                use _ = g.Activate ()
                let first = createAsyncSource<string>()
                first.Settle "a"
                let second = createAsyncSource<string>()
                let row1 = createSignal first
                let row2 = createAsyncSource<string>()
                let items = createSignal [ 1; 2 ]

                let proj =
                    createProjection id (fun x -> if x = 1 then row1.Value.Value else row2.Value) (fun () -> items.Value)

                let view = proj.AsObservableCollection ()

                let pairs () =
                    proj.Snapshot
                    |> Seq.map (fun p -> p.Key, p.Value)
                    |> Seq.toList

                Expect.equal (pairs ()) [ 1, "a" ] "row 2 has never settled"
                Expect.sequenceEqual view [ "a" ] "the view agrees"

                row1.Value <- second
                Expect.sequenceEqual proj.PendingKeys [ 1; 2 ] "precondition: both rows in flight"
                Expect.equal (pairs ()) [ 1, "a" ] "row 1 keeps its last settled value"
                Expect.sequenceEqual view [ "a" ] "in the view as well"

                row2.Settle "z"
                Expect.equal (pairs ()) [ 1, "a"; 2, "z" ] "row 2 appears once it settles"
                Expect.sequenceEqual view [ "a"; "z" ] "in the view as well"

                second.Settle "b"
                Expect.equal (pairs ()) [ 1, "b"; 2, "z" ] "row 1 takes its new value"
                Expect.sequenceEqual view [ "b"; "z" ] "in the view as well"
            }

            test "Snapshot raises while the pass is suspended, and the view keeps its contents" {
                use g = new Graph ()
                use _ = g.Activate ()
                let ready = createAsyncSource<int list>()
                ready.Settle [ 1; 2 ]
                let later = createAsyncSource<int list>()
                let current = createSignal ready
                let proj = createProjection id (fun x -> x * 10) (fun () -> current.Value.Value)
                let view = proj.AsObservableCollection ()

                current.Value <- later

                Expect.equal proj.Status Status.Pending "precondition: the pass suspended"
                Expect.throwsT<NotReadyException> (fun () -> proj.Snapshot |> ignore) "a snapshot has no key set to show"
                Expect.sequenceEqual view [ 10; 20 ] "the view keeps the last settled contents"

                later.Settle [ 3 ]
                Expect.sequenceEqual view [ 30 ] "and follows once the pass settles"
            }

            test "the view stops following when the projection is disposed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let view = proj.AsObservableCollection ()
                let changes = ref 0
                view.CollectionChanged.Add (fun _ -> changes.Value <- changes.Value + 1)

                proj.Dispose ()
                items.Value <- [ 3 ]

                Expect.sequenceEqual view [ 10; 20 ] "the view is frozen"
                Expect.equal changes.Value 0 "and nothing wrote to it"
                Expect.throwsT<ObjectDisposedException> (fun () -> proj.AsObservableCollection () |> ignore) "a disposed projection builds no view"
            }

            test "a view stops following when the scope that created it re-runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let tick = createSignal 0
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let views = ResizeArray<Collections.ObjectModel.ObservableCollection<int>>()

                createEffect (fun () ->
                    tick.Value |> ignore
                    views.Add (proj.AsObservableCollection ()))

                for i in 1..10 do
                    tick.Value <- i

                let changes = ref 0

                for view in views do
                    view.CollectionChanged.Add (fun _ -> changes.Value <- changes.Value + 1)

                items.Value <- [ 2 ]

                Expect.equal views.Count 11 "precondition: one view per run"
                Expect.sequenceEqual views[10] [ 20 ] "the live view follows"
                Expect.sequenceEqual views[0] [ 10 ] "a view from an earlier run is frozen"
                Expect.isGreaterThan changes.Value 0 "precondition: the live view changed"
                Expect.isLessThanOrEqual changes.Value 2 "only the live view changed"
            }

            test "a Snapshot read under a boundary wakes when the suspended pass settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let later = createAsyncSource<int list>()
                let proj = createProjection id (fun x -> x * 10) (fun () -> later.Value)
                let seen = ResizeArray<int>()
                let counted = createSuspense (fun _ -> -1) (fun () -> proj.Snapshot.Count)

                createEffect (fun () -> seen.Add counted.Value)

                later.Settle [ 1; 2 ]

                Expect.sequenceEqual seen [ -1; 2 ] "the boundary re-ran once the pass settled"
            }

            test "a tracked TryGet of an absent key wakes when the key arrives" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let seen = ResizeArray<int option>()

                createEffect (fun () -> seen.Add (proj.TryGet 2))

                items.Value <- [ 1; 2 ]

                Expect.sequenceEqual seen [ None; Some 20 ] "the reader saw the key arrive"
            }

            test "a tracked TryGet of a live key wakes when the key is removed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let seen = ResizeArray<int option>()

                createEffect (fun () -> seen.Add (proj.TryGet 2))

                items.Value <- [ 1 ]

                Expect.sequenceEqual seen [ Some 20; None ] "the reader saw the key leave"
            }

            test "a tracked TryGet follows a key that is removed and re-added" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, "a"); (2, "b") ]
                let proj = createProjection fst snd (fun () -> items.Value)
                let seen = ResizeArray<string option>()

                createEffect (fun () -> seen.Add (proj.TryGet 2))

                items.Value <- [ (1, "a") ]
                items.Value <- [ (1, "a"); (2, "c") ]

                Expect.sequenceEqual seen [ Some "b"; None; Some "c" ] "the reader followed the new row"
            }

            test "disposing the projection from a removed key's cleanup ends the pass cleanly" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3 ]
                let mutable proj = Unchecked.defaultof<Projection<int, int>>

                proj <-
                    createProjectionWith
                        id
                        (fun item ->
                            onCleanup (fun () -> proj.Dispose ())
                            fun () -> item () * 10)
                        (fun () -> items.Value)

                createEffect (fun () -> proj.Keys |> ignore)

                items.Value <- [ 1 ]

                Expect.equal proj.Status Status.None "the pass did not fail"
                Expect.isNull proj.Error "and recorded no error"
            }

            test "disposing the projection from its source leaves it empty" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let mutable proj = Unchecked.defaultof<Projection<int, int>>
                let created = ref 0

                proj <-
                    createProjectionWith
                        id
                        (fun item ->
                            created.Value <- created.Value + 1
                            fun () -> item () * 10)
                        (fun () ->
                            let xs = items.Value

                            if List.length xs = 3 then
                                proj.Dispose ()

                            xs)

                createEffect (fun () -> proj.Keys |> ignore)
                Expect.equal created.Value 2 "precondition: one factory run per key"

                items.Value <- [ 1; 2; 3 ]

                Expect.equal created.Value 2 "no factory ran under the disposed projection"
                Expect.equal (proj.TryGet 1) None "and it holds no rows"
            }

            test "a disposed projection's key set is empty and its readers see it empty" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let seen = ResizeArray<int>()

                createEffect (fun () -> seen.Add proj.Count)

                proj.Dispose ()

                Expect.sequenceEqual proj.Keys [] "Keys agrees with Get after disposal"
                Expect.sequenceEqual seen [ 2; 0 ] "a reader of the key set saw it empty"
            }

            test "disposing the enclosing root disposes the projection" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let cleaned = ResizeArray<int>()

                let root, proj =
                    createRoot (fun o ->
                        let proj =
                            createProjectionWith
                                id
                                (fun item ->
                                    let x = item ()
                                    onCleanup (fun () -> cleaned.Add x)
                                    fun () -> item () * 10)
                                (fun () -> items.Value)

                        o, proj)

                Expect.sequenceEqual proj.Keys [ 1; 2 ] "precondition: both keys live"

                root.Dispose ()

                Expect.sequenceEqual (Seq.sort cleaned) [ 1; 2 ] "every key's scope was disposed"
                Expect.equal (proj.TryGet 1) None "and the projection holds no rows"
            }

            test "a reader of Keys and every row sees the whole diff at once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, "a"); (2, "b") ]
                let proj = createProjection fst snd (fun () -> items.Value)
                let seen = ResizeArray<string list>()

                createEffect (fun () -> seen.Add [ for k in proj.Keys -> proj.Get k ])

                items.Value <- [ (3, "c"); (1, "A") ]

                Expect.sequenceEqual seen [ [ "a"; "b" ]; [ "c"; "A" ] ] "one run, over the applied diff"
            }

            test "a projection keyed by reference treats equal items as distinct keys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = ResizeArray [ 1 ]
                let b = ResizeArray [ 1 ]
                let items = createSignal [ a; b ]

                let proj =
                    createProjection id (fun (x: ResizeArray<int>) -> x.Count) (fun () -> items.Value)

                Expect.equal proj.Count 2 "two items, two keys"
                items.Value <- [ b ]
                Expect.equal proj.Count 1 "removing one leaves the other"
                Expect.isTrue (obj.ReferenceEquals (proj.Keys[0], b)) "the survivor is the item still in the source"
            }

            test "a row whose value comes out equal leaves its readers asleep" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, 10) ]
                let proj = createProjection fst (fun (_, v) -> v / 10) (fun () -> items.Value)
                let runs = ref 0

                createEffect (fun () ->
                    proj.Get 1 |> ignore
                    runs.Value <- runs.Value + 1)

                items.Value <- [ (1, 11) ]

                Expect.equal runs.Value 1 "the item changed but the row's value did not"
            }

            test "Get on a pending row names the source its reader awaits" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = createAsyncSource<string>()
                let proj = createProjection id (fun _ -> flight.Value) (fun () -> [ 1 ])

                let awaited =
                    try
                        proj.Get 1 |> ignore
                        None
                    with NotReadyException source ->
                        Some source

                Expect.isTrue
                    (awaited
                     |> Option.exists (fun s -> obj.ReferenceEquals (s, flight)))
                    "the exception names the async source"
            }

            test "a factory's nested pass that fails leaves the projection failed, then recovers in full" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let bad = createSignal true
                let created = ResizeArray<int>()
                let mutable proj = Unchecked.defaultof<Projection<int, int>>

                // Key 2's factory writes the source and reads `Keys`, which runs a
                // pass inside the outer pass's `CreateAdded`.
                proj <-
                    createProjectionWith
                        (fun x -> if bad.Value && x = 99 then failwith "keyOf failed" else x)
                        (fun item ->
                            let x = item ()
                            created.Add x

                            if x = 2 then
                                items.Value <- [ 1; 2; 3; 99 ]

                                try
                                    proj.Keys |> ignore
                                with _ ->
                                    ()

                            fun () -> item () * 10)
                        (fun () -> items.Value)

                let seen = ResizeArray<string>()

                createEffect (fun () ->
                    seen.Add (
                        try
                            sprintf "%A" [ for k in proj.Keys -> k, proj.Get k ]
                        with _ ->
                            "error"
                    ))

                items.Value <- [ 1; 2; 3 ]
                Expect.equal proj.Status Status.Error "the source holds the item keyOf rejects"

                bad.Value <- false
                Expect.equal proj.Status Status.None "the next pass succeeds"
                Expect.sequenceEqual proj.Keys [ 1; 2; 3; 99 ] "with every key"
                Expect.sequenceEqual [ for k in proj.Keys -> proj.Get k ] [ 10; 20; 30; 990 ] "and every row"
                Expect.sequenceEqual created [ 1; 2; 3; 99 ] "each factory ran once"

                Expect.sequenceEqual
                    seen
                    [ "[(1, 10)]"; "error"; "[(1, 10); (2, 20); (3, 30); (99, 990)]" ]
                    "the reader never saw a partial set of rows"
            }

            test "a record key matches its row by structure" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ { Row = 1; Column = 1 }; { Row = 1; Column = 2 } ]
                let created = ResizeArray<Cell>()

                let proj =
                    createProjectionWith
                        id
                        (fun item ->
                            created.Add (item ())
                            fun () -> (item ()).Column)
                        (fun () -> items.Value)

                Expect.equal (proj.Get { Row = 1; Column = 2 }) 2 "an equal record finds the row"

                items.Value <- [ { Row = 1; Column = 2 }; { Row = 2; Column = 1 } ]

                Expect.sequenceEqual proj.Keys [ { Row = 1; Column = 2 }; { Row = 2; Column = 1 } ] "the key set follows the source"

                Expect.sequenceEqual
                    created
                    [ { Row = 1; Column = 1 }; { Row = 1; Column = 2 }; { Row = 2; Column = 1 } ]
                    "a fresh but equal record keeps its row"

                Expect.equal (proj.TryGet { Row = 1; Column = 1 }) None "the removed key is absent"
            }

            test "None and Some keys share one projection" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ None; Some (1, 2); Some (3, 4) ]
                let created = ResizeArray<(int * int) option>()

                let proj =
                    createProjectionWith
                        id
                        (fun item ->
                            created.Add (item ())
                            fun () -> item () |> Option.map fst)
                        (fun () -> items.Value)

                Expect.equal (proj.Get None) None "the None row"
                Expect.equal (proj.Get (Some (3, 4))) (Some 3) "a Some row"

                items.Value <- [ Some (3, 4); None ]

                Expect.sequenceEqual proj.Keys [ Some (3, 4); None ] "the key set follows the source"
                Expect.equal (proj.TryGet (Some (1, 2))) None "the removed key is absent"

                items.Value <- [ Some (3, 4); None; Some (1, 2) ]

                Expect.equal proj.Count 3 "every key is live"

                Expect.sequenceEqual
                    created
                    [ None; Some (1, 2); Some (3, 4); Some (1, 2) ]
                    "survivors keep their rows and a returning key gets a new one"
            }

            test "a reader of a projection over another's Keys runs once per removing write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3 ]
                let up = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let stage = createProjection id (fun k -> up.Get k + 1) (fun () -> up.Keys)
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    for key in stage.Keys do
                        stage.Get key |> ignore)

                items.Value <- [ 1; 3 ]
                Expect.equal runs.Value 2 "one run for the removal"

                items.Value <- [ 3; 1; 4 ]
                Expect.equal runs.Value 3 "one run for a reorder and an addition"
                Expect.sequenceEqual stage.Keys [ 3; 1; 4 ] "the stage follows the upstream"
            }

            test "a memo reading a projection over another's Keys runs once per removing write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3 ]
                let up = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let stage = createProjection id (fun k -> up.Get k + 1) (fun () -> up.Keys)
                let memoRuns = ref 0

                let total =
                    createMemo (fun () ->
                        memoRuns.Value <- memoRuns.Value + 1
                        stage.Keys |> Array.sumBy stage.Get)

                createEffect (fun () -> total.Value |> ignore)
                items.Value <- [ 1; 3 ]

                Expect.equal memoRuns.Value 2 "one memo run for the removal"
                Expect.equal total.Value 42 "the memo reads the surviving rows"
            }

            test "a reader of an index projection over another's Keys runs once per removing write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3 ]
                let up = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let stage = createIndexProjection up.Get (fun () -> up.Keys)
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    for index in stage.Keys do
                        stage.Get index |> ignore)

                items.Value <- [ 1; 3 ]
                Expect.equal runs.Value 2 "one run for the removal"
            }

            test "a reader of a projection over another's Keys runs once per nested batch" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2; 3; 4 ]
                let up = createProjection id (fun x -> x * 10) (fun () -> items.Value)
                let stage = createProjection id (fun k -> up.Get k + 1) (fun () -> up.Keys)
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    for key in stage.Keys do
                        stage.Get key |> ignore)

                batch (fun () ->
                    batch (fun () -> items.Value <- [ 1; 3; 4 ])
                    items.Value <- [ 1; 4 ])

                Expect.equal runs.Value 2 "one run for both removals"
                Expect.sequenceEqual stage.Keys [ 1; 4 ] "the stage follows the upstream"
            }

            test "a failed projection stays idle while its reader wakes for an unrelated memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let other = createSignal 0
                let calls = ref 0

                let proj =
                    createProjection id id (fun () ->
                        calls.Value <- calls.Value + 1
                        let keys = items.Value

                        if keys.Length = 1 then
                            invalidOp "boom"

                        keys)

                let parity = createMemo (fun () -> other.Value % 2)

                createEffect (fun () ->
                    parity.Value |> ignore

                    try
                        proj.Keys |> ignore
                    with _ ->
                        ())

                items.Value <- [ 1 ]
                Expect.equal proj.Status Status.Error "the pass failed"
                let before = calls.Value, proj.Runs

                for i in 1..5 do
                    other.Value <- 2 * i

                Expect.equal (calls.Value, proj.Runs) before "no pass for a wake that cuts off"
            }

            test "a pending projection stays idle while its reader wakes for an unrelated memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let gate = createAsyncSource<int list>()
                let other = createSignal 0
                let calls = ref 0

                let proj =
                    createProjection id id (fun () ->
                        calls.Value <- calls.Value + 1
                        gate.Value)

                let parity = createMemo (fun () -> other.Value % 2)

                createEffect (fun () ->
                    parity.Value |> ignore

                    try
                        proj.Keys |> ignore
                    with _ ->
                        ())

                let before = calls.Value

                for i in 1..5 do
                    other.Value <- 2 * i

                Expect.equal calls.Value before "no pass for a wake that cuts off"
                gate.Settle [ 1; 2 ]
                Expect.sequenceEqual proj.Keys [ 1; 2 ] "the settle runs the pass"
            }
        ]

/// <summary>A row in flight, read once so it enters the pending summary unless <c>preRead</c> is false.</summary>
let private pendingRow (preRead: bool) =
    let items = createSignal [ 1 ]
    let flight = createAsyncSource<int>()
    let proj = createProjection id (fun _ -> flight.Value) (fun () -> items.Value)

    if preRead then
        Expect.throwsT<NotReadyException> (fun () -> proj.Get 1 |> ignore) "precondition: the row is in flight"

    flight, proj

let private rowText (proj: Projection<int, int>) =
    try
        string (proj.Get 1)
    with :? NotReadyException ->
        "pending"

/// <summary>The values <c>read</c> returns in the effect runs that one settle of the row adds.</summary>
let private runsOnSettle (preRead: bool) (read: Projection<int, int> -> string) =
    use g = new Graph ()
    use _ = g.Activate ()
    let flight, proj = pendingRow preRead
    let seen = ResizeArray<string>()
    createEffect (fun () -> seen.Add (read proj))
    let before = seen.Count
    flight.Settle 5
    List.ofSeq seen |> List.skip before

/// <summary>Counts the runs of an effect reading <c>read</c> over a projection whose pass awaits its source.</summary>
let private runsOnPassSettle (read: Projection<int, int> -> unit) (chain: Projection<int, int> -> Projection<int, int>) =
    use g = new Graph ()
    use _ = g.Activate ()
    let source = createAsyncSource<int list>()

    let view =
        createProjection id id (fun () -> source.Value)
        |> chain

    let runs = ref 0

    createEffect (fun () ->
        runs.Value <- runs.Value + 1

        try
            read view
        with :? NotReadyException ->
            ())

    let before = runs.Value
    source.Settle [ 1; 2 ]
    runs.Value - before

[<Tests>]
let summaryTests =
    testList
        "Projection summary"
        [
            for preRead in [ true; false ] do
                test $"a reader of AnyPending and then the row runs once per settle and sees both settled (read first: {preRead})" {
                    let runs =
                        runsOnSettle preRead (fun proj -> sprintf "any=%b %s" proj.AnyPending (rowText proj))

                    Expect.equal runs [ "any=false 5" ] "one run, consistent"
                }

                test $"a reader of PendingKeys and then the row runs once per settle and sees both settled (read first: {preRead})" {
                    let runs =
                        runsOnSettle preRead (fun proj -> sprintf "pk=%A %s" (List.ofArray proj.PendingKeys) (rowText proj))

                    Expect.equal runs [ "pk=[] 5" ] "one run, consistent"
                }

                test $"a reader of the row and then AnyPending runs once per settle (read first: {preRead})" {
                    let runs =
                        runsOnSettle preRead (fun proj -> let v = rowText proj in sprintf "%s any=%b" v proj.AnyPending)

                    Expect.equal runs [ "5 any=false" ] "one run"
                }

                test $"a reader of the row and then PendingKeys runs once per settle (read first: {preRead})" {
                    let runs =
                        runsOnSettle preRead (fun proj -> let v = rowText proj in sprintf "%s pk=%A" v (List.ofArray proj.PendingKeys))

                    Expect.equal runs [ "5 pk=[]" ] "one run"
                }

            test "a reader of AnyPending sees a row that goes pending during its own run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight, proj = pendingRow false
                let seen = ResizeArray<string>()
                createEffect (fun () -> seen.Add (sprintf "any=%b %s" proj.AnyPending (rowText proj)))
                Expect.equal (List.ofSeq seen) [ "any=false pending"; "any=true pending" ] "the second run sees the row counted"
                flight.Settle 5
                Expect.equal (List.ofSeq seen |> List.last) "any=false 5" "and the settle clears it"
            }

            test "a run that reads a row between two reads of AnyPending sees the row counted only in the second" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight, proj = pendingRow false
                let seen = ResizeArray<string>()

                createEffect (fun () ->
                    let before = proj.AnyPending
                    let row = rowText proj
                    seen.Add (sprintf "any=%b %s any=%b" before row proj.AnyPending))

                Expect.equal (List.ofSeq seen) [ "any=false pending any=true" ] "one run, with both values"
                flight.Settle 5
                Expect.equal (List.ofSeq seen |> List.last) "any=false 5 any=false" "and one run per settle"
                Expect.equal seen.Count 2 "no run for the row going pending"
            }

            test "a memo over AnyPending read inside a batch sees a row settle that no flush has refreshed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight, proj = pendingRow true
                let any = createMemo (fun () -> proj.AnyPending)
                Expect.isTrue any.Value "precondition: the row is in flight"

                let inside =
                    batch (fun () ->
                        flight.Settle 5
                        any.Value)

                Expect.isFalse inside "the read brings the row current"
            }

            test "a memo over PendingKeys read inside a batch after a Keys read sees a pending key removed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let flight = createAsyncSource<int>()

                let proj =
                    createProjection id (fun n -> if n = 1 then flight.Value else n) (fun () -> items.Value)

                let pending = createMemo (fun () -> List.ofArray proj.PendingKeys)
                Expect.throwsT<NotReadyException> (fun () -> proj.Get 1 |> ignore) "precondition: the row is in flight"
                Expect.equal pending.Value [ 1 ] "precondition: the summary counts it"

                let inside =
                    batch (fun () ->
                        items.Value <- [ 2 ]
                        proj.Keys |> ignore
                        pending.Value)

                Expect.equal inside [] "the removed key leaves the summary"
            }

            let keys (view: Projection<int, int>) =
                view.Keys |> ignore

            let keysAndRows (view: Projection<int, int>) =
                for key in view.Keys do
                    view.Get key |> ignore

            let snapshot (view: Projection<int, int>) =
                view.Snapshot |> ignore

            for name, read, chain in
                [
                    "Keys", keys, id
                    "Keys and Get", keysAndRows, id
                    "Snapshot", snapshot, id
                    "Keys and Get of filter then map",
                    keysAndRows,
                    (Projection.filter (fun n -> n > 0)
                     >> Projection.map (fun n -> n * 3))
                    "Keys and Get of sortBy", keysAndRows, Projection.sortBy (fun n -> -n)
                ] do
                test $"a reader of {name} over a pending pass runs once when the pass settles" {
                    Expect.equal (runsOnPassSettle read chain) 1 "one run"
                }

            test "AsObservableCollection over a pending pass resets once when the pass settles" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int list>()
                let proj = createProjection id id (fun () -> source.Value)
                let view = proj.AsObservableCollection ()
                let resets = ref 0

                view.CollectionChanged.Add (fun e ->
                    if e.Action = Collections.Specialized.NotifyCollectionChangedAction.Reset then
                        resets.Value <- resets.Value + 1)

                source.Settle [ 1; 2 ]
                Expect.sequenceEqual view [ 1; 2 ] "the view follows"
                Expect.equal resets.Value 1 "one reset"
            }
        ]
