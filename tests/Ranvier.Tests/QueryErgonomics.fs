module Ranvier.Tests.QueryErgonomics

open System
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Query
open Ranvier.Tests.QuerySupport

let private observe (pending: Task<'T>) =
    task {
        try
            let! value = pending
            return Result.Ok value
        with error ->
            return Result.Error error
    }

let private expectCancelled outcome =
    match outcome with
    | Result.Error _ -> ()
    | Result.Ok _ -> failtest "expected a cancelled waiter"

[<Tests>]
let tests =
    testList
        "QueryErgonomics"
        [
            testCaseAsync "default keys share requests and replacement is an edit"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let mutable calls = 0

                let family =
                    client.Define (fun (key: int) _ ->
                        calls <- calls + 1
                        completed key)

                use first = family.Acquire 1
                use second = family.Acquire 1
                let! value = first.EnsureAsync () |> awaitOutcome
                Expect.equal value 1 "loaded"
                Expect.equal calls 1 "same default key shares the fetch"
                let edit = family.SetIfLoaded (1, 42)
                Expect.equal first.Value 1 "describing an edit doesn't execute it"
                client.Commit [ edit ]
                Expect.equal second.Value 42 "shared replacement"
                let! cached = second.EnsureAsync () |> awaitOutcome
                Expect.equal cached 42 "fresh cached result"
                Expect.equal calls 1 "no second fetch"
            }
            testCaseAsync "explicit page owner survives a temporary view owner"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                use pageOwner = new Owner ()
                let family = client.Define (fun (key: int) _ -> completed key)

                let viewOwner, page =
                    createRoot (fun owner -> owner, family.AcquireOwned (1, pageOwner))

                let! _ = page.EnsureAsync () |> awaitOutcome
                viewOwner.Dispose ()
                Expect.equal page.Value 1 "page lifetime is independent of the view"
                pageOwner.Dispose ()
                Expect.throws (fun () -> page.Value |> ignore) "page disposed its lease"
                Expect.throws (fun () -> family.AcquireOwned (2, pageOwner) |> ignore) "disposed owners reject acquisition"
            }
            testCaseAsync "awaiters join the current request and see published data"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let mutable calls = 0

                let family =
                    client.Define (fun () _ ->
                        calls <- calls + 1
                        reply.Task)

                use page = family.Acquire ()
                let first = page.EnsureAsync ()
                let second = page.EnsureAsync ()
                Expect.equal calls 1 "both waiters join acquisition"
                reply.SetResult 7
                let! accepted = first |> awaitOutcome
                Expect.equal page.Value accepted "publication precedes completion"
                let! other = second |> awaitOutcome
                Expect.equal other 7 "other waiter also completes"
            }
            testCaseAsync "refresh supersedes and cancels older waiters"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let first = TaskCompletionSource<int>()
                let next = TaskCompletionSource<int>()
                let mutable calls = 0

                let family =
                    client.Define (fun () _ ->
                        calls <- calls + 1
                        if calls = 1 then first.Task else next.Task)

                use page = family.Acquire ()
                let old = page.EnsureAsync () |> observe
                let latest = page.RefreshAsync ()
                let! retired = old |> awaitOutcome
                expectCancelled retired
                next.SetResult 20
                let! accepted = latest |> awaitOutcome
                Expect.equal accepted 20 "new request result"
                Expect.equal page.Value 20 "published before continuation"
                first.SetResult 10
                do! Async.Sleep 5
                Expect.equal page.Value 20 "late response rejected"
            }
            testCaseAsync "disposing one lease cancels only its own waiters"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let mutable token = System.Threading.CancellationToken.None

                let family =
                    client.Define (fun () ct ->
                        token <- ct
                        reply.Task)

                let first = family.Acquire ()
                use second = family.Acquire ()
                let closed = first.EnsureAsync () |> observe
                let retained = second.EnsureAsync ()
                first.Dispose ()
                let! outcome = closed |> awaitOutcome
                expectCancelled outcome
                Expect.isFalse token.IsCancellationRequested "remaining page owns the shared request"
                reply.SetResult 9
                let! value = retained |> awaitOutcome
                Expect.equal value 9 "retained page still loads"
            }
            testCaseAsync "refresh failure faults the waiter and retains accepted data"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let mutable calls = 0

                let family =
                    client.Define (fun () _ ->
                        calls <- calls + 1
                        if calls = 1 then completed 1 else reply.Task)

                use page = family.Acquire ()
                let! _ = page.EnsureAsync () |> awaitOutcome
                let pending = page.RefreshAsync () |> observe
                reply.SetException (InvalidOperationException "offline")
                let! outcome = pending |> awaitOutcome

                match outcome with
                | Result.Error error -> Expect.equal error.Message "offline" "fetch failure reaches caller"
                | _ -> failtest "expected fault"

                Expect.equal page.Value 1 "previous accepted record retained"
                Expect.isTrue page.State.IsStale "refresh can be retried explicitly"
            }
            testCaseAsync "reconciliation cancels pending awaiters after publishing replacement"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let mutable calls = 0

                let family =
                    client.Define (fun () _ ->
                        calls <- calls + 1
                        if calls = 1 then completed 1 else reply.Task)

                use page = family.Acquire ()
                let! _ = page.EnsureAsync () |> awaitOutcome
                let pending = page.RefreshAsync () |> observe
                client.Commit [ family.SetIfLoaded ((), 42) ]
                let! retired = pending |> awaitOutcome
                expectCancelled retired
                Expect.equal page.Value 42 "replacement already published"
                reply.SetResult 99
                do! Async.Sleep 5
                Expect.equal page.Value 42 "old request cannot overwrite save"
            }
            testCaseAsync "invalidation cancels pending awaiters without fetching"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let mutable calls = 0

                let family =
                    client.Define (fun () _ ->
                        calls <- calls + 1
                        TaskCompletionSource<int>().Task)

                use page = family.Acquire ()
                let pending = page.EnsureAsync () |> observe
                client.Commit [ family.Invalidate () ]
                let! retired = pending |> awaitOutcome
                expectCancelled retired
                Expect.equal calls 1 "invalidation isn't demand"
                Expect.isTrue page.State.IsStale "returning page must ensure again"
            }
            testCaseAsync "client disposal cancels outstanding load waiters"
            <| async {
                use graph = newGraph ()
                let client = new QueryClient (graph)
                let family = client.Define (fun () _ -> TaskCompletionSource<int>().Task)
                use page = family.Acquire ()
                let pending = page.EnsureAsync () |> observe
                client.Dispose ()
                let! outcome = pending |> awaitOutcome
                expectCancelled outcome
            }
            testCaseAsync "client disposal during publication cancels the load result"
            <| async {
                use graph = newGraph ()
                use client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let family = client.Define (fun () _ -> reply.Task)
                use page = family.Acquire ()
                let pending = page.EnsureAsync () |> observe

                use close =
                    new Effect (
                        graph,
                        fun () ->
                            if page.State.Data.IsSome then
                                client.Dispose ()
                    )

                reply.SetResult 42
                let! outcome = pending |> awaitOutcome
                expectCancelled outcome
            }
        ]
