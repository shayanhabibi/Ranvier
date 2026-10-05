module Ranvier.Tests.QueryComposition

open System.Collections.Generic
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Query
open Ranvier.Tests.QuerySupport

[<Tests>]
let tests =
    testList
        "QueryComposition"
        [
            testCaseAsync "initial query value suspends and settles through a boundary"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let reply = TaskCompletionSource<int>()
                let family = client.Define (EqualityComparer<int>.Default, fun _ _ -> reply.Task)
                use query = family.Acquire 1
                let shown = createBoundary (fun _ -> -1) (fun _ _ -> -2) (fun () -> query.Value)
                Expect.equal shown.Value -1 "pending boundary"
                Expect.equal query.State.Data None "state read remains available"
                reply.SetResult 42
                do! waitUntil (fun () -> query.State.Data.IsSome)
                Expect.equal shown.Value 42 "settled boundary"
            }

            testCaseAsync "failed initial query can explicitly retry"
            <| async {
                use graph = newGraph ()
                use active = graph.Activate ()
                use client = new QueryClient (graph)
                let mutable calls = 0

                let family =
                    client.Define (
                        EqualityComparer<int>.Default,
                        fun _ _ ->
                            calls <- calls + 1

                            if calls = 1 then
                                faulted<int>(exn "offline")
                            else
                                completed 12
                    )

                use query = family.Acquire 1
                do! waitUntil (fun () -> query.State.Error.IsSome)
                let shown = createBoundary (fun _ -> -1) (fun _ _ -> -2) (fun () -> query.Value)
                Expect.equal shown.Value -2 "failed boundary"
                query.Refresh ()
                do! waitUntil (fun () -> query.State.Data.IsSome)
                Expect.equal shown.Value 12 "retry clears failure"
            }
        ]
