module Ranvier.Tests.QueryReconciliation

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Query
open Ranvier.Tests.QuerySupport

[<Tests>]
let tests =
    testList
        "QueryReconciliation"
        [
            testCaseAsync "equal patches retire requests and failed commits preserve requests"
            <| async {
                use graph = newGraph ()
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
                replies[0].SetResult 10
                do! waitUntil (fun () -> query.State.Data.IsSome)
                query.Refresh ()
                Expect.throws (fun () -> client.Commit [ numbers.UpdateIfLoaded (1, fun _ -> failwith "bad") ]) "staging failed"
                replies[1].SetResult 20
                do! waitUntil (fun () -> query.State.Data = Some 20)
                query.Refresh ()
                client.Commit [ numbers.UpdateIfLoaded (1, id) ]
                replies[2].SetResult 99
                do! Async.Sleep 5
                Expect.equal query.Value 20 "even equal patches retire old responses"
                Expect.isFalse query.State.IsStale "patched value accepted"
            }
            testCaseAsync "throwing data comparisons happen before any publication"
            <| async {
                let mutable reject = false
                let mutable comparisons = 0

                let policy =
                    { new IEqualityPolicy with
                        member _.Comparer<'T>() =
                            { new IEqualityComparer<'T> with
                                member _.Equals(a, b) =
                                    comparisons <- comparisons + 1

                                    if reject then
                                        failwith "comparison"

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

                let numbers =
                    client.Define (EqualityComparer<int>.Default, fun key _ -> completed key)

                use left = numbers.Acquire 1
                use right = numbers.Acquire 2
                do! waitUntil (fun () -> left.State.Data.IsSome && right.State.Data.IsSome)
                reject <- true

                Expect.throws
                    (fun () ->
                        client.Commit
                            [
                                numbers.UpdateIfLoaded (1, fun _ -> 10)
                                numbers.UpdateIfLoaded (2, fun _ -> 20)
                            ])
                    "comparison failed"

                Expect.equal (left.Value, right.Value) (1, 2) "no writes before comparisons finish"
                client.Commit [ numbers.Invalidate 1 ]
                Expect.isTrue left.State.IsStale "invalidation never invokes data comparer"
                reject <- false
                comparisons <- 0
                client.Commit [ numbers.UpdateIfLoaded (1, fun _ -> 10) ]
                Expect.equal comparisons 1 "one comparison during preparation, none during publication"
            }
            testCaseAsync "staging failure is atomic and repeated edits compose"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)

                let numbers =
                    client.Define (EqualityComparer<int>.Default, fun key _ -> completed key)

                use left = numbers.Acquire 1
                use right = numbers.Acquire 2
                do! waitUntil (fun () -> left.State.Data.IsSome && right.State.Data.IsSome)

                Expect.throws
                    (fun () ->
                        client.Commit
                            [
                                numbers.UpdateIfLoaded (1, fun n -> n + 10)
                                numbers.UpdateIfLoaded (2, fun _ -> failwith "bad")
                            ])
                    "failed staging"

                Expect.equal (left.Value, right.Value) (1, 2) "no partial data"
                let seen = ResizeArray<int * int>()
                createEffect (fun () -> seen.Add (left.Value, right.Value))

                client.Commit
                    [
                        numbers.UpdateIfLoaded (1, fun n -> n + 1)
                        numbers.UpdateIfLoaded (1, fun n -> n * 2)
                        numbers.UpdateIfLoaded (2, fun n -> n + 10)
                    ]

                Expect.sequenceEqual seen [ (1, 2); (4, 12) ] "single atomic publication"

                Expect.throws
                    (fun () ->
                        client.Commit
                            [
                                numbers.UpdateIfLoaded (
                                    1,
                                    fun _ ->
                                        right.Refresh ()
                                        99
                                )
                            ])
                    "reentry rejected"

                Expect.equal left.Value 4 "reentry rollback"
            }
            testCaseAsync "empty pending entry retires while absent key stays absent"
            <| async {
                use graph = newGraph ()
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
                let mutable updates = 0

                client.Commit
                    [
                        numbers.UpdateIfLoaded (
                            1,
                            fun n ->
                                updates <- updates + 1
                                n
                        )
                        numbers.UpdateIfLoaded (
                            2,
                            fun n ->
                                updates <- updates + 1
                                n
                        )
                    ]

                Expect.equal updates 0 "no data means no updater"
                Expect.equal query.State.FetchStatus FetchStatus.Idle "pending request retired"
                replies[0].SetResult 10
                do! Async.Sleep 5
                Expect.equal query.State.Data None "old response cannot publish"
                query.Ensure ()
                replies[1].SetResult 20
                do! waitUntil (fun () -> query.State.Data.IsSome)
                use absent = numbers.Acquire 2
                Expect.equal replies.Count 3 "absent edit allocated no entry"
            }
            testCaseAsync "predicate failure and foreign edits leave all entries untouched"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                use other = new QueryClient (graph)

                let numbers =
                    client.Define (EqualityComparer<int>.Default, fun key _ -> completed key)

                let foreign =
                    other.Define (EqualityComparer<int>.Default, fun key _ -> completed key)

                use query = numbers.Acquire 1
                do! waitUntil (fun () -> query.State.Data.IsSome)

                Expect.throws
                    (fun () ->
                        client.Commit
                            [
                                numbers.UpdateIfLoaded (1, fun _ -> 9)
                                numbers.InvalidateWhere (fun _ -> failwith "predicate")
                            ])
                    "predicate failure"

                Expect.throws (fun () -> client.Commit [ numbers.UpdateIfLoaded (1, fun _ -> 9); foreign.Invalidate 1 ]) "foreign client"
                Expect.equal query.Value 1 "no accepted data changes"
                Expect.isFalse query.State.IsStale "no invalidation on failure"
            }
            testCaseAsync "invalidation keeps data and waits for explicit demand"
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

                use left = numbers.Acquire 1
                use right = numbers.Acquire 2
                do! waitUntil (fun () -> left.State.Data.IsSome && right.State.Data.IsSome)
                client.Commit [ numbers.InvalidateWhere (fun key -> key = 1) ]
                Expect.equal (left.Value, right.Value) (1, 2) "retained data"
                Expect.equal calls 2 "invalidation does no IO"
                Expect.isTrue left.State.IsStale "matching key"
                Expect.isFalse right.State.IsStale "unrelated key"
                left.Ensure ()
                do! waitUntil (fun () -> not left.State.IsStale)
                Expect.equal calls 3 "explicit demand refreshes"
            }
        ]
