module Ranvier.Tests.ValueReaderCases

open System
open System.Collections.Generic
open Ranvier

let private check label expected actual =
    if expected <> actual then failwithf "%s: expected %A, got %A" label expected actual

let private changes (delta: ProjectionDelta<int>) =
    delta.Changes |> Seq.map(fun change -> change.Key, change.Value) |> Seq.sortBy fst |> List.ofSeq

let independentReaders () =
    use graph = new Graph()
    graph.Run(fun () ->
        let input = createSignal [|1, 10; 2, 20|]
        let rows = createProjection fst snd (fun () -> input.Value)
        use fast = rows.NewValueReader ()
        use slow = rows.NewValueReader ()
        use keys = rows.NewKeyReader ()
        check "first value read resets" true (fast.Read().IsReset)
        slow.Read () |> ignore
        keys.Read () |> ignore
        input.Value <- [|1, 11; 2, 20|]
        let retained = fast.Read ()
        check "one changed key" [1, KeyChange.Changed] (changes retained)
        check "key reader ignores values" true (keys.Read().IsEmpty)
        input.Value <- [|1, 11; 2, 22|]
        check "fast reader advances separately" [2, KeyChange.Changed] (changes (fast.Read ()))
        check "slow reader catches both" [1, KeyChange.Changed; 2, KeyChange.Changed] (changes (slow.Read ()))
        check "retained delta is unchanged" [1, KeyChange.Changed] (changes retained)
        check "idle value read" true (fast.Read().IsEmpty))

let reactiveCutoffs () =
    use graph = new Graph()
    graph.Run(fun () ->
        let input = createSignal [|1, 10; 2, 20|]
        let mutable calls = 0
        let rows = createProjection fst (fun (_, value) -> calls <- calls + 1; value % 2) (fun () -> input.Value)
        use reader = rows.NewValueReader ()
        let seen = ResizeArray<ProjectionDelta<int>>()
        createEffect(fun () -> seen.Add(reader.Read ()))
        check "initial rows evaluated once" 2 calls
        input.Value <- [|1, 12; input.Peek[1]|]
        check "equal settled output stays asleep" 1 seen.Count
        check "only changed row computed" 3 calls
        input.Value <- [|1, 13; input.Peek[1]|]
        check "unequal output wakes" 2 seen.Count
        check "changed output" [1, KeyChange.Changed] (changes seen[1]))

let firstSettlementAndStates () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = AsyncSource<int>(graph)
        let mode = createSignal 0
        let rows = createIndexProjection (fun _ ->
            if mode.Value = 1 then failwith "row failed"
            source.Value) (fun () -> [1])
        use reader = rows.NewValueReader ()
        reader.Read () |> ignore
        source.Settle 0
        check "first default-valued settlement is a change" [0, KeyChange.Changed] (changes (reader.Read ()))
        mode.Value <- 1
        check "failure keeps settled value" true (reader.Read().IsEmpty)
        mode.Value <- 0
        check "equal recovery changes no settled value" true (reader.Read().IsEmpty)
        source.Settle 3
        check "later accepted result changes" [0, KeyChange.Changed] (changes (reader.Read ())))

let previouslySettledRows () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = createAsyncSource<int>()
        let rows = createIndexProjection (fun _ -> source.Value) (fun () -> [0])
        source.Settle 7
        check "row settled before value observation" 7 (rows.Get 0)
        source.Fail (InvalidOperationException "later failure")
        use reader = rows.NewValueReader ()
        check "new reader sees failed row's last settlement" true (reader.Read().IsReset)
        source.Settle 7
        check "same settlement after failure is not a new value" true (reader.Read().IsEmpty))

let purityFailuresDoNotPublish () =
    use graph = new Graph()
    graph.Run(fun () ->
        let violate = createSignal false
        let rows = createIndexProjection (fun _ ->
            if violate.Value then onCleanup ignore
            if violate.Value then 99 else 1) (fun () -> [0])
        use reader = rows.NewValueReader ()
        reader.Read () |> ignore
        violate.Value <- true
        check "rejected body value is not published" true (reader.Read().IsEmpty)
        violate.Value <- false
        check "accepted value unchanged after recovery" true (reader.Read().IsEmpty))

