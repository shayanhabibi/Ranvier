module Ranvier.Tests.DischargeReentry

open System
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>
/// The <c>InvalidOperationException</c> raised by <c>f</c>.
/// </summary>
let private invalidOp (f: unit -> unit) (message: string) : InvalidOperationException =
    try
        f ()
        failtest message
    with :? InvalidOperationException as ex ->
        ex

/// <summary>
/// A cleanup that writes a source of its computation and then reads the
/// computation, an effect's teardown under a pure reader, and the error a
/// pure body reports after swallowing its violation.
/// </summary>
[<Tests>]
let tests =
    testList
        "DischargeReentry"
        [
            test "N2e a memo cleanup that writes a source and reads the memo runs the body once per change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let mutable self: Memo<int> = Unchecked.defaultof<_>
                let mutable bodyRuns = 0
                let mutable effectRuns = 0

                self <-
                    createMemoWith (fun _ ->
                        bodyRuns <- bodyRuns + 1
                        let n = s.Value

                        createEffect (fun () ->
                            tick.Value |> ignore
                            effectRuns <- effectRuns + 1)

                        onCleanup (fun () ->
                            if n = 2 then
                                s.Value <- 3
                                self.Value |> ignore)

                        n)

                self.Value |> ignore
                s.Value <- 2
                self.Value |> ignore
                s.Value <- 10
                Expect.equal self.Value 3 "the memo serves the cleanup's write"
                Expect.equal bodyRuns 3 "one run per change"
                Expect.equal tick.ObserverCount 1 "one generation of effects is alive"

                let before = effectRuns
                tick.Value <- 1
                Expect.equal (effectRuns - before) 1 "a write runs one effect"

                self.Dispose ()
                Expect.equal tick.ObserverCount 0 "disposal disposes the effect"
                Expect.equal s.ObserverCount 0 "disposal detaches the memo"
            }

            test "a memo cleanup that writes a source without reading the memo still re-runs it with the write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let mutable bodyRuns = 0

                let m =
                    createMemoWith (fun _ ->
                        bodyRuns <- bodyRuns + 1
                        let n = s.Value

                        onCleanup (fun () ->
                            if n = 2 then
                                s.Value <- 3)

                        n)

                m.Value |> ignore
                s.Value <- 2
                Expect.equal m.Value 2 "the second run"
                s.Value <- 10
                Expect.equal m.Value 3 "the run after the discharge sees the cleanup's write"
                Expect.equal bodyRuns 3 "one run per pull"
            }

            test "a memo cleanup that writes a source after reading the memo leaves the memo stale for the next read" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let mutable self: Memo<int> = Unchecked.defaultof<_>

                self <-
                    createMemoWith (fun _ ->
                        let n = s.Value

                        onCleanup (fun () ->
                            if n = 2 then
                                s.Value <- 3
                                self.Value |> ignore
                                s.Value <- 4)

                        n)

                self.Value |> ignore
                s.Value <- 2
                self.Value |> ignore
                s.Value <- 10
                Expect.equal self.Value 4 "the read after the discharge serves the cleanup's last write"
            }

            test "N2e an error boundary cleanup that writes a source and reads the boundary runs the body once per change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let mutable self: Boundary<int> = Unchecked.defaultof<_>
                let mutable bodyRuns = 0

                self <-
                    createErrorBoundary (fun _ _ -> -1) (fun () ->
                        bodyRuns <- bodyRuns + 1
                        let n = s.Value
                        createEffect (fun () -> tick.Value |> ignore)

                        onCleanup (fun () ->
                            if n = 2 then
                                s.Value <- 3
                                self.Value |> ignore)

                        n)

                self.Value |> ignore
                s.Value <- 2
                self.Value |> ignore
                s.Value <- 10
                Expect.equal self.Value 3 "the boundary serves the cleanup's write"
                Expect.equal bodyRuns 3 "one run per change"
                Expect.equal tick.ObserverCount 1 "one generation of effects is alive"

                self.Dispose ()
                Expect.equal tick.ObserverCount 0 "disposal disposes the effect"
            }

            test "N2e a projection source cleanup that writes a source and reads the projection runs one pass per change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let mutable self: Projection<int, int> = Unchecked.defaultof<_>

                self <-
                    createProjection id (fun x -> x * 10) (fun () ->
                        let n = s.Value
                        createEffect (fun () -> tick.Value |> ignore)

                        onCleanup (fun () ->
                            if n = 2 then
                                s.Value <- 3
                                self.Keys |> ignore)

                        [ n ])

                self.Keys |> ignore
                s.Value <- 2
                self.Keys |> ignore
                s.Value <- 10
                Expect.sequenceEqual self.Keys [ 3 ] "the projection serves the cleanup's write"
                Expect.equal self.Runs 3 "one pass per change"
                Expect.equal tick.ObserverCount 1 "one generation of effects is alive"

                self.Dispose ()
                Expect.equal tick.ObserverCount 0 "disposal disposes the effect"
            }

            test "N2e an async memo cleanup that writes a source and reads the async memo starts one flight per change" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let tick = createSignal 0
                let mutable self: AsyncMemo<int> = Unchecked.defaultof<_>

                self <-
                    createAsyncWith (fun _ ->
                        let n = s.Value
                        createEffect (fun () -> tick.Value |> ignore)

                        onCleanup (fun () ->
                            if n = 2 then
                                s.Value <- 3
                                self.TryValue |> ignore)

                        Task.FromResult n)

                self.TryValue |> ignore
                s.Value <- 2
                self.TryValue |> ignore
                s.Value <- 10
                self.TryValue |> ignore
                Expect.equal self.Peek 3 "the async memo serves the cleanup's write"
                Expect.equal self.Runs 3 "one flight per change"
                Expect.equal tick.ObserverCount 1 "one generation of effects is alive"

                self.Dispose ()
                Expect.equal tick.ObserverCount 0 "disposal disposes the effect"
            }

            test "an effect cleanup read during a disposal from a pure memo is not tracked by the memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let trigger = createSignal 0
                let other = createSignal 0
                let mutable root: Owner = Unchecked.defaultof<_>

                createRoot (fun o ->
                    root <- o
                    createEffect (fun () -> onCleanup (fun () -> other.Value |> ignore)))

                let mutable runs = 0

                let m =
                    createMemo (fun _ ->
                        runs <- runs + 1

                        if trigger.Value = 1 then
                            root.Dispose ()

                        trigger.Value)

                m.Value |> ignore
                trigger.Value <- 1
                Expect.equal m.Value 1 "the memo disposed the root"
                Expect.equal other.ObserverCount 0 "the cleanup's read left no edge"
                other.Value <- 1
                m.Value |> ignore
                Expect.equal runs 2 "a write to the cleanup's signal does not re-run the memo"
            }

            test "a pure memo that disposes a root is not blamed for a node created by an effect's cleanup" {
                use g = new Graph ()
                use _ = g.Activate ()
                let trigger = createSignal 0
                let mutable root: Owner = Unchecked.defaultof<_>

                createRoot (fun o ->
                    root <- o
                    createEffect (fun () -> onCleanup (fun () -> onCleanup ignore)))

                let m =
                    createMemo (fun _ ->
                        if trigger.Value = 1 then
                            root.Dispose ()

                        trigger.Value)

                m.Value |> ignore
                trigger.Value <- 1
                Expect.equal m.Value 1 "the memo's own body created nothing"
            }

            test "a pure memo that disposes a root gains no edge from the root's own cleanup" {
                use g = new Graph ()
                use _ = g.Activate ()
                let trigger = createSignal 0
                let other = createSignal 0

                let root =
                    createRoot (fun o ->
                        onCleanup (fun () -> other.Value |> ignore)
                        o)

                let mutable runs = 0

                let m =
                    createMemo (fun _ ->
                        runs <- runs + 1

                        if trigger.Value = 1 then
                            root.Dispose ()

                        trigger.Value)

                m.Value |> ignore
                trigger.Value <- 1
                Expect.equal m.Value 1 "the memo disposed the root"
                Expect.equal other.ObserverCount 0 "the cleanup's read left no edge"
                other.Value <- 1
                m.Value |> ignore
                Expect.equal runs 2 "a write to the cleanup's signal does not re-run the memo"
            }

            test "a pure memo that disposes a root is not blamed for a node created by the root's own cleanup" {
                use g = new Graph ()
                use _ = g.Activate ()
                let trigger = createSignal 0

                let root =
                    createRoot (fun o ->
                        onCleanup (fun () -> createEffect ignore)
                        o)

                let m =
                    createMemo (fun _ ->
                        if trigger.Value = 1 then
                            root.Dispose ()

                        trigger.Value)

                m.Value |> ignore
                trigger.Value <- 1
                Expect.equal m.Value 1 "the memo's own body created nothing"
            }

            test "a scope created and disposed by a cleanup is released at once" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let weak = ResizeArray<WeakReference>()

                let m =
                    createMemoWith (fun _ ->
                        let v = s.Value

                        onCleanup (fun () ->
                            let r = createRoot id
                            r.Dispose ()
                            weak.Add (WeakReference r))

                        v)

                m.Value |> ignore
                s.Value <- 1
                m.Value |> ignore

                GC.Collect ()
                GC.WaitForPendingFinalizers ()
                GC.Collect ()

                Expect.equal weak.Count 1 "precondition: one discharge ran the cleanup"
                Expect.isFalse weak[0].IsAlive "the memo's scope keeps no link to the disposed scope"
            }

            test "an effect cleanup that registers a cleanup during a re-run gives it to the effect's scope" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let cleaned = ResizeArray<int>()

                createEffect (fun () ->
                    let n = s.Value
                    onCleanup (fun () -> onCleanup (fun () -> cleaned.Add n)))

                s.Value <- 2
                Expect.isEmpty cleaned "the late cleanup waits for the next run"
                s.Value <- 3
                Expect.sequenceEqual cleaned [ 1 ] "the next run discharges it"
            }

            test "a pure memo that swallows its violation and throws its own InvalidOperationException names createMemoWith" {
                use g = new Graph ()
                use _ = g.Activate ()

                let m =
                    createMemo (fun _ ->
                        (try
                            createEffect ignore
                         with _ ->
                             ())

                        raise (InvalidOperationException "user"))

                let ex = invalidOp (fun () -> m.Value |> ignore) "the run fails"
                Expect.stringContains ex.Message "createMemoWith" "the message names the owning memo"
            }

            test "a pure memo that swallows its violation and reads a disposed lookup names createMemoWith" {
                use g = new Graph ()
                use _ = g.Activate ()
                let lookup = createSelector (fun () -> 1)
                lookup.Dispose ()

                let m =
                    createMemo (fun _ ->
                        (try
                            onCleanup ignore
                         with _ ->
                             ())

                        lookup.Get 1)

                let ex = invalidOp (fun () -> m.Value |> ignore) "the run fails"
                Expect.stringContains ex.Message "createMemoWith" "the message names the owning memo"
            }

            test "a pure async memo that swallows its violation and throws its own InvalidOperationException names createAsyncWith" {
                use g = new Graph ()
                use _ = g.Activate ()

                let a =
                    createAsync (fun _ ->
                        (try
                            onCleanup ignore
                         with _ ->
                             ())

                        raise (InvalidOperationException "user"))

                let ex = invalidOp (fun () -> a.Value |> ignore) "the flight fails"
                Expect.stringContains ex.Message "createAsyncWith" "the message names the owning async value"
            }

            test "a lookup f that swallows its violation and throws its own InvalidOperationException names the lookup" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let lookup =
                    createLookup
                        (fun (_: int) (k: int) ->
                            (try
                                onCleanup ignore
                             with _ ->
                                 ())

                            if k = 2 then
                                raise (InvalidOperationException "user")

                            true)
                        (fun prev next -> [ prev; next ])
                        (fun () -> s.Value)

                let ex = invalidOp (fun () -> lookup.Get 2 |> ignore) "f is pure"
                Expect.stringContains ex.Message "A lookup's f created an owned node" "the message names the lookup's rule"
            }

            test "an effect disposed by its own cleanup does not run again" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let mutable runs = 0
                let mutable self: Effect = Unchecked.defaultof<_>

                self <-
                    new Effect (
                        g,
                        fun () ->
                            runs <- runs + 1
                            let n = s.Value

                            onCleanup (fun () ->
                                if n = 0 then
                                    self.Dispose ())
                    )

                Expect.equal runs 1 "the first run"
                s.Value <- 1
                Expect.equal runs 1 "the disposed effect skips its body"
                Expect.equal s.ObserverCount 0 "the disposed effect holds no source edge"
            }

            test "an effect whose cleanup disposes its owner does not run again" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let mutable runs = 0

                createRoot (fun owner ->
                    createEffect (fun () ->
                        runs <- runs + 1
                        let n = s.Value

                        onCleanup (fun () ->
                            if n = 0 then
                                owner.Dispose ())))

                Expect.equal runs 1 "the first run"
                s.Value <- 1
                Expect.equal runs 1 "the disposed effect skips its body"
                Expect.equal s.ObserverCount 0 "the disposed effect holds no source edge"
            }
            test "a cleanup registered on a disposed scope inside an effect body runs untracked" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let other = createSignal 0
                let mutable runs = 0
                let mutable cleaned = 0

                createEffect (fun () ->
                    runs <- runs + 1
                    s.Value |> ignore

                    createRoot (fun o ->
                        o.Dispose ()

                        onCleanup (fun () ->
                            cleaned <- cleaned + 1
                            other.Value |> ignore))
                    |> ignore)

                Expect.equal cleaned 1 "the cleanup runs at once"
                Expect.equal other.ObserverCount 0 "the cleanup's read left no edge"
                other.Value <- 1
                Expect.equal runs 1 "a write to the cleanup's signal does not re-run the effect"
            }

            test "an effect that disposes itself and then registers a cleanup holds no source edge" {
                use g = new Graph ()
                use _ = g.Activate ()
                let u = createSignal 0
                let t = createSignal 0
                let mutable self: Effect = Unchecked.defaultof<_>

                self <-
                    new Effect (
                        g,
                        fun () ->
                            if u.Value = 1 then
                                self.Dispose ()
                                onCleanup (fun () -> t.Value |> ignore)
                    )

                u.Value <- 1
                Expect.equal t.ObserverCount 0 "the disposed effect holds no source edge"
                Expect.equal u.ObserverCount 0 "the disposed effect is detached"
            }

            test "Owner.OnCleanup on a disposed root inside an effect body runs untracked" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let other = createSignal 0
                let mutable runs = 0
                let mutable cleaned = 0

                createEffect (fun () ->
                    runs <- runs + 1
                    s.Value |> ignore

                    createRoot (fun o ->
                        o.Dispose ()

                        o.OnCleanup (fun () ->
                            cleaned <- cleaned + 1
                            other.Value |> ignore))
                    |> ignore)

                Expect.equal cleaned 1 "the cleanup runs at once"
                Expect.equal other.ObserverCount 0 "the cleanup's read left no edge"
                other.Value <- 1
                Expect.equal runs 1 "a write to the cleanup's signal does not re-run the effect"
            }

            test "Owner.OnCleanup on a disposed root owns what the cleanup creates" {
                use g = new Graph ()
                use _ = g.Activate ()
                let t = createSignal 0
                let mutable runs = 0
                let o = createRoot id
                o.Dispose ()

                o.OnCleanup (fun () ->
                    createEffect (fun () ->
                        runs <- runs + 1
                        t.Value |> ignore))

                Expect.equal t.ObserverCount 0 "the effect belongs to the disposed root"
                t.Value <- 1
                Expect.equal runs 0 "the effect never runs"
            }

            test "Owner.OnCleanup on a disposed root batches the cleanup's writes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 0
                let b = createSignal 0
                let mutable runs = 0

                createEffect (fun () ->
                    runs <- runs + 1
                    a.Value + b.Value |> ignore)

                let o = createRoot id
                o.Dispose ()

                o.OnCleanup (fun () ->
                    a.Value <- 1
                    b.Value <- 1)

                Expect.equal runs 2 "the effect runs once for both writes"
            }

            test "Owner.Attach on a disposed root disposes the child untracked" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let other = createSignal 0
                let mutable runs = 0

                createEffect (fun () ->
                    runs <- runs + 1
                    s.Value |> ignore

                    createRoot (fun o ->
                        o.Dispose ()

                        o.Attach
                            { new IDisposable with
                                member _.Dispose() =
                                    other.Value |> ignore
                            })
                    |> ignore)

                Expect.equal other.ObserverCount 0 "the child's read left no edge"
                other.Value <- 1
                Expect.equal runs 1 "a write to the child's signal does not re-run the effect"
            }

            test "Owner.Attach on a disposed root batches the child's writes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 0
                let b = createSignal 0
                let mutable runs = 0

                createEffect (fun () ->
                    runs <- runs + 1
                    a.Value + b.Value |> ignore)

                let o = createRoot id
                o.Dispose ()

                o.Attach
                    { new IDisposable with
                        member _.Dispose() =
                            a.Value <- 1
                            b.Value <- 1
                    }

                Expect.equal runs 2 "the effect runs once for both writes"
            }

            test "an effect that disposes itself in its body and then reads a source holds no edge" {
                use g = new Graph ()
                use _ = g.Activate ()
                let u = createSignal 0
                let t = createSignal 0
                let mutable self: Effect = Unchecked.defaultof<_>

                self <-
                    new Effect (
                        g,
                        fun () ->
                            if u.Value = 1 then
                                self.Dispose ()

                            t.Value |> ignore
                    )

                u.Value <- 1
                Expect.equal t.ObserverCount 0 "the disposed effect holds no edge to t"
                Expect.equal u.ObserverCount 0 "the disposed effect holds no edge to u"
            }

            test "a memo that disposes itself in its body and then reads a source holds no edge" {
                use g = new Graph ()
                use _ = g.Activate ()
                let u = createSignal 0
                let t = createSignal 0
                let mutable self: Memo<int> = Unchecked.defaultof<_>

                self <-
                    createMemo (fun _ ->
                        if u.Value = 1 then
                            self.Dispose ()

                        t.Value)

                self.Value |> ignore
                u.Value <- 1
                self.Value |> ignore
                Expect.equal t.ObserverCount 0 "the disposed memo holds no edge to t"
                Expect.equal u.ObserverCount 0 "the disposed memo holds no edge to u"
            }

            test "an async memo that disposes itself in its body and then reads a source holds no edge" {
                use g = new Graph ()
                use _ = g.Activate ()
                let u = createSignal 0
                let t = createSignal 0
                let mutable self: AsyncMemo<int> = Unchecked.defaultof<_>

                self <-
                    createAsync (fun _ ->
                        if u.Value = 1 then
                            self.Dispose ()

                        Task.FromResult t.Value)

                self.TryValue |> ignore
                u.Value <- 1
                self.TryValue |> ignore
                Expect.equal t.ObserverCount 0 "the disposed async memo holds no edge to t"
                Expect.equal u.ObserverCount 0 "the disposed async memo holds no edge to u"
            }

            test "a boundary that disposes itself in its body and then reads a source holds no edge" {
                use g = new Graph ()
                use _ = g.Activate ()
                let u = createSignal 0
                let t = createSignal 0
                let mutable self: Boundary<int> = Unchecked.defaultof<_>

                self <-
                    createErrorBoundary (fun _ _ -> -1) (fun () ->
                        if u.Value = 1 then
                            self.Dispose ()

                        t.Value)

                self.Value |> ignore
                u.Value <- 1
                self.Value |> ignore
                Expect.equal t.ObserverCount 0 "the disposed boundary holds no edge to t"
                Expect.equal u.ObserverCount 0 "the disposed boundary holds no edge to u"
            }

            test "a projection that disposes itself in its source and then reads a signal holds no edge" {
                use g = new Graph ()
                use _ = g.Activate ()
                let u = createSignal 0
                let t = createSignal 0
                let mutable self: Projection<int, int> = Unchecked.defaultof<_>

                self <-
                    createProjection id id (fun () ->
                        if u.Value = 1 then
                            self.Dispose ()

                        [ t.Value ])

                self.Keys |> ignore
                u.Value <- 1
                self.Keys |> ignore
                Expect.equal t.ObserverCount 0 "the disposed projection holds no edge to t"
                Expect.equal u.ObserverCount 0 "the disposed projection holds no edge to u"
            }
        ]
