module Ranvier.Tests.ComparerCases

open System
open System.Collections.Generic
open Ranvier

let private check label expected actual =
    if expected <> actual then failwithf "%s: expected %A, got %A" label expected actual

let private comparer equals =
    { new IEqualityComparer<'T> with
        member _.Equals(a, b) = equals a b
        member _.GetHashCode _ = 0 }

let private insensitive = comparer (fun (a: string) b -> String.Equals(a, b, StringComparison.OrdinalIgnoreCase))
let private parity = comparer (fun (a: int) b -> a % 2 = b % 2)
let private sameInt = comparer (fun (a: int) b -> a = b)

let signalIsolation () =
    use graph = new Graph()
    graph.Run(fun () ->
        let custom = createSignalWithComparer insensitive "abc"
        let normal = createSignal "abc"
        let customSeen, normalSeen = ResizeArray<string>(), ResizeArray<string>()
        createEffect (fun () -> customSeen.Add custom.Value)
        createEffect (fun () -> normalSeen.Add normal.Value)
        custom.Value <- "ABC"
        normal.Value <- "ABC"
        check "custom cutoff" ["abc"] (List.ofSeq customSeen)
        check "custom signal keeps accepted value" "abc" custom.Peek
        check "default node remains independent" ["abc"; "ABC"] (List.ofSeq normalSeen))

let overrideBypassesPolicy () =
    let policy = { new IEqualityPolicy with member _.Comparer<'T>() : IEqualityComparer<'T> = failwith "graph comparer used" }
    use graph = new Graph({ GraphOptions.Default with Equality = policy })
    graph.Run(fun () ->
        let source = createSignalWithComparer sameInt 1
        let memo = createMemoWithComparer sameInt (fun _ -> source.Value)
        let owning = createOwningMemoWithComparer sameInt (fun _ -> source.Value)
        let suspense = createSuspenseWithComparer sameInt (fun _ -> -1) (fun () -> source.Value)
        let errors = createErrorBoundaryWithComparer sameInt (fun _ _ -> -1) (fun () -> source.Value)
        let both = createBoundaryWithComparer sameInt (fun _ -> -1) (fun _ _ -> -1) (fun () -> source.Value)
        let seen = ResizeArray<int>()
        createEffectOnWithComparer sameInt (fun () -> memo.Value + owning.Value + suspense.Value + errors.Value + both.Value) seen.Add
        source.Value <- 2
        check "custom nodes resolve no graph comparer" [5; 10] (List.ofSeq seen))

let memoCutoff () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = createSignal 0
        let memo = createMemoWithComparer parity (fun _ -> source.Value)
        let normal = createMemo (fun _ -> source.Value)
        let seen, normalSeen = ResizeArray<int>(), ResizeArray<int>()
        createEffect (fun () -> seen.Add memo.Value)
        createEffect (fun () -> normalSeen.Add normal.Value)
        source.Value <- 2
        check "equal result suppresses dependent" [0] (List.ofSeq seen)
        check "memo still caches computed result" 2 memo.Peek
        source.Value <- 3
        check "unequal result wakes dependent" [0; 3] (List.ofSeq seen)
        check "default memo still uses graph policy" [0; 2; 3] (List.ofSeq normalSeen))

let owningMemoLifetime () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = createSignal 0
        let mutable cleanups = 0
        let memo = createOwningMemoWithComparer parity (fun _ ->
            onCleanup (fun () -> cleanups <- cleanups + 1)
            source.Value)
        createEffect (fun () -> memo.Value |> ignore)
        source.Value <- 2
        check "equal output still replaces run resources" 1 cleanups
        memo.Dispose()
        check "disposal cleans current run" 2 cleanups)

let pureMemoRemainsPure () =
    use graph = new Graph()
    graph.Run(fun () ->
        let memo = createMemoWithComparer sameInt (fun _ -> onCleanup ignore; 1)
        match memo.TryValue with
        | Failed (:? InvalidOperationException) -> ()
        | reading -> failwithf "Expected purity failure, got %A" reading)

let splitEffectLifetime () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = createSignal "abc"
        let acted = ResizeArray<string>()
        let mutable cleanups = 0
        let owner = createRoot (fun owner ->
            createEffectOnWithComparer insensitive (fun () -> source.Value) (fun value ->
                acted.Add value
                onCleanup (fun () -> cleanups <- cleanups + 1))
            owner)
        source.Value <- "ABC"
        check "equal compute skips act" ["abc"] (List.ofSeq acted)
        check "equal compute keeps action resources" 0 cleanups
        source.Value <- "abd"
        check "changed compute replaces action" ["abc"; "abd"] (List.ofSeq acted)
        check "previous action cleaned" 1 cleanups
        owner.Dispose()
        check "current action cleaned" 2 cleanups)

let stateTransitions () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = createAsyncSource<int>()
        let memo = createMemoWithComparer (comparer (fun _ _ -> true)) (fun _ -> source.Value)
        let seen = ResizeArray<string>()
        createEffect (fun () ->
            seen.Add (match memo.TryValue with Ready value -> $"ready:{value}" | Pending -> "pending" | Failed error -> error.Message))
        source.Settle 1
        source.Fail (InvalidOperationException "failed")
        source.Settle 1
        check "state changes propagate even with always equal values" ["pending"; "ready:1"; "failed"; "ready:1"] (List.ofSeq seen))