let replacementLifetimes () =
    use graph = new Graph()
    graph.Run(fun () ->
        let input = createSignal [1]
        let value = createSignal 10
        let mutable cleanups = 0
        let rows = createProjectionWith id (fun _ ->
            onCleanup(fun () -> cleanups <- cleanups + 1)
            fun () -> value.Value) (fun () -> input.Value)
        use reader = rows.NewValueReader ()
        let observed = ResizeArray<ProjectionDelta<int>>()
        createEffect(fun () -> observed.Add(reader.Read ()))
        use slow = rows.NewValueReader ()
        slow.Read () |> ignore
        input.Value <- []
        input.Value <- [1]
        value.Value <- 11
        check "new row replaced old scope" 1 cleanups
        check "replacement survives later Changed" [1, KeyChange.Replaced] (changes (slow.Read ()))
        rows.Dispose ()
        check "disposal cleans replacement" 2 cleanups
        let endDelta = slow.Read ()
        check "source disposal resets" true endDelta.IsReset
        check "disposed source is empty" 0 endDelta.Keys.Length)

let valueReaderDisposal () =
    use graph = new Graph()
    graph.Run(fun () ->
        let value = createSignal 1
        let mutable calls = 0
        let rows = createIndexProjection (fun _ -> calls <- calls + 1; value.Value) (fun () -> [0])
        let reader = rows.NewValueReader ()
        createEffect(fun () -> reader.Read () |> ignore)
        (reader :> IDisposable).Dispose ()
        value.Value <- 2
        check "last reader detaches row observation" 1 calls
        let mutable rejected = false
        try reader.Read () |> ignore with error -> rejected <- error.Message.Contains "disposed"
        check "disposed reader rejects reads" true rejected)

let batchCoalescing () =
    use graph = new Graph()
    graph.Run(fun () ->
        let value = createSignal 0
        let rows = createIndexProjection (fun _ -> value.Value) (fun () -> [0])
        use reader = rows.NewValueReader ()
        let seen = ResizeArray<ProjectionDelta<int>>()
        createEffect(fun () -> seen.Add(reader.Read ()))
        batch(fun () -> for i in 1 .. 100 do value.Value <- i)
        check "one value delta after batch" 2 seen.Count
        check "one changed key" [0, KeyChange.Changed] (changes seen[1])
        check "current final value" 100 (rows.Get 0))

let mapReplacement () =
    use graph = new Graph()
    graph.Run(fun () ->
        let input = createSignal [1; 2; 3]
        let mutable factories = 0
        let mutable cleanups = 0
        let rows = createProjection id id (fun () -> input.Value)
        let mapped = rows |> Projection.mapWith(fun _ read ->
            factories <- factories + 1
            onCleanup(fun () -> cleanups <- cleanups + 1)
            fun () -> read () * 2)
        createEffect(fun () -> mapped.Keys |> Array.map mapped.Get |> ignore)
        input.Value <- [3; 2; 1]
        check "reorder reuses map rows" 3 factories
        input.Value <- [3; 1; 4]
        check "delta creates only new map row" 4 factories
        check "delta disposes only removed map row" 1 cleanups
        check "mapped output" [6; 2; 8] (mapped.Keys |> Array.map mapped.Get |> List.ofArray))

let sparsePipeline () =
    use graph = new Graph()
    graph.Run(fun () ->
        let input = createSignal (Array.init 10000 (fun i -> i, i))
        let mutable first, second = 0, 0
        let rows = createProjection fst snd (fun () -> input.Value)
        let mapped = rows |> Projection.map(fun v -> first <- first + 1; v * 2)
        let mappedAgain = mapped |> Projection.map(fun v -> second <- second + 1; v + 1)
        use reader = mappedAgain.NewValueReader ()
        createEffect(fun () -> reader.Read () |> ignore)
        check "initial first map" 10000 first
        check "initial second map" 10000 second
        let next = Array.copy input.Peek
        next[5000] <- 5000, 90000
        input.Value <- next
        check "one first-map recomputation" 10001 first
        check "one second-map recomputation" 10001 second
        check "correct terminal row" 180001 (mappedAgain.Get 5000))

let hiddenRows () =
    use graph = new Graph()
    graph.Run(fun () ->
        let input = createSignal [|1, 1; 2, 2|]
        let rows = createProjection fst snd (fun () -> input.Value)
        let visible = rows |> Projection.filter(fun value -> if value < 0 then failwith "hidden" else true)
        use reader = visible.NewValueReader ()
        reader.Read () |> ignore
        input.Value <- [|1, -1; 2, 2|]
        check "failed predicate removes visible key" [1, KeyChange.Removed] (changes (reader.Read ()))
        input.Value <- [|1, 3; 2, 2|]
        check "recovered predicate adds visible key" [1, KeyChange.Added] (changes (reader.Read ())))

