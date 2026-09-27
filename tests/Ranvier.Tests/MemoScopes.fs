module Ranvier.Tests.MemoScopes

open System
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>
/// An owning memo, owning async memo, boundary or projection pass owns the
/// nodes its body creates. The scope is discharged before each re-run and disposed with the
/// computation, as an effect's is.
/// </summary>
[<Tests>]
let tests =
    testList
        "MemoScopes"
        [
            test "a memo's cleanup runs before its re-run and at its disposal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let cleaned = ResizeArray<int> ()

                let m =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> cleaned.Add n)
                        n)

                Expect.equal m.Value 1 "first run"
                Expect.isEmpty cleaned "a cleanup waits for the re-run"

                s.Value <- 2
                Expect.equal m.Value 2 "second run"
                Expect.sequenceEqual cleaned [ 1 ] "the first run's cleanup ran before the second"

                m.Dispose ()
                Expect.sequenceEqual cleaned [ 1; 2 ] "disposal runs the second run's cleanup"
            }

            test "a node a memo returns is disposed when the memo re-runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let runs = ResizeArray<int> ()

                let m =
                    createMemoWith (fun () ->
                        let n = s.Value

                        createEffect (fun () ->
                            tick.Value |> ignore
                            runs.Add n)

                        n)

                m.Value |> ignore
                s.Value <- 2
                m.Value |> ignore
                runs.Clear ()

                tick.Value <- 1
                Expect.sequenceEqual runs [ 2 ] "only the current run's effect is live"
            }

            test "a memo returning a memo it created hands back a disposed node after a re-run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let k = createSignal 10

                let outer = createMemoWith (fun () -> let n = s.Value in createMemo (fun () -> n * k.Value))

                let first = outer.Value
                Expect.equal first.Value 10 "the first inner memo computes"

                s.Value <- 2
                let second = outer.Value
                Expect.equal second.Value 20 "the second inner memo computes"

                k.Value <- 100
                Expect.equal second.Value 200 "the live inner memo follows its sources"
                Expect.equal first.Value 10 "the disposed one keeps its last value"
            }

            test "an effect's re-run leaves the nodes of a memo it pulled alone" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 10
                let tick = createSignal 0
                let mutable cleaned = 0

                let ext =
                    createMemoWith (fun () ->
                        let inner = createMemo (fun () -> s.Value * 2)
                        onCleanup (fun () -> cleaned <- cleaned + 1)
                        inner.Value)

                let seen = ResizeArray<int> ()

                createEffect (fun () ->
                    tick.Value |> ignore
                    seen.Add ext.Value)

                tick.Value <- 1
                Expect.equal cleaned 0 "the effect's re-run leaves the memo's cleanup"

                s.Value <- 11
                Expect.equal ext.Value 22 "the memo follows its nested memo"
                Expect.sequenceEqual seen [ 20; 20; 22 ] "and wakes the effect"
            }

            test "disposing a memo disposes the nodes its body created" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let runs = ref 0

                let m =
                    createMemoWith (fun () ->
                        createEffect (fun () ->
                            s.Value |> ignore
                            runs.Value <- runs.Value + 1)

                        0)

                m.Value |> ignore
                Expect.equal runs.Value 1 "the nested effect ran"

                m.Dispose ()
                s.Value <- 2
                Expect.equal runs.Value 1 "the nested effect is disposed with the memo"
            }

            test "a root created in a memo body is disposed when the memo re-runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let cleaned = ResizeArray<int> ()

                let m =
                    createMemoWith (fun () ->
                        let n = s.Value

                        createRoot (fun _ ->
                            onCleanup (fun () -> cleaned.Add n)
                            n))

                m.Value |> ignore
                s.Value <- 2
                m.Value |> ignore
                Expect.sequenceEqual cleaned [ 1 ] "the first run's root is disposed"
            }

            test "a memo body that creates nothing allocates no scope, and a cleanup-free re-run is unchanged" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun () -> s.Value * 2)

                Expect.equal m.Value 2 "first run"
                s.Value <- 3
                Expect.equal m.Value 6 "second run"
                Expect.equal m.Runs 2 "one run per change"
            }

            test "a memo cleanup that writes a signal wakes its effects once, and the memo runs once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let log = createSignal 0
                let effectSeen = ResizeArray<int * int> ()

                let m =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> log.Value <- log.Value + 1)
                        n)

                createEffect (fun () -> effectSeen.Add ((log.Value, m.Value)))

                s.Value <- 2
                Expect.equal m.Value 2 "the memo is current"
                Expect.equal m.Runs 2 "and ran once per change"
                Expect.equal (Seq.last effectSeen) (1, 2) "the effect saw the cleanup's write and the new value"
            }

            test "a boundary's cleanup runs before its re-run and at its disposal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let cleaned = ResizeArray<int> ()

                let b =
                    createSuspense
                        (fun _ -> -1)
                        (fun () ->
                            let n = s.Value
                            onCleanup (fun () -> cleaned.Add n)
                            n)

                Expect.equal b.Value 1 "first run"
                s.Value <- 2
                Expect.equal b.Value 2 "second run"
                Expect.sequenceEqual cleaned [ 1 ] "the first run's cleanup ran before the second"

                b.Dispose ()
                Expect.sequenceEqual cleaned [ 1; 2 ] "disposal runs the second run's cleanup"
            }

            test "an async memo's cleanup runs before the next flight starts and at its disposal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let cleaned = ResizeArray<int> ()

                let a =
                    createAsyncWith (fun _ ->
                        let n = s.Value
                        onCleanup (fun () -> cleaned.Add n)
                        Task.FromResult n)

                a.TryValue |> ignore
                Expect.equal a.Peek 1 "first flight settled"

                s.Value <- 2
                a.TryValue |> ignore
                Expect.equal a.Peek 2 "second flight settled"
                Expect.sequenceEqual cleaned [ 1 ] "the first flight's cleanup ran before the second started"

                a.Dispose ()
                Expect.sequenceEqual cleaned [ 1; 2 ] "disposal runs the second flight's cleanup"
            }

            test "an effect created after an await under runWithOwner is disposed with the flight" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let gate = TaskCompletionSource<unit> ()

                let a =
                    createAsyncWith (fun _ ->
                        let owner = getOwner ()
                        let n = s.Value

                        task {
                            do! gate.Task
                            runWithOwner owner (fun () -> createEffect (fun () -> tick.Value |> ignore))
                            return n
                        })

                a.TryValue |> ignore
                gate.SetResult ()
                Expect.equal tick.ObserverCount 1 "the effect is live while the flight is current"

                a.Dispose ()
                Expect.equal tick.ObserverCount 0 "disposing the async value disposed the effect"
            }

            test "a node created under runWithOwner on a disposed owner is disposed at once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let tick = createSignal 0
                let owner = createRoot id
                owner.Dispose ()

                runWithOwner owner (fun () -> createEffect (fun () -> tick.Value |> ignore))
                Expect.equal tick.ObserverCount 0 "the effect was disposed as it attached"
            }

            test "runWithOwner inside a pure memo raises" {
                use g = new Graph ()
                use _ = g.Activate ()
                let owner = createRoot id
                let m = createMemo (fun () -> runWithOwner owner (fun () -> 1))
                Expect.throwsT<InvalidOperationException> (fun () -> m.Value |> ignore) "a pure body cannot own nodes"
            }

            test "a projection's source owns the nodes it creates, and discharges them before the next pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let cleaned = ResizeArray<int list> ()

                let proj =
                    createProjection
                        id
                        (fun x -> x * 10)
                        (fun () ->
                            let current = items.Value
                            onCleanup (fun () -> cleaned.Add current)
                            current)

                let reader = createProjection id (fun (x: int) -> proj.Get x + 1) (fun () -> [ 1 ])

                Expect.equal (reader.Get 1) 11 "a reader pulling the pass is not blamed for the source's creation"
                Expect.isEmpty cleaned "the pass's cleanup waits for the next pass"

                items.Value <- [ 1 ]
                Expect.sequenceEqual proj.Keys [ 1 ] "second pass"
                Expect.sequenceEqual cleaned [ [ 1; 2 ] ] "the first pass's cleanup ran"

                proj.Dispose ()
                Expect.sequenceEqual cleaned [ [ 1; 2 ]; [ 1 ] ] "disposal runs the last pass's cleanup"
            }

            test "N2 a cleanup that reads its own memo sees the previous value, and the body runs once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let log = ResizeArray<string> ()
                let mutable self: Memo<int> = Unchecked.defaultof<_>
                let mutable bodyRuns = 0

                self <-
                    createMemoWith (fun () ->
                        bodyRuns <- bodyRuns + 1
                        let n = s.Value
                        onCleanup (fun () -> log.Add $"A{n}")
                        onCleanup (fun () -> log.Add $"B{n}:{self.Value}")
                        n)

                self.Value |> ignore
                s.Value <- 2
                Expect.equal self.Value 2 "the memo re-ran"
                Expect.equal bodyRuns 2 "once per change"
                Expect.sequenceEqual log [ "B1:1"; "A1" ] "each cleanup ran once, reading the previous value"
                Expect.isEmpty g.Root.Errors "the re-entrant read raised nothing"

                self.Dispose ()
                Expect.equal s.ObserverCount 0 "disposal detaches the memo"
            }

            test "N2b an effect's cleanup inside an owning memo reads the memo's previous value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let seen = ResizeArray<int> ()
                let mutable a: Memo<int> = Unchecked.defaultof<_>

                a <-
                    createMemoWith (fun () ->
                        let n = s.Value
                        createEffect (fun () -> onCleanup (fun () -> seen.Add a.Value))
                        n)

                a.Value |> ignore
                s.Value <- 2
                Expect.equal a.Value 2 "the memo re-ran"
                Expect.sequenceEqual seen [ 1 ] "the cleanup saw the previous value"

                a.Dispose ()
                Expect.sequenceEqual seen [ 1; 2 ] "disposal runs the second effect's cleanup"
                Expect.equal s.ObserverCount 0 "disposal detaches the memo"
            }

            test "N2c a cleanup that reads a memo derived from its own memo sees the previous value" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let seen = ResizeArray<int> ()
                let mutable d: Memo<int> = Unchecked.defaultof<_>

                let m =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> seen.Add d.Value)
                        n)

                d <- createMemo (fun () -> m.Value + 100)
                Expect.equal d.Value 101 "first run"
                s.Value <- 2
                Expect.equal d.Value 102 "the derived memo follows"
                Expect.sequenceEqual seen [ 101 ] "the cleanup saw the previous value"

                d.Dispose ()
                m.Dispose ()
                Expect.equal s.ObserverCount 0 "disposal detaches both memos"
            }

            test "N3b a cleanup that disposes its memo stops the re-run, and the memo's nodes die" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let mutable self: Memo<int> = Unchecked.defaultof<_>
                let mutable effectRuns = 0
                let mutable bodyRuns = 0

                self <-
                    createMemoWith (fun () ->
                        bodyRuns <- bodyRuns + 1
                        let n = s.Value

                        createEffect (fun () ->
                            tick.Value |> ignore
                            effectRuns <- effectRuns + 1)

                        onCleanup (fun () -> if n = 1 then self.Dispose ())
                        n)

                self.Value |> ignore
                s.Value <- 2
                self.Value |> ignore
                Expect.equal bodyRuns 1 "the disposed memo did not re-run"
                Expect.equal s.ObserverCount 0 "the disposed memo is detached"

                let before = effectRuns
                tick.Value <- 1
                Expect.equal effectRuns before "the first run's effect died with the memo"
                Expect.equal tick.ObserverCount 0 "and is detached"
            }

            test "N2d an effect cleanup that reads its memo leaves the new run's nodes owned" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let mutable runs = 0
                let mutable bodyRuns = 0
                let mutable m: Memo<int> = Unchecked.defaultof<_>

                m <-
                    createMemoWith (fun () ->
                        bodyRuns <- bodyRuns + 1
                        let n = s.Value

                        createEffect (fun () ->
                            tick.Value |> ignore
                            runs <- runs + 1)

                        createEffect (fun () -> onCleanup (fun () -> m.Value |> ignore))
                        n)

                m.Value |> ignore
                s.Value <- 2
                m.Value |> ignore
                Expect.equal bodyRuns 2 "once per change"

                m.Dispose ()
                let before = runs
                tick.Value <- 1
                Expect.equal tick.ObserverCount 0 "every run's effect died with the memo"
                Expect.equal runs before "and stays dead"
                Expect.equal s.ObserverCount 0 "disposal detaches the memo"
            }

            test "N22 a throwing cleanup in a memo's scope reaches the graph root's errors" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let m =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> failwith "boom")
                        n)

                m.Value |> ignore
                s.Value <- 2
                Expect.equal m.Value 2 "the memo re-runs past the failing cleanup"
                Expect.equal (Seq.length g.Root.Errors) 1 "the re-run's discharge recorded the failure"

                m.Dispose ()
                Expect.equal (Seq.length g.Root.Errors) 2 "disposal recorded the second"
            }

            test "a throwing cleanup in an effect's scope reaches the graph root's errors" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let root =
                    createRoot (fun owner ->
                        createEffect (fun () ->
                            s.Value |> ignore
                            onCleanup (fun () -> failwith "boom"))

                        owner)

                s.Value <- 2
                Expect.equal (Seq.length g.Root.Errors) 1 "the re-run's discharge recorded the failure"

                root.Dispose ()
                Expect.equal (Seq.length g.Root.Errors) 2 "disposal recorded the second"
            }

            test "a throwing cleanup in a projection key's scope reaches the graph root's errors" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]

                let proj =
                    createProjectionWith id (fun item ->
                        onCleanup (fun () -> failwith "boom")
                        fun () -> item ())
                        (fun () -> items.Value)

                proj.Get 1 |> ignore
                items.Value <- []
                proj.Keys |> ignore
                Expect.equal (Seq.length g.Root.Errors) 1 "removing the key recorded the failure"
            }

            test "an effect woken by a cleanup during a nested pull runs after the memo re-ran, exactly once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let w = createSignal 0
                let log = ResizeArray<int * int> ()

                let m =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> w.Value <- w.Value + 1)
                        n)

                createEffect (fun () -> log.Add ((w.Value, m.Runs)))

                let mutable seenInRead = -1

                let reader =
                    createMemo (fun () ->
                        let v = m.Value
                        seenInRead <- log.Count
                        v * 10)

                Expect.equal reader.Value 10 "first read"
                s.Value <- 2
                Expect.equal reader.Value 20 "the nested pull re-ran the memo"
                Expect.equal seenInRead 1 "the woken effect had not run inside the read"
                Expect.sequenceEqual log [ (0, 0); (1, 2) ] "it ran once, after the memo's re-run"
            }
        ]
