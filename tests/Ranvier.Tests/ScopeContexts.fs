module Ranvier.Tests.ScopeContexts

open System
open Expecto
open Ranvier

/// <summary>
/// The context a computation's scope is discharged and disposed under, a
/// lookup's per-key purity, and a disposed computation's later creations.
/// </summary>
[<Tests>]
let tests =
    testList
        "ScopeContexts"
        [
            test "a cleanup read during a pulled memo's discharge is not tracked by a memo reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let other = createSignal 0

                let o =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> other.Value |> ignore)
                        n)

                let mutable runs = 0

                let p =
                    createMemo (fun () ->
                        runs <- runs + 1
                        s.Value |> ignore
                        o.Value * 10)

                Expect.equal p.Value 10 "first read"
                s.Value <- 2
                Expect.equal p.Value 20 "the reader pulled the re-run"
                Expect.equal other.ObserverCount 0 "the cleanup's read left no edge"
                other.Value <- 1
                Expect.equal p.Value 20 "same value"
                Expect.equal runs 2 "a write to the cleanup's signal does not re-run the reader"
            }

            test "a cleanup read during a pulled memo's discharge is not tracked by an effect reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let other = createSignal 0

                let o =
                    createMemoWith (fun () ->
                        let n = s.Value
                        onCleanup (fun () -> other.Value |> ignore)
                        n)

                let mutable runs = 0

                createEffect (fun () ->
                    runs <- runs + 1
                    s.Value |> ignore
                    o.Value |> ignore)

                s.Value <- 2
                Expect.equal runs 2 "the write re-ran the effect"
                other.Value <- 1
                Expect.equal runs 2 "a write to the cleanup's signal does not re-run the effect"
                Expect.equal other.ObserverCount 0 "the cleanup's read left no edge"
            }

            test "a pure reader is not blamed for a node created by a pulled owning memo's cleanup" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let mutable rootCleaned = 0

                let o =
                    createMemoWith (fun () ->
                        let n = s.Value

                        onCleanup (fun () ->
                            createRoot (fun _ -> onCleanup (fun () -> rootCleaned <- rootCleaned + 1))
                            |> ignore)

                        n)

                let p =
                    createMemo (fun () ->
                        s.Value |> ignore
                        o.Value * 10)

                Expect.equal p.Value 10 "first read"
                s.Value <- 2
                Expect.equal p.Value 20 "the cleanup's root does not fail the reader"
                Expect.equal rootCleaned 0 "the root belongs to the owning memo and is still live"
                s.Value <- 3
                Expect.equal p.Value 30 "next run"
                Expect.equal rootCleaned 1 "the owning memo disposed the root with its previous run"
                o.Dispose ()
                Expect.equal rootCleaned 3 "the memo disposes its last root, and the root its final cleanup creates is disposed at once"
            }

            test "a cleanup read during an owning memo's disposal is not tracked by the disposing effect" {
                use g = new Graph ()
                use _ = g.Activate ()
                let gate = createSignal false
                let other = createSignal 0

                let o =
                    createMemoWith (fun () ->
                        onCleanup (fun () -> other.Value |> ignore)
                        1)

                o.Value |> ignore
                let mutable runs = 0

                createEffect (fun () ->
                    runs <- runs + 1

                    if gate.Value then
                        o.Dispose ())

                gate.Value <- true
                Expect.equal runs 2 "the effect disposed the memo"
                Expect.equal other.ObserverCount 0 "the cleanup's read left no edge"
            }

            test "a lookup key that creates a node fails, and a nested key read inside it is not blamed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let mutable self: Lookup<int, int> = Unchecked.defaultof<_>

                self <-
                    createLookup
                        (fun sel k ->
                            if k = 1 then
                                (try
                                    createRoot (fun _ -> ()) |> ignore
                                 with _ ->
                                     ())

                                (try
                                    self.Get 2
                                 with _ ->
                                     -100)
                                + 1
                            else
                                sel + k)
                        (fun _ _ -> [ 1; 2 ])
                        (fun () -> s.Value)

                Expect.throwsT<InvalidOperationException> (fun () -> self.Get 1 |> ignore) "key 1 created a node"
                Expect.equal (self.Get 2) 3 "key 2 creates nothing"
            }

            test "a node created by a lookup's affected fails the lookup's keys, not the pure reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let lookup =
                    createLookup
                        (fun sel k -> sel + k)
                        (fun prev next ->
                            onCleanup ignore
                            [ prev; next ])
                        (fun () -> s.Value)

                let p =
                    createMemo (fun () ->
                        s.Value |> ignore
                        lookup.Get 2)

                Expect.equal p.Value 3 "first read"

                let ex =
                    Expect.throwsC
                        (fun () ->
                            g.Batch (fun () ->
                                s.Value <- 2
                                p.Value)
                            |> ignore)
                        id

                Expect.isFalse (ex.Message.Contains "createMemo") "the reader is not blamed"
                Expect.stringContains ex.Message "affected" "the lookup's rule names affected"
            }

            test "a node created by a lookup's affected fails the keys when the lookup's effect refreshes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let mutable cleaned = 0

                let lookup =
                    createLookup
                        (fun sel k -> sel + k)
                        (fun prev next ->
                            onCleanup (fun () -> cleaned <- cleaned + 1)
                            [ prev; next ])
                        (fun () -> s.Value)

                let p = createMemo (fun () -> lookup.Get 2)
                Expect.equal p.Value 3 "first read"
                s.Value <- 2

                let ex = Expect.throwsC (fun () -> p.Value |> ignore) id
                Expect.stringContains ex.Message "affected" "the key failed under the lookup's rule"
                g.Dispose ()
                Expect.equal cleaned 0 "the cleanup was never registered"
            }

            test "a lookup key computed at top level defers the effects its f wakes until the read returns" {
                use g = new Graph ()
                use _ = g.Activate ()
                let w = createSignal 0
                let mutable effectRuns = 0

                createEffect (fun () ->
                    w.Value |> ignore
                    effectRuns <- effectRuns + 1)

                let mutable seenMid = -1

                let lookup =
                    createLookup
                        (fun (_: int) (k: int) ->
                            w.Value <- w.Peek + 1
                            seenMid <- effectRuns
                            k)
                        (fun _ _ -> Seq.empty)
                        (fun () -> 0)

                Expect.equal (lookup.Get 1) 1 "the read"
                Expect.equal seenMid 1 "the woken effect had not run inside f"
                Expect.equal effectRuns 2 "it ran once, after the read"
            }

            test "an owning memo disposed during its body disposes the nodes created after the disposal" {
                use g = new Graph ()
                use _ = g.Activate ()
                let tick = createSignal 0
                let mutable self: Memo<int> = Unchecked.defaultof<_>

                self <-
                    createMemoWith (fun () ->
                        onCleanup ignore
                        self.Dispose ()
                        createEffect (fun () -> tick.Value |> ignore)
                        1)

                Expect.equal self.Value 1 "the body completes"
                Expect.equal tick.ObserverCount 0 "the effect was disposed with the memo"
            }

            test "an owning memo disposed before its body's first creation disposes that creation" {
                use g = new Graph ()
                use _ = g.Activate ()
                let tick = createSignal 0
                let mutable self: Memo<int> = Unchecked.defaultof<_>

                self <-
                    createMemoWith (fun () ->
                        self.Dispose ()
                        createEffect (fun () -> tick.Value |> ignore)
                        1)

                Expect.equal self.Value 1 "the body completes"
                Expect.equal tick.ObserverCount 0 "the effect was disposed with the memo"
            }

            test "a root created in a disposed root is disposed at once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let tick = createSignal 0

                createRoot (fun owner ->
                    owner.Dispose ()
                    createRoot (fun _ -> createEffect (fun () -> tick.Value |> ignore)))

                Expect.equal tick.ObserverCount 0 "the effect never subscribed"
            }
        ]