let selfStalingRows () =
    use graph = new Graph()
    graph.Run(fun () ->
        let value = createSignal 1
        let rows = createIndexProjection (fun _ ->
            let captured = value.Value
            if captured = 1 then value.Value <- 2
            captured) (fun () -> [0])
        use reader = rows.NewValueReader ()
        reader.Read () |> ignore
        check "suspect draining reaches current row" 2 (rows.Get 0)
        check "no missed change left after first read" true (reader.Read().IsEmpty))

let editableSource () =
    use graph = new Graph()
    graph.Run(fun () ->
        use source = createKeyedCollection fst
        use reader = source.Rows.NewValueReader ()
        check "empty source resets" true (reader.Read().IsReset)
        source.Edit(fun edit ->
            edit.AddOrUpdate(1, 10)
            edit.AddOrUpdate(2, 20))
        check "batch adds" [1, KeyChange.Added; 2, KeyChange.Added] (changes (reader.Read ()))
        source.AddOrUpdate(1, 11)
        check "direct value update" [1, KeyChange.Changed] (changes (reader.Read ()))
        check "updated row" (1, 11) (source.Rows.Get 1)
        source.Edit(fun edit ->
            check "existing removed" true (edit.Remove 1)
            edit.AddOrUpdate(1, 11))
        check "replacement even with equal payload" [1, KeyChange.Replaced] (changes (reader.Read ()))
        check "replacement appended" [2; 1] (List.ofArray source.Rows.Keys)
        check "absent removal" false (source.Remove 999)
        source.Clear ()
        check "clear removes both" [1, KeyChange.Removed; 2, KeyChange.Removed] (changes (reader.Read ()))
        check "empty after clear" 0 source.Rows.Keys.Length)

let editableSparsePipeline () =
    use graph = new Graph()
    graph.Run(fun () ->
        use source = createKeyedCollection fst
        source.Edit(fun edit -> for i in 0 .. 9999 do edit.AddOrUpdate(i, i))
        let mutable first, second = 0, 0
        let mapped = source.Rows |> Projection.map(fun (_, v) -> first <- first + 1; v * 2)
        let terminal = mapped |> Projection.map(fun v -> second <- second + 1; v + 1)
        use reader = terminal.NewValueReader ()
        let seen = ResizeArray<ProjectionDelta<int>>()
        createEffect(fun () -> seen.Add(reader.Read ()))
        let initialKeys = source.Rows.Keys
        source.AddOrUpdate(5000, 90000)
        check "source keys unchanged by value edit" true (obj.ReferenceEquals(initialKeys, source.Rows.Keys))
        check "direct edit first map only one recomputation" 10001 first
        check "direct edit second map only one recomputation" 10001 second
        check "direct edit emits only one key" [5000, KeyChange.Changed] (changes seen[1])
        check "direct edit final value" 180001 (terminal.Get 5000))

let editableLifetimeAndFailures () =
    use graph = new Graph()
    graph.Run(fun () ->
        let mutable source = Unchecked.defaultof<KeyedCollection<int, int>>
        let owner = createRoot(fun owner -> source <- createKeyedCollection id; owner)
        source.AddOrUpdate 1
        try source.Edit(fun edit -> edit.AddOrUpdate 2; failwith "callback") with _ -> ()
        check "throwing batch retains applied edits" [1; 2] (List.ofArray source.Rows.Keys)
        source.Edit(fun edit -> for i in 3 .. 1000 do edit.AddOrUpdate i; edit.Remove i |> ignore)
        check "unread churn cancels" [1; 2] (List.ofArray source.Rows.Keys)
        owner.Dispose ()
        check "owner disposes source view" 0 source.Rows.Keys.Length
        let mutable rejected = false
        try source.AddOrUpdate 3 with error -> rejected <- error.Message.Contains "disposed"
        check "owner disposal rejects future edits" true rejected)

