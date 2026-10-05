module Ranvier.Tests.Queries

open System
open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Query
open Ranvier.Tests.QuerySupport

#if !FABLE_COMPILER
[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private releasedPayload (family: QueryFamily<int, byte array>) =
    let lease = family.Acquire 1
    let reference = System.WeakReference (lease.Value)
    lease.Dispose ()
    reference
#endif

[<Tests>]
let tests =
    testList
        "QueryFamilies"
        [
            testCase "one hundred definitions perform no IO"
            <| fun () ->
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let mutable calls = 0

                for _ in 1..100 do
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun key _ ->
                            calls <- calls + 1
                            completed key
                    )
                    |> ignore

                Expect.equal calls 0 "definitions are descriptions"
#if !FABLE_COMPILER
            testCase "released root lease does not retain its cached payload"
            <| fun () ->
#if RANVIER_TRACE
                skiptest "Trace history intentionally retains published values."
#else
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)

                let family =
                    client.Define (EqualityComparer<int>.Default, fun _ _ -> completed (Array.zeroCreate<byte> 100000))

                let reference = releasedPayload family
                System.GC.Collect ()
                System.GC.WaitForPendingFinalizers ()
                System.GC.Collect ()
                Expect.isFalse reference.IsAlive "root cleanup registration retains no disposed payload"
                System.GC.KeepAlive client
                System.GC.KeepAlive graph
#endif
            testCase "throwing cancellation does not stop sibling retirement during graph disposal"
            <| fun () ->
                let graph = newGraph ()
                use active = graph.Activate ()
                let client = new QueryClient (graph)
                let tokens = ResizeArray<System.Threading.CancellationToken>()

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun key ct ->
                            tokens.Add ct

                            if key = 1 then
                                ct.Register (fun () -> failwith "cancel")
                                |> ignore

                            TaskCompletionSource<int>().Task
                    )

                family.Acquire 1 |> ignore
                family.Acquire 2 |> ignore

                try
                    graph.Dispose ()
                with _ ->
                    ()

                Expect.isTrue
                    (tokens
                     |> Seq.forall (fun token -> token.IsCancellationRequested))
                    "all requests retired"
            testCase "worker completion waits for graph dispatch"
            <| fun () ->
                use posted = new System.Threading.ManualResetEventSlim (false)

                let dispatcher =
                    { new IGraphDispatcher with
                        member _.Post _ =
                            posted.Set ()
                    }

                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some dispatcher
                        }
                    )

                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let numbers = client.Define (EqualityComparer<int>.Default, fun _ _ -> reply.Task)
                use query = numbers.Acquire 1
                let worker = System.Threading.Thread (fun () -> reply.SetResult 10)
                worker.Start ()
                worker.Join ()
                Expect.isTrue (posted.Wait 5000) "completion dispatched"
                Expect.equal query.State.Data None "not applied off thread"
                graph.Pump () |> ignore
                Expect.equal query.Value 10 "applied on graph thread"