let boundaryCutoff () =
    use graph = new Graph()
    graph.Run(fun () ->
        let source = createSignal 0
        let boundary = createBoundaryWithComparer parity (fun _ -> -1) (fun _ _ -> -2) (fun () -> source.Value)
        let seen = ResizeArray<int>()
        createEffect (fun () -> seen.Add boundary.Value)
        source.Value <- 2
        source.Value <- 3
        check "boundary uses its own cutoff" [0; 3] (List.ofSeq seen))

let boundaryStateTransitions () =
    use graph = new Graph()
    graph.Run(fun () ->
        let equal = comparer (fun (_: int) _ -> true)
        let source = createAsyncSource<int>()
        let suspense = createSuspenseWithComparer equal (fun _ -> 0) (fun () -> source.Value)
        let waiting = ResizeArray<int * bool>()
        createEffect (fun () -> waiting.Add (suspense.Value, suspense.IsWaiting))
        source.Settle 0
        check "equal fallback-to-ready transition propagates" [(0, true); (0, false)] (List.ofSeq waiting)
        let mode = createSignal 0
        let errors = createErrorBoundaryWithComparer equal (fun _ _ -> 0) (fun () ->
            if mode.Value <> 0 then failwith $"failure {mode.Value}"
            0)
        let seen = ResizeArray<int>()
        createEffect (fun () -> seen.Add errors.Value)
        mode.Value <- 1
        mode.Value <- 2
        mode.Value <- 0
        check "equal recovered values still expose caught-state changes" [0; 0; 0; 0] (List.ofSeq seen))

let comparerFailures () =
    use graph = new Graph()
    graph.Run(fun () ->
        let mutable armed = false
        let equal = comparer (fun (a: int) b -> if armed then failwith "comparison failed" else a = b)
        let signal = createSignalWithComparer equal 1
        let source = createSignal 1
        let memo = createMemoWithComparer equal (fun previous ->
            check "last accepted value survives failure" (if source.Value = 1 then ValueNone else ValueSome 1) previous
            source.Value)
        let mutable recovered = 0
        let boundary = createErrorBoundaryWithComparer equal (fun _ _ -> recovered <- recovered + 1; -1) (fun () -> source.Value)
        let acted = ResizeArray<int>()
        createEffectOnWithComparer equal (fun () -> source.Value) acted.Add
        memo.Value |> ignore
        boundary.Value |> ignore
        armed <- true
        let mutable threw = false
        try signal.Value <- 2 with error -> check "signal failure" "comparison failed" error.Message; threw <- true
        check "signal comparer throws to writer" true threw
        check "signal retains value" 1 signal.Peek
        source.Value <- 2
        for reading in [memo.TryValue; boundary.TryValue] do
            match reading with Failed error -> check "node failure" "comparison failed" error.Message | _ -> failwith "Expected comparer failure"
        check "comparer error bypasses boundary recover" 0 recovered
        check "failed comparison does not act" [1] (List.ofSeq acted)
        armed <- false
        source.Value <- 3
        check "memo recovers" 3 memo.Value
        check "boundary recovers" 3 boundary.Value
        check "effect recovers" [1; 3] (List.ofSeq acted))

let nullComparersRejected () =
    use graph = new Graph()
    graph.Run(fun () ->
        let missing = Unchecked.defaultof<IEqualityComparer<int>>
        let attempts =
            [ (fun () -> createSignalWithComparer missing 1 |> ignore)
              (fun () -> createMemoWithComparer missing (fun _ -> 1) |> ignore)
              (fun () -> createOwningMemoWithComparer missing (fun _ -> 1) |> ignore)
              (fun () -> createEffectOnWithComparer missing (fun () -> 1) ignore)
              (fun () -> createSuspenseWithComparer missing (fun _ -> 0) (fun () -> 1) |> ignore)
              (fun () -> createErrorBoundaryWithComparer missing (fun _ _ -> 0) (fun () -> 1) |> ignore)
              (fun () -> createBoundaryWithComparer missing (fun _ -> 0) (fun _ _ -> 0) (fun () -> 1) |> ignore) ]
        for attempt in attempts do
            let mutable rejected = false
            try attempt () with :? ArgumentNullException -> rejected <- true
            check "null comparer rejected at construction" true rejected)

let cases =
    [| "per-signal comparers preserve other nodes' policies", signalIsolation
       "per-node overrides bypass graph comparer resolution", overrideBypassesPolicy
       "per-memo comparers suppress equal results without changing default nodes", memoCutoff
       "owning memo comparers retain per-run cleanup", owningMemoLifetime
       "custom-comparer memos retain purity enforcement", pureMemoRemainsPure
       "split-effect comparers retain action resource ownership", splitEffectLifetime
       "custom equality cannot suppress Pending and Failed transitions", stateTransitions
       "boundary values use their own comparer", boundaryCutoff
       "boundary waiting and caught states bypass value cutoff", boundaryStateTransitions
       "throwing node comparers preserve failure and recovery semantics", comparerFailures
       "null node comparers are rejected", nullComparersRejected |]