let editableStructuralKeys () =
    use graph = new Graph()
    graph.Run(fun () ->
        use source = createKeyedCollection fst
        source.AddOrUpdate([|1|], "first")
        check "initial structural key" "first" (snd (source.Rows.Get [|1|]))
        check "equal structural key removed" true (source.Remove [|1|])
        check "no orphaned order slot" 0 source.Rows.Keys.Length
        source.AddOrUpdate([|1|], "second")
        check "reinsert unique structural key" 1 source.Rows.Keys.Length
        check "new structural row" "second" (snd (source.Rows.Get [|1|])))

let editableMappedReplacement () =
    use graph = new Graph()
    graph.Run(fun () ->
        use source = createKeyedCollection id
        source.AddOrUpdate 1
        let mutable factories, cleanups = 0, 0
        let mapped = source.Rows |> Projection.mapWith(fun _ read ->
            factories <- factories + 1
            onCleanup(fun () -> cleanups <- cleanups + 1)
            read)
        use reader = mapped.NewValueReader ()
        reader.Read () |> ignore
        source.Edit(fun edit -> edit.Remove 1 |> ignore; edit.AddOrUpdate 1)
        check "mapped batched replacement identity" [1, KeyChange.Replaced] (changes (reader.Read ()))
        check "replacement runs new factory" 2 factories
        check "replacement disposes old factory" 1 cleanups)

let mappedOverflowReplacement () =
    use graph = new Graph()
    graph.Run(fun () ->
        use source = createKeyedCollection id
        source.Edit(fun edit -> for i in 0 .. 99 do edit.AddOrUpdate i)
        let mutable factories, cleanups = 0, 0
        let mapped = source.Rows |> Projection.mapWith(fun _ read ->
            factories <- factories + 1
            onCleanup(fun () -> cleanups <- cleanups + 1)
            read)
        mapped.Keys |> Array.map mapped.Get |> ignore
        source.Edit(fun edit -> edit.Clear (); edit.AddOrUpdate 1)
        check "overflow fallback current keys" [1] (List.ofArray mapped.Keys)
        check "overflow replacement creates new factory" 101 factories
        check "overflow disposes every prior factory" 100 cleanups
        check "replacement row current" 1 (mapped.Get 1))

let mappedCleanupRecovery () =
    use graph = new Graph()
    graph.Run(fun () ->
        use source = createKeyedCollection id
        source.Edit(fun edit -> edit.AddOrUpdate 1; edit.AddOrUpdate 2)
        let mutable factories, cleanups = 0, 0
        let mutable failCleanup = true
        let mapped = source.Rows |> Projection.mapWith(fun _ read ->
            factories <- factories + 1
            onCleanup(fun () ->
                cleanups <- cleanups + 1
                if failCleanup then failCleanup <- false; failwith "cleanup")
            read)
        mapped.Keys |> Array.map mapped.Get |> ignore
        source.Edit(fun edit -> edit.Clear (); edit.AddOrUpdate 1; edit.AddOrUpdate 2)
        try mapped.Keys |> ignore with _ -> ()
        check "retry after partial cleanup reads both replacements" [1; 2] (List.ofArray mapped.Keys)
        check "retry recreates both factory scopes" 4 factories
        check "retry retires both original scopes" 2 cleanups)

let cases =
    [| "value readers have independent baselines and immutable deltas", independentReaders
       "value readers preserve reactive cutoffs", reactiveCutoffs
       "value readers report first settlement but not pending/error states", firstSettlementAndStates
       "new value readers retain rows settled before observation", previouslySettledRows
       "value readers do not publish purity-rejected results", purityFailuresDoNotPublish
       "value readers preserve replacement identity and lifetimes", replacementLifetimes
       "disposing the last value reader stops row observation", valueReaderDisposal
       "value readers coalesce batched writes", batchCoalescing
       "map deltas preserve row factories and cleanup", mapReplacement
       "sparse updates traverse a two-map pipeline incrementally", sparsePipeline
       "value readers respect hidden and recovered predicate rows", hiddenRows
       "value readers drain invalidations raised while settling", selfStalingRows
       "editable collections report direct edits and replacement identity", editableSource
       "editable collections update two maps without replacing keys", editableSparsePipeline
       "editable collections preserve scope and throwing batch semantics", editableLifetimeAndFailures
       "editable collections remove structurally equal keys", editableStructuralKeys
       "map factories consume batched source replacement deltas", editableMappedReplacement
       "map overflow recovery preserves replacement lifetimes", mappedOverflowReplacement
       "map cleanup failure recovery preserves replacement lifetimes", mappedCleanupRecovery |]