#endif
            testCaseAsync "comparer refresh reentry retires the completion being compared"
            <| async {
                let mutable callback = ignore

                let policy =
                    { new IEqualityPolicy with
                        member _.Comparer<'T>() =
                            { new IEqualityComparer<'T> with
                                member _.Equals(a, b) =
                                    callback ()
                                    EqualityComparer<'T>.Default.Equals(a, b)

                                member _.GetHashCode(value) =
                                    EqualityComparer<'T>.Default.GetHashCode value
                            }
                    }

                use graph =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = policy
                            ThreadAffinity = ThreadAffinity.Unchecked
                            Dispatcher = Some (ImmediateDispatcher ())
                        }
                    )

                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let replies = ResizeArray<TaskCompletionSource<int>>()

                let numbers =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun _ _ ->
                            let reply = TaskCompletionSource<int>() in
                            replies.Add reply
                            reply.Task
                    )

                use query = numbers.Acquire 1
                replies[0].SetResult 1
                do! waitUntil (fun () -> query.State.Data.IsSome)
                query.Refresh ()

                callback <-
                    fun () ->
                        callback <- ignore
                        query.Refresh ()

                replies[1].SetResult 2
                do! waitUntil (fun () -> replies.Count = 3)
                Expect.equal query.Value 1 "retired result was not accepted"
                replies[2].SetResult 3
                do! waitUntil (fun () -> query.State.Data = Some 3)
            }
            testCaseAsync "same family key shares a request and independent leases"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let mutable calls = 0

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun key _ ->
                            calls <- calls + 1
                            completed (key * 10)
                    )

                Expect.equal calls 0 "definition does not load"
                use first = family.Acquire 7
                use second = family.Acquire 7
                do! waitUntil (fun () -> second.State.Data.IsSome)
                Expect.equal calls 1 "one shared request"
                first.Dispose ()
                Expect.equal second.Value 70 "second lease survives"
                second.Dispose ()
                use third = family.Acquire 7
                do! waitUntil (fun () -> third.State.Data.IsSome)
                Expect.equal calls 2 "last release evicts"
            }
            testCaseAsync "unit keys and custom equality deduplicate"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let mutable calls = 0

                let singleton =
                    client.Define (EqualityComparer<unit>.Default, fun () _ -> completed 42)

                use index = singleton.Acquire ()
                do! waitUntil (fun () -> index.State.Data.IsSome)
                Expect.equal index.Value 42 "unit key"

                let keys =
                    { new IEqualityComparer<string> with
                        member _.Equals(a, b) =
                            a.ToLowerInvariant () = b.ToLowerInvariant ()

                        member _.GetHashCode(a) =
                            hash (a.ToLowerInvariant ())
                    }

                let family =
                    client.Define (
                        keys,
                        fun key _ ->
                            calls <- calls + 1
                            completed key
                    )

                use first = family.Acquire "Ada"
                use second = family.Acquire "ADA"
                do! waitUntil (fun () -> second.State.Data.IsSome)
                Expect.equal calls 1 "custom key equality"
            }
            testCaseAsync "families and clients isolate identical keys"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use left = new QueryClient (graph)
                use right = new QueryClient (graph)

                let define (client: QueryClient) value =
                    client.Define (EqualityComparer<int>.Default, fun _ _ -> completed value)

                use a = (define left 1).Acquire 7
                use b = (define left 2).Acquire 7
                use c = (define right 3).Acquire 7

                do!
                    waitUntil (fun () ->
                        a.State.Data.IsSome
                        && b.State.Data.IsSome
                        && c.State.Data.IsSome)

                Expect.equal (a.Value, b.Value, c.Value) (1, 2, 3) "separate identities"
            }
            testCaseAsync "client disposal cancels requests and lease disposal remains idempotent"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                let client = new QueryClient (graph)
                let mutable token = System.Threading.CancellationToken.None
                let reply = TaskCompletionSource<int>()

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun _ ct ->
                            token <- ct
                            reply.Task
                    )

                let query = family.Acquire 1
                client.Dispose ()
                Expect.isTrue token.IsCancellationRequested "in-flight request cancelled"
                query.Dispose ()
                query.Dispose ()
                Expect.throws (fun () -> query.Value |> ignore) "disposed lease"
                reply.SetResult 10
                do! Async.Sleep 1
            }
            testCaseAsync "page owner disposal releases just its lease"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let family = client.Define (EqualityComparer<int>.Default, fun _ _ -> completed 9)
                let page, first = createRoot (fun owner -> owner, family.Acquire 1)
                use second = family.Acquire 1
                do! waitUntil (fun () -> second.State.Data.IsSome)
                page.Dispose ()
                Expect.throws (fun () -> first.Value |> ignore) "page lease closed"
                Expect.equal second.Value 9 "shared resource remains"
            }
            testCaseAsync "refresh ignores retired success and failure"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let replies = ResizeArray<TaskCompletionSource<int>>()

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun _ _ ->
                            let reply = TaskCompletionSource<int>()
                            replies.Add reply
                            reply.Task
                    )

                use query = family.Acquire 1
                query.Ensure ()
                Expect.equal replies.Count 1 "ensure joins first fetch"
                query.Refresh ()
                replies[1].SetResult 20
                do! waitUntil (fun () -> query.State.Data = Some 20)
                replies[0].SetException(exn "old fault")
                do! Async.Sleep 5
                Expect.equal query.State.Error None "old fault discarded"
                query.Refresh ()
                query.Refresh ()
                replies[3].SetResult 40
                do! waitUntil (fun () -> query.State.Data = Some 40)
                replies[2].SetResult 30
                do! Async.Sleep 5
                Expect.equal query.Value 40 "old success discarded"
            }
            testCaseAsync "refresh retains data and only wakes state readers"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let mutable calls = 0
                let reply = TaskCompletionSource<int>()

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun _ _ ->
                            calls <- calls + 1
                            if calls = 1 then completed 10 else reply.Task
                    )

                use query = family.Acquire 1
                do! waitUntil (fun () -> query.State.Data.IsSome)
                let mutable values = 0
                let mutable states = 0

                createEffect (fun () ->
                    query.Value |> ignore
                    values <- values + 1)

                createEffect (fun () ->
                    query.State |> ignore
                    states <- states + 1)

                query.Refresh ()
                Expect.equal query.Value 10 "stale value stays readable"
                Expect.equal values 1 "fetch activity doesn't change data"
                reply.SetException (exn "offline")
                do! waitUntil (fun () -> query.State.Error.IsSome)
                Expect.equal query.Value 10 "failure retains data"
                Expect.equal values 1 "failure only changes metadata"
                Expect.isGreaterThan states 1 "state tracks refresh"
                query.State |> ignore
                query.Value |> ignore
                Expect.equal calls 2 "reads never retry"
            }
            testCaseAsync "evicted pending entry cannot affect reacquired key"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let replies = ResizeArray<TaskCompletionSource<int>>()

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun _ _ ->
                            let reply = TaskCompletionSource<int>()
                            replies.Add reply
                            reply.Task
                    )

                let old = family.Acquire 1
                old.Dispose ()
                use fresh = family.Acquire 1
                replies[1].SetResult 2
                do! waitUntil (fun () -> fresh.State.Data.IsSome)
                replies[0].SetResult 1
                do! Async.Sleep 5
                Expect.equal fresh.Value 2 "old identity discarded"
            }
            testCase "synchronous throws and null query tasks are request failures"
            <| fun () ->
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)

                use thrown =
                    (client.Define (EqualityComparer<int>.Default, fun _ _ -> failwith "sync")).Acquire 1

                use missing =
                    (client.Define (EqualityComparer<int>.Default, fun _ _ -> Unchecked.defaultof<Task<int>>)).Acquire 1

                Expect.equal thrown.State.Error.Value.Message "sync" "synchronous failure"
                Expect.isTrue missing.State.Error.IsSome "null task failure"
            testCaseAsync "cancellation reentry cannot give two requests the same generation"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let replies = ResizeArray<TaskCompletionSource<int>>()
                let mutable onCancel = ignore

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun _ ct ->
                            let reply = TaskCompletionSource<int>()
                            replies.Add reply

                            if replies.Count = 1 then
                                ct.Register (fun () -> onCancel ()) |> ignore

                            reply.Task
                    )

                use query = family.Acquire 1
                onCancel <- query.Refresh
                query.Refresh ()
                Expect.equal replies.Count 2 "recursive refresh supersedes outer refresh"
                replies[1].SetResult 2
                do! waitUntil (fun () -> query.State.Data = Some 2)
            }
        ]
