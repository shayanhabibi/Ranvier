module Ranvier.Tests.QueryMutations

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Query
open Ranvier.Tests.QuerySupport

[<Tests>]
let tests =
    testList
        "QueryMutations"
        [
            testCaseAsync "outcome continuation joins the existing client FIFO"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let starts = ResizeArray<int>()
                let reply = TaskCompletionSource<int>()

                let execute input _ =
                    starts.Add input
                    if input = 1 then reply.Task else completed input

                let first = client.Mutate (1, execute, fun _ -> [])
                let next = client.Mutate (2, execute, fun _ -> [])

                let chained =
                    task {
                        let! _ = first
                        return! client.Mutate (3, execute, fun _ -> [])
                    }

                reply.SetResult 1
                let! _ = awaitOutcome next
                let! outcome = awaitOutcome chained
                Expect.equal outcome (MutationOutcome.Applied 3) "continuation completed"
                Expect.sequenceEqual starts [ 1; 2; 3 ] "new work joins behind admitted work"
            }
            testCaseAsync "closing editor leaves client save alive and Home isolates a new session"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                let client = new QueryClient (graph)

                let numbers =
                    client.Define (EqualityComparer<int>.Default, fun key _ -> completed key)

                use query = numbers.Acquire 1
                do! waitUntil (fun () -> query.State.Data.IsSome)
                let reply = TaskCompletionSource<int>()

                let editor, pending =
                    createRoot (fun owner ->
                        owner, client.Mutate ((), (fun () _ -> reply.Task), fun value -> [ numbers.UpdateIfLoaded (1, fun _ -> value) ]))

                editor.Dispose ()
                reply.SetResult 10
                let! outcome = awaitOutcome pending
                Expect.equal outcome (MutationOutcome.Applied 10) "page closure doesn't own save"
                Expect.equal query.Value 10 "retained page reconciled"
                let late = TaskCompletionSource<int>()

                let oldSave =
                    client.Mutate ((), (fun () _ -> late.Task), fun value -> [ numbers.UpdateIfLoaded (1, fun _ -> value) ])

                let observed =
                    task {
                        try
                            let! _ = oldSave in return ()
                        with _ ->
                            return ()
                    }

                client.Dispose ()
                use fresh = new QueryClient (graph)

                let freshNumbers =
                    fresh.Define (EqualityComparer<int>.Default, fun _ _ -> completed 100)

                use home = freshNumbers.Acquire 1
                do! waitUntil (fun () -> home.State.Data.IsSome)
                late.SetResult 99
                do! awaitOutcome observed
                do! Async.Sleep 5
                Expect.equal home.Value 100 "new session unaffected"
            }
            testCaseAsync "reconciliation reentry fails locally and queue continues"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)

                let numbers =
                    client.Define (EqualityComparer<int>.Default, fun key _ -> completed key)

                use query = numbers.Acquire 1
                do! waitUntil (fun () -> query.State.Data.IsSome)

                let first =
                    client.Mutate (
                        (),
                        (fun () _ -> completed 10),
                        fun _ ->
                            query.Refresh ()
                            []
                    )

                let next =
                    client.Mutate ((), (fun () _ -> completed 20), fun value -> [ numbers.UpdateIfLoaded (1, fun _ -> value) ])

                let! outcome = awaitOutcome first

                match outcome with
                | MutationOutcome.ReconciliationFailed (10, _) -> ()
                | _ -> failtest "expected local failure"

                let! nextOutcome = awaitOutcome next
                Expect.equal nextOutcome (MutationOutcome.Applied 20) "queue continues"
                Expect.equal query.Value 20 "next write published"
            }
            testCaseAsync "heterogeneous writes serialize through reconciliation"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let starts = ResizeArray<int>()
                let reply = TaskCompletionSource<int>()

                let numbers =
                    client.Define (EqualityComparer<unit>.Default, fun () _ -> completed 0)

                use query = numbers.Acquire ()
                do! waitUntil (fun () -> query.State.Data.IsSome)

                let first =
                    client.Mutate (
                        1,
                        (fun input _ ->
                            starts.Add input
                            reply.Task),
                        fun value -> [ numbers.UpdateIfLoaded ((), fun _ -> value) ]
                    )

                let second =
                    client.Mutate (
                        "second",
                        (fun _ _ ->
                            starts.Add 2
                            Expect.equal query.Value 1 "prior write visible before next request"
                            completed "ok"),
                        fun _ -> [ numbers.UpdateIfLoaded ((), fun _ -> 2) ]
                    )

                Expect.sequenceEqual starts [ 1 ] "one in flight"
                reply.SetResult 1
                let! secondOutcome = awaitOutcome second
                let! firstOutcome = awaitOutcome first
                Expect.equal firstOutcome (MutationOutcome.Applied 1) "first result"
                Expect.equal secondOutcome (MutationOutcome.Applied "ok") "different result type"
                Expect.sequenceEqual starts [ 1; 2 ] "FIFO"
                Expect.equal query.Value 2 "final authoritative data"
            }
            testCaseAsync "remote success with failed reconciliation retains receipt and invalidates"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let mutable calls = 0

                let numbers =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun key _ ->
                            calls <- calls + 1
                            completed key
                    )

                use query = numbers.Acquire 1
                do! waitUntil (fun () -> query.State.Data.IsSome)

                let result =
                    client.Mutate ((), (fun () _ -> completed 42), fun _ -> [ numbers.UpdateIfLoaded (1, fun _ -> failwith "bad") ])

                let! outcome = awaitOutcome result

                match outcome with
                | MutationOutcome.ReconciliationFailed (42, error) -> Expect.equal error.Message "bad" "local failure"
                | _ -> failtest "expected saved receipt"

                Expect.equal query.Value 1 "accepted data unchanged"
                Expect.isTrue query.State.IsStale "conservative invalidation"
                Expect.equal calls 1 "no automatic IO"
            }
            testCaseAsync "failed request skips reconciliation and allows next write"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let mutable reconciled = false

                let first =
                    client.Mutate (
                        (),
                        (fun () _ -> failwith "remote": Task<int>),
                        fun _ ->
                            reconciled <- true
                            []
                    )

                let next = client.Mutate ((), (fun () _ -> completed 9), fun _ -> [])
                let! nextOutcome = awaitOutcome next
                let! firstOutcome = awaitOutcome first

                match firstOutcome with
                | MutationOutcome.RequestFailed error -> Expect.equal error.Message "remote" "request failure"
                | _ -> failtest "wrong outcome"

                Expect.isFalse reconciled "not called"
                Expect.equal nextOutcome (MutationOutcome.Applied 9) "queue continues"
            }
            testCaseAsync "client disposal cancels callers and never starts queued requests"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                let client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let mutable token = System.Threading.CancellationToken.None
                let mutable starts = 0
                let mutable reconciled = false

                let first =
                    client.Mutate (
                        (),
                        (fun () ct ->
                            token <- ct
                            reply.Task),
                        fun _ ->
                            reconciled <- true
                            []
                    )

                let next =
                    client.Mutate (
                        (),
                        (fun () _ ->
                            starts <- starts + 1
                            completed 2),
                        fun _ -> []
                    )

                let observe (pending: Task<_>) =
                    task {
                        try
                            let! _ = pending
                            return false
                        with _ ->
                            return true
                    }

                let firstObserved = observe first
                let nextObserved = observe next
                client.Dispose ()
                let! firstCancelled = awaitOutcome firstObserved
                let! nextCancelled = awaitOutcome nextObserved
                Expect.isTrue firstCancelled "active caller cancelled"
                Expect.isTrue nextCancelled "queued caller cancelled"
                Expect.isTrue token.IsCancellationRequested "remote cancellation requested"
                reply.SetResult 1
                do! Async.Sleep 5
                Expect.equal starts 0 "queued delegate not started"
                Expect.isFalse reconciled "late result ignored"
            }
        ]
