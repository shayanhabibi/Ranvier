module Ranvier.Tests.MapSemantics

open System
open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Ranvier

type private User = { Id: int; Name: string }

let private users =
    [ { Id = 1; Name = "ada" }; { Id = 2; Name = "bob" }; { Id = 3; Name = "cy" } ]

/// <summary>
/// Counts runs of an effect reading <c>read</c>, swallowing suspension and failure.
/// </summary>
let private observe (read: unit -> 'a) =
    let runs = ref 0

    createEffect (fun () ->
        runs.Value <- runs.Value + 1

        try
            read () |> ignore
        with _ ->
            ())

    runs

let private bump (d: Dictionary<int, int>) k =
    d[k] <-
        (match d.TryGetValue k with
         | true, n -> n
         | _ -> 0)
        + 1

/// <summary>
/// Reads a row expected to be pending, so it enters the pending summary.
/// </summary>
let private readPending (proj: Projection<'K, 'V>) (key: 'K) =
    Expect.throwsT<NotReadyException> (fun () -> proj.Get key |> ignore) $"precondition: row %A{key} is pending"

/// <summary>
/// Scenarios S1 to S9 are ported from the bake-off's <c>proto/accessor-factory</c>
/// branch. S1 and S6 read their rows before asserting on the pending summary,
/// because rows are lazy: an unread row is absent from <c>PendingKeys</c>.
/// </summary>
[<Tests>]
let tests =
    testList
        "MapSemantics"
        [
            test "S1 a per-row fetch settles, and settling one row leaves the others' nodes alone" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let flights = Dictionary<int, TaskCompletionSource<string>>()

                for u in users do
                    flights[u.Id] <- TaskCompletionSource<string>()

                let created = Dictionary<int, int>()

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let id = (item ()).Id
                            bump created id
                            let fetch = createAsync (fun _ _ -> flights[id].Task)
                            fun () -> fetch.Value)
                        (fun () -> source.Value)

                let views =
                    [
                        for u in users -> u.Id, createSuspense (fun _ -> "loading") (fun () -> proj.Get u.Id)
                    ]
                    |> dict

                let log = ResizeArray<string>()
                createEffect (fun () -> log.Add views[1].Value)

                Expect.throwsT<NotReadyException> (fun () -> proj.Get 1 |> ignore) "row 1 is pending"
                Expect.equal views[1].Value "loading" "row 1 shows the fallback"

                flights[1].SetResult "one"
                Expect.equal (proj.Get 1) "one" "row 1 settled"
                Expect.throwsT<NotReadyException> (fun () -> proj.Get 2 |> ignore) "row 2 still pending"
                Expect.equal views[2].Value "loading" "row 2 still shows the fallback"

                flights[2].SetResult "two"
                Expect.equal (proj.Get 2) "two" "row 2 settled"
                Expect.equal (proj.Get 1) "one" "row 1 still settled"
                Expect.equal created[1] 1 "row 1's node was created once"
                Expect.equal created[2] 1 "row 2's node was created once"
                Expect.sequenceEqual log [ "loading"; "one" ] "row 1's view: fallback, then value"

                // Adapted: row 3 is read so that it computes.
                readPending proj 3
                Expect.sequenceEqual proj.PendingKeys [ 3 ] "row 3 alone is pending"
            }

            test "S2 an item changed under its key reaches the row without re-creating it" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let mutable created = 0
                let mutable cleaned = 0

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            created <- created + 1
                            onCleanup (fun () -> cleaned <- cleaned + 1)
                            let upper = createMemo (fun _ -> (item ()).Name.ToUpper())
                            fun () -> upper.Value)
                        (fun () -> source.Value)

                let row1 = observe (fun () -> proj.Get 1)
                Expect.equal (proj.Get 1) "ADA" "initial"
                let createdBefore = created

                source.Value <-
                    source.Value
                    |> List.map (fun u -> if u.Id = 1 then { u with Name = "adele" } else u)

                Expect.equal (proj.Get 1) "ADELE" "the row shows the new content"
                Expect.equal row1.Value 2 "the row observer woke once"
                Expect.equal created createdBefore "no row was re-created"
                Expect.equal cleaned 0 "no cleanup ran"
            }

            test "S3 a reorder wakes Keys alone" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let mutable created = 0

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            created <- created + 1
                            fun () -> (item ()).Name)
                        (fun () -> source.Value)

                let keysRuns = observe (fun () -> proj.Keys)
                let rows = [ for u in users -> observe (fun () -> proj.Get u.Id) ]

                source.Value <- List.rev source.Value

                Expect.sequenceEqual proj.Keys [ 3; 2; 1 ] "reordered"
                Expect.equal keysRuns.Value 2 "Keys woke"

                for r in rows do
                    Expect.equal r.Value 1 "no row woke"

                Expect.equal created 3 "no row was re-created"
            }

            test "S4 editing one row wakes that row alone" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let proj = createProjection _.Id _.Name (fun () -> source.Value)
                let keysRuns = observe (fun () -> proj.Keys)
                let row1 = observe (fun () -> proj.Get 1)
                let row2 = observe (fun () -> proj.Get 2)
                let row3 = observe (fun () -> proj.Get 3)

                source.Value <-
                    source.Value
                    |> List.map (fun u -> if u.Id = 2 then { u with Name = "bo" } else u)

                Expect.equal (proj.Get 2) "bo" "edited"
                Expect.equal row2.Value 2 "the edited row woke"
                Expect.equal row1.Value 1 "row 1 did not"
                Expect.equal row3.Value 1 "row 3 did not"
                Expect.equal keysRuns.Value 1 "Keys did not"
            }

            test "S5 a removed key's scope is disposed once, and re-adding it builds a fresh row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let created = Dictionary<int, int>()
                let cleaned = Dictionary<int, int>()

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let id = (item ()).Id
                            bump created id
                            onCleanup (fun () -> bump cleaned id)
                            fun () -> (item ()).Name)
                        (fun () -> source.Value)

                observe (fun () -> proj.Keys) |> ignore
                observe (fun () -> proj.Get 2) |> ignore

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 2)
                Expect.equal cleaned[2] 1 "removed key disposed once"
                Expect.isNone (proj.TryGet 2) "absent"

                source.Value <- source.Value @ [ { Id = 2; Name = "bea" } ]
                Expect.equal cleaned[2] 1 "still disposed once"
                Expect.equal created[2] 2 "a fresh row"
                Expect.equal (proj.Get 2) "bea" "with the new item"
                Expect.isFalse (cleaned.ContainsKey 1) "row 1 untouched"
            }

            test "S6 the pending channel reports per-row async state" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let gates = Dictionary<int, AsyncSource<string>>()

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let gate = createAsyncSource<string>()
                            gates[(item ()).Id] <- gate
                            fun () -> gate.Value)
                        (fun () -> source.Value)

                // Adapted: every row is read before the summary is observed.
                for u in users do
                    readPending proj u.Id

                let anyLog = ResizeArray<bool>()
                createEffect (fun () -> anyLog.Add proj.AnyPending)
                let view = createSuspense (fun _ -> "loading") (fun () -> proj.Get 1)
                let viewLog = ResizeArray<string>()
                createEffect (fun () -> viewLog.Add view.Value)

                Expect.throwsT<NotReadyException> (fun () -> proj.Get 1 |> ignore) "pending row raises"
                Expect.sequenceEqual proj.PendingKeys [ 1; 2; 3 ] "all pending"

                gates[1].Settle "one"
                Expect.sequenceEqual proj.PendingKeys [ 2; 3 ] "1 settled"
                gates[2].Settle "two"
                gates[3].Settle "three"
                Expect.sequenceEqual proj.PendingKeys [] "none pending"
                Expect.sequenceEqual anyLog [ true; false ] "AnyPending true then false"
                Expect.sequenceEqual viewLog [ "loading"; "one" ] "Suspense: fallback then value"
            }

            test "S7 a projection nobody reads never runs the factory" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let mutable created = 0

                let _proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            created <- created + 1
                            fun () -> (item ()).Name)
                        (fun () -> source.Value)

                source.Value <- List.rev users
                source.Value <- users
                Expect.equal created 0 "never ran"
            }

            test "S8 an index projection updates a slot without re-creating its row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ "a"; "b" ]
                let mutable created = 0

                let proj =
                    createIndexProjectionWith
                        (fun item ->
                            created <- created + 1
                            fun () -> (item (): string).ToUpper())
                        (fun () -> source.Value)

                let slot0 = observe (fun () -> proj.Get 0)
                source.Value <- [ "z"; "b" ]

                Expect.equal (proj.Get 0) "Z" "slot updated"
                Expect.equal slot0.Value 2 "slot woke once"
                Expect.equal created 2 "no row re-created"
            }

            test "S9 the plain value form is a one-liner" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let proj = createProjection _.Id (fun u -> u.Name) (fun () -> source.Value)
                Expect.equal (proj.Get 3) "cy" "reads"
            }

            test "a value-form map that creates a memo throws on the row's first read" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjection _.Id (fun u -> (createMemo (fun _ -> u.Name)).Value) (fun () -> source.Value)

                let ex = Expect.throwsC (fun () -> proj.Get 1 |> ignore) id

                Expect.isTrue (ex :? InvalidOperationException) "an InvalidOperationException"
                Expect.stringContains ex.Message "createProjectionWith" "naming the factory form"
                Expect.stringContains ex.Message "factory" "and saying where the node belongs"
            }

            test "a value-form map that creates an async node throws rather than staying pending" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let flight = TaskCompletionSource<string>()

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            (createAsync (fun _ _ -> flight.Task)).Value
                            + u.Name)
                        (fun () -> source.Value)

                Expect.throwsT<InvalidOperationException> (fun () -> proj.Get 1 |> ignore) "the spec's example throws on the first read"
                Expect.isFalse proj.AnyPending "the row failed; it is not pending"
            }

            test "a map that creates an effect throws, and the effect never runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let tick = createSignal 0
                let effectRuns = ref 0

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            createEffect (fun () ->
                                tick.Value |> ignore
                                effectRuns.Value <- effectRuns.Value + 1)

                            u.Name)
                        (fun () -> source.Value)

                Expect.throwsT<InvalidOperationException> (fun () -> proj.Get 1 |> ignore) "the run throws"
                let before = effectRuns.Value

                tick.Value <- 1

                Expect.equal effectRuns.Value before "the effect the run created no longer runs"
            }

            test "a map that catches the creation's exception still throws" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            try
                                (createMemo (fun _ -> u.Name)).Value
                            with _ ->
                                u.Name)
                        (fun () -> source.Value)

                Expect.throwsT<InvalidOperationException> (fun () -> proj.Get 1 |> ignore) "the row fails"
            }

            test "a row that catches the creation's exception on its first run is absent from Snapshot" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let catching (u: User) =
                    if u.Id = 1 then
                        try
                            (createMemo (fun _ -> u.Name)).Value
                        with _ ->
                            u.Name
                    else
                        u.Name

                let valueForm = createProjection _.Id catching (fun () -> source.Value)

                let factoryForm =
                    createProjectionWith _.Id (fun item -> fun () -> catching (item ())) (fun () -> source.Value)

                for proj in [ valueForm; factoryForm ] do
                    Expect.throwsT<InvalidOperationException> (fun () -> proj.Get 1 |> ignore) "the row fails"

                    Expect.equal
                        (proj.Snapshot
                         |> Seq.map (fun p -> p.Key, p.Value)
                         |> List.ofSeq)
                        [ 2, "bob"; 3, "cy" ]
                        "the never-settled row is absent"

                    Expect.sequenceEqual (proj.AsObservableCollection ()) [ "bob"; "cy" ] "and absent from the collection"
            }

            test "a settled row that then catches the creation's exception keeps its last value in Snapshot" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            if u.Name = "x" then
                                try
                                    (createMemo (fun _ -> u.Name)).Value
                                with _ ->
                                    u.Name
                            else
                                u.Name)
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada" "precondition: the row settled"
                source.Value <- { Id = 1; Name = "x" } :: List.tail users
                Expect.throwsT<InvalidOperationException> (fun () -> proj.Get 1 |> ignore) "the row fails"
                Expect.equal proj.Snapshot[1] "ada" "the row keeps its last settled value"
            }

            test "a value-form map that calls onCleanup throws, and the cleanup is not registered" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let mutable cleaned = 0

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            onCleanup (fun () -> cleaned <- cleaned + 1)
                            u.Name)
                        (fun () -> source.Value)

                let ex = Expect.throwsC (fun () -> proj.Get 1 |> ignore) id
                Expect.isTrue (ex :? InvalidOperationException) "an InvalidOperationException"
                Expect.stringContains ex.Message "createProjectionWith" "naming the factory form"

                source.Value <- []
                Expect.sequenceEqual proj.Keys [] "precondition: the key is removed"
                Expect.equal cleaned 0 "no cleanup ran"
            }

            test "a factory-form reader that creates a node throws, telling the caller to move it into the factory" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjectionWith _.Id (fun item -> fun () -> (createMemo (fun _ -> (item ()).Name)).Value) (fun () -> source.Value)

                let ex = Expect.throwsC (fun () -> proj.Get 1 |> ignore) id

                Expect.isTrue (ex :? InvalidOperationException) "an InvalidOperationException"
                Expect.stringContains ex.Message "factory body" "saying to move the creation into the factory body"
            }

            test "a row read that first computes an outside memo leaves the memo's nodes alone" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 10

                let ext =
                    createMemoWith (fun _ ->
                        let inner = createMemo (fun _ -> s.Value * 2)
                        inner.Value)

                let proj =
                    createProjection _.Id (fun (u: User) -> $"%s{u.Name}%d{ext.Value}") (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "the outside memo's nested memo is not blamed on the row"

                s.Value <- 11
                Expect.equal ext.Value 22 "the outside memo still follows its nested memo"
                Expect.equal (proj.Get 1) "ada22" "and the row follows the outside memo"
            }

            test "a factory memo whose body creates a memo computes inside the row's reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let m =
                                createMemoWith (fun _ ->
                                    let inner = createMemo (fun _ -> (item ()).Name)
                                    inner.Value)

                            fun () -> m.Value)
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada" "the factory memo's nested memo is not blamed on the reader"

                source.Value <-
                    source.Value
                    |> List.map (fun u -> if u.Id = 1 then { u with Name = "adele" } else u)

                Expect.equal (proj.Get 1) "adele" "the row follows the item"
            }

            test "onCleanup inside a memo a row pulls registers without throwing" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let mutable cleaned = 0

                let ext =
                    createMemoWith (fun _ ->
                        onCleanup (fun () -> cleaned <- cleaned + 1)
                        1)

                let proj =
                    createProjection _.Id (fun (u: User) -> u.Id + ext.Value) (fun () -> source.Value)

                Expect.equal (proj.Get 1) 2 "the row reads the memo"
                Expect.equal cleaned 0 "the cleanup is registered, not run"
                proj.Dispose ()
                Expect.equal cleaned 0 "the cleanup belongs to the memo, not the projection"
                ext.Dispose ()
                Expect.equal cleaned 1 "the cleanup runs with the memo"
            }

            test "removing a key leaves an outside memo that the key's row first computed following its sources" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 10

                let ext =
                    createMemoWith (fun _ ->
                        let inner = createMemo (fun _ -> s.Value * 2)
                        inner.Value)

                let proj =
                    createProjectionWith _.Id (fun item -> fun () -> $"%s{(item ()).Name}%d{ext.Value}") (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "row 1 computes the outside memo first"
                Expect.equal (proj.Get 2) "bob20" "row 2 reads the same memo"

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 1)
                Expect.sequenceEqual proj.Keys [ 2; 3 ] "precondition: key 1 is removed"

                s.Value <- 11
                Expect.equal ext.Value 22 "the outside memo still follows its nested memo"
                Expect.equal (proj.Get 2) "bob22" "and the surviving row follows the outside memo"
            }

            test "removing a key leaves an outside memo that the key's factory first computed following its sources" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 10

                let ext =
                    createMemoWith (fun _ ->
                        let inner = createMemo (fun _ -> s.Value * 2)
                        inner.Value)

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let seed = ext.Value
                            fun () -> $"%s{(item ()).Name}%d{seed}")
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "the factory for key 1 computes the outside memo first"

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 1)
                Expect.sequenceEqual proj.Keys [ 2; 3 ] "precondition: key 1 is removed"

                s.Value <- 11
                Expect.equal ext.Value 22 "the outside memo still follows its nested memo"
            }

            test "a node created by a factory memo's body is disposed with the key" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let cleaned = ResizeArray<int>()

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let m =
                                createMemoWith (fun _ ->
                                    let id = (item ()).Id
                                    onCleanup (fun () -> cleaned.Add id)
                                    id)

                            fun () -> m.Value)
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) 1 "the factory memo computes inside the row's reader"
                Expect.equal (proj.Get 2) 2 "and in the second row"

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 1)
                Expect.sequenceEqual proj.Keys [ 2; 3 ] "precondition: key 1 is removed"
                Expect.sequenceEqual cleaned [ 1 ] "the key's cleanup runs on removal, the other key's does not"
            }

            test "a map that creates a memo inside untrack still throws" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjection _.Id (fun (u: User) -> (untrack (fun () -> createMemo (fun _ -> u.Name))).Value) (fun () -> source.Value)

                Expect.throwsT<InvalidOperationException> (fun () -> proj.Get 1 |> ignore) "untrack does not hide a creation"
            }

            test "a reader may create unowned sources" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjection _.Id (fun (u: User) -> (createSignal u.Name).Value) (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada" "createSignal in a reader does not throw"
            }

            test "a row computes when read, and an unread row never runs its reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let reads = Dictionary<int, int>()

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            bump reads u.Id
                            u.Name)
                        (fun () -> source.Value)

                observe (fun () -> proj.Keys) |> ignore
                Expect.equal reads.Count 0 "reading Keys runs no reader"

                Expect.equal (proj.Get 2) "bob" "a read computes the row"
                Expect.equal reads.Count 1 "one row computed"
                Expect.equal reads[2] 1 "once"

                source.Value <-
                    source.Value
                    |> List.map (fun u -> { u with Name = u.Name + "!" })

                Expect.equal reads.Count 1 "a changed item does not compute an unread row"
                Expect.equal reads[2] 1 "nor re-run an unobserved one"

                Expect.equal (proj.Get 2) "bob!" "the next read is current"
                Expect.equal reads[2] 2 "having run once more"
            }

            test "an unchanged survivor is skipped" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let reads = Dictionary<int, int>()

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            bump reads u.Id
                            u.Name)
                        (fun () -> source.Value)

                let rows = [ for u in users -> observe (fun () -> proj.Get u.Id) ]

                // The same item instances, reordered and extended: every
                // survivor's item compares equal to its previous item.
                source.Value <- List.rev users @ [ { Id = 4; Name = "dee" } ]

                Expect.sequenceEqual proj.Keys [ 3; 2; 1; 4 ] "precondition: the pass ran"

                for u in users do
                    Expect.equal reads[u.Id] 1 $"row %d{u.Id}'s reader did not re-run"

                for r in rows do
                    Expect.equal r.Value 1 "and no row reader woke"
            }

            test "a pending row stays scheduled until it settles, then is lazy again" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1 ]
                let gate = createAsyncSource<int>()
                let offset = createSignal 0
                let runs = ref 0

                let proj =
                    createProjection
                        id
                        (fun x ->
                            runs.Value <- runs.Value + 1
                            gate.Value + x + offset.Value)
                        (fun () -> source.Value)

                readPending proj 1
                Expect.equal runs.Value 1 "precondition: one run"

                gate.Settle 10

                Expect.equal runs.Value 2 "the settle re-ran the row with no reader"
                Expect.isFalse proj.AnyPending "and the summary saw it"
                Expect.equal proj.Snapshot[1] 11 "with the settled value"

                offset.Value <- 5
                Expect.equal runs.Value 2 "a settled row with no reader is lazy"
                Expect.equal (proj.Get 1) 16 "until read"
                Expect.equal runs.Value 3 "once"
            }

            test "a row fails alone, and recovers when a dependency changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let broken = createSignal true

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            if u.Id = 2 && broken.Value then
                                failwith "row 2 failed"
                            else
                                u.Name)
                        (fun () -> source.Value)

                let seen = ResizeArray<string>()

                createEffect (fun () ->
                    seen.Add (
                        try
                            proj.Get 2
                        with e ->
                            e.Message
                    ))

                Expect.equal (proj.Get 1) "ada" "row 1 is unaffected"
                Expect.throwsT<Exception> (fun () -> proj.Get 2 |> ignore) "Get re-raises the row's error"
                Expect.throwsT<Exception> (fun () -> proj.TryGet 2 |> ignore) "and so does TryGet"
                Expect.equal proj.Status Status.None "the pass succeeded"
                Expect.isNull proj.Error "and recorded no error"
                Expect.isFalse proj.AnyPending "a failed row is not pending"
                Expect.isFalse (proj.Snapshot.ContainsKey 2) "a row that never settled is absent from the snapshot"

                broken.Value <- false

                Expect.equal (proj.Get 2) "bob" "the row recomputed"
                Expect.sequenceEqual seen [ "row 2 failed"; "bob" ] "and its reader woke"

                broken.Value <- true
                Expect.equal proj.Snapshot[2] "bob" "a failed row keeps its last settled value in the snapshot"
            }

            test "a factory that throws fails its row until the key is removed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1; 2 ]
                let factories = Dictionary<int, int>()

                let proj =
                    createProjectionWith
                        id
                        (fun item ->
                            let x = item ()
                            bump factories x

                            if x = 2 && factories[x] = 1 then
                                failwith "factory failed"

                            fun () -> item () * 10)
                        (fun () -> source.Value)

                observe (fun () -> proj.Keys) |> ignore

                Expect.equal (proj.Get 1) 10 "row 1 is unaffected"
                Expect.throwsT<Exception> (fun () -> proj.Get 2 |> ignore) "row 2 fails"
                Expect.throwsT<Exception> (fun () -> proj.Get 2 |> ignore) "and stays failed"
                Expect.equal factories[2] 1 "the factory is not retried"

                source.Value <- [ 1 ]
                source.Value <- [ 1; 2 ]

                Expect.equal (proj.Get 2) 20 "a re-added key runs its factory again"
                Expect.equal factories[2] 2 "once"
            }

            test "a factory that reads a pending source fails its row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1 ]
                let gate = createAsyncSource<int>()

                let proj =
                    createProjectionWith
                        id
                        (fun item ->
                            let offset = gate.Value
                            fun () -> item () + offset)
                        (fun () -> source.Value)

                let ex = Expect.throwsC (fun () -> proj.Get 1 |> ignore) id

                Expect.isTrue (ex :? InvalidOperationException) "an InvalidOperationException, not a suspension"
                Expect.isTrue (ex.InnerException :? NotReadyException) "wrapping the suspension"
            }

            test "Snapshot computes every row it reports" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let reads = ref 0

                let proj =
                    createProjection
                        _.Id
                        (fun (u: User) ->
                            reads.Value <- reads.Value + 1
                            u.Name)
                        (fun () -> source.Value)

                Expect.sequenceEqual (proj.Snapshot |> Seq.map _.Value) [ "ada"; "bob"; "cy" ] "every row, in key order"
                Expect.equal reads.Value 3 "each computed once"
            }

            test "M1 an outside memo that creates its memo under untrack, first read by a factory body, survives key removal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (List.take 2 users)
                let s = createSignal 10

                let ext =
                    createMemoWith (fun _ ->
                        let inner = untrack (fun () -> createMemo (fun _ -> s.Value * 2))
                        inner.Value)

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            ext.Value |> ignore
                            fun () -> $"%s{(item ()).Name}%d{ext.Value}")
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "key 1's factory computes the outside memo first"
                Expect.equal (proj.Get 2) "bob20" "row 2 reads the same memo"

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 1)
                Expect.sequenceEqual proj.Keys [ 2 ] "precondition: key 1 is removed"

                s.Value <- 11
                Expect.equal ext.Value 22 "the outside memo follows its nested memo"
                Expect.equal (proj.Get 2) "bob22" "and row 2 follows the outside memo"
            }

            test "M2 a cleanup an outside memo registers under untrack, first read by a factory body, survives key removal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal (List.take 2 users)
                let s = createSignal 10
                let mutable cleaned = 0

                let ext =
                    createMemoWith (fun _ ->
                        untrack (fun () -> onCleanup (fun () -> cleaned <- cleaned + 1))
                        s.Value)

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            ext.Value |> ignore
                            fun () -> (item ()).Name)
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada" "key 1's factory computes the outside memo first"
                proj.Get 2 |> ignore

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 1)
                Expect.sequenceEqual proj.Keys [ 2 ] "precondition: key 1 is removed"
                Expect.equal cleaned 0 "the memo's cleanup survives the key"

                proj.Dispose ()
                Expect.equal cleaned 0 "and the projection"

                s.Value <- 11
                Expect.equal ext.Value 11 "precondition: the memo re-runs"
                Expect.equal cleaned 1 "its cleanup runs before the re-run"
            }

            test "M3 an outside memo lazily pulled by a row reader keeps its nested nodes after the projection is disposed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 10

                let ext =
                    createMemoWith (fun _ ->
                        let inner = createMemo (fun _ -> s.Value * 2)
                        inner.Value)

                let proj =
                    createProjection _.Id (fun (u: User) -> $"%s{u.Name}%d{ext.Value}") (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "the row computes the outside memo first"
                proj.Dispose ()

                s.Value <- 11
                Expect.equal ext.Value 22 "the outside memo follows its nested memo"
            }

            test "M4 an outside memo first computed by a factory-form reader keeps its nested nodes after key removal and projection disposal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 10
                let mutable cleaned = 0

                let ext =
                    createMemoWith (fun _ ->
                        let inner = createMemo (fun _ -> s.Value * 2)
                        onCleanup (fun () -> cleaned <- cleaned + 1)
                        inner.Value)

                let proj =
                    createProjectionWith _.Id (fun item -> fun () -> $"%s{(item ()).Name}%d{ext.Value}") (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "row 1 computes the outside memo first"

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 1)
                Expect.sequenceEqual proj.Keys [ 2; 3 ] "precondition: key 1 is removed"
                Expect.equal cleaned 0 "key removal leaves the memo's cleanup"

                proj.Dispose ()
                Expect.equal cleaned 0 "projection disposal leaves the memo's cleanup"

                s.Value <- 11
                Expect.equal ext.Value 22 "the outside memo follows its nested memo"
            }

            test "M5 an outside memo first computed by a factory body keeps its nested nodes after projection disposal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 10

                let ext =
                    createMemoWith (fun _ ->
                        let inner = createMemo (fun _ -> s.Value * 2)
                        inner.Value)

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let seed = ext.Value
                            fun () -> $"%s{(item ()).Name}%d{seed}")
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "the factory computes the outside memo first"
                proj.Dispose ()

                s.Value <- 11
                Expect.equal ext.Value 22 "the outside memo follows its nested memo"
            }

            test "M6 a value-form reader that creates a memo throws naming createProjectionWith, on every run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users

                let proj =
                    createProjection _.Id (fun (u: User) -> (createMemo (fun _ -> u.Name)).Value) (fun () -> source.Value)

                let first = Expect.throwsC (fun () -> proj.Get 1 |> ignore) id
                Expect.isTrue (first :? InvalidOperationException) "an InvalidOperationException"
                Expect.stringContains first.Message "createProjectionWith" "naming the factory form"

                source.Value <-
                    source.Value
                    |> List.map (fun u -> if u.Id = 1 then { u with Name = "adele" } else u)

                let again = Expect.throwsC (fun () -> proj.Get 1 |> ignore) id
                Expect.stringContains again.Message "createProjectionWith" "the re-run throws as well"
            }

            test "M8 an outside memo that creates its memo under untrack, first read by a row reader, is not blamed on the reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 10

                let ext =
                    createMemoWith (fun _ ->
                        let inner = untrack (fun () -> createMemo (fun _ -> s.Value * 2))
                        inner.Value)

                let proj =
                    createProjection _.Id (fun (u: User) -> $"%s{u.Name}%d{ext.Value}") (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada20" "the row computes the outside memo first"

                s.Value <- 11
                Expect.equal (proj.Get 1) "ada22" "and follows it"
            }

            test "M7 nodes a factory creates live exactly as long as the key" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal users
                let s = createSignal 1
                let cleaned = Dictionary<int, int>()
                let effectRuns = Dictionary<int, int>()

                let proj =
                    createProjectionWith
                        _.Id
                        (fun item ->
                            let id = (untrack item).Id
                            onCleanup (fun () -> bump cleaned id)
                            let m = createMemo (fun _ -> $"%s{(item ()).Name}%d{s.Value}")

                            createEffect (fun () ->
                                s.Value |> ignore
                                bump effectRuns id)

                            fun () -> m.Value)
                        (fun () -> source.Value)

                Expect.equal (proj.Get 1) "ada1" "row 1 reads its factory's memo"
                Expect.equal (proj.Get 2) "bob1" "row 2 reads its own"

                source.Value <-
                    source.Value
                    |> List.map (fun u -> if u.Id = 1 then { u with Name = "adele" } else u)

                Expect.equal (proj.Get 1) "adele1" "an item change keeps the factory's memo"
                Expect.equal cleaned.Count 0 "and disposes nothing"

                source.Value <- source.Value |> List.filter (fun u -> u.Id <> 2)
                Expect.sequenceEqual proj.Keys [ 1; 3 ] "precondition: key 2 is removed"
                Expect.equal cleaned[2] 1 "removing key 2 disposes its nodes once"
                Expect.isFalse (cleaned.ContainsKey 1) "and leaves key 1's"

                let before2 = effectRuns[2]
                s.Value <- 2
                Expect.equal effectRuns[2] before2 "key 2's effect is disposed"
                Expect.equal effectRuns[1] 2 "key 1's effect still runs"
                Expect.equal (proj.Get 1) "adele2" "key 1's memo still follows its sources"

                proj.Dispose ()
                Expect.equal cleaned[1] 1 "projection disposal disposes key 1's nodes once"
                Expect.equal cleaned[2] 1 "and key 2's are not disposed again"
            }

            test "a reader may read another row of its own projection" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1; 2 ]
                let mutable self: Projection<int, int> = Unchecked.defaultof<_>

                self <- createProjection id (fun x -> if x = 1 then self.Get 2 + 1 else x * 10) (fun () -> source.Value)

                Expect.equal (self.Get 1) 21 "row 1 read row 2"
            }

            test "a row that catches a sibling row's failure is not blamed for the sibling's creation" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1; 2 ]
                let mutable self: Projection<int, string> = Unchecked.defaultof<_>

                self <-
                    createProjection
                        id
                        (fun x ->
                            if x = 1 then
                                try
                                    self.Get 2
                                with _ ->
                                    "fallback"
                            else
                                onCleanup ignore
                                "two")
                        (fun () -> source.Value)

                Expect.equal (self.Get 1) "fallback" "row 1 created nothing"
                Expect.throwsT<InvalidOperationException> (fun () -> self.Get 2 |> ignore) "row 2 created a node"
            }
        ]
