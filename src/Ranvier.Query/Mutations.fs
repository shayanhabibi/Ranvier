namespace Ranvier.Query

open System
open System.Threading
open System.Threading.Tasks
open Fable.Core

/// <summary>Client-owned remote writes and their atomic query reconciliation.</summary>
[<AutoOpen>]
module QueryMutationExtensions =
    let private observeRequest (graph: Ranvier.Graph) (remote: Task<'T>) (apply: Result<'T, exn> -> unit) =
        task {
            let! outcome =
                task {
                    try
                        let! value = remote
                        return Result.Ok value
                    with error ->
                        return Result.Error error
                }

            graph.Dispatch (fun () -> apply outcome)
        }
        |> ignore

    /// <summary>Settles cancellation using each target's completion-source spelling.</summary>
    [<Emit("$0.SetCancelled()")>]
    let private cancelResult (source: TaskCompletionSource<'T>) =
        source.SetCanceled ()

    type QueryClient with
        /// <summary>Queues one remote write, then publishes its edits before the next write starts.</summary>
        /// <remarks>A reconciliation failure retains the saved receipt and invalidates cached queries. No write is retried.</remarks>
        member client.Mutate(input: 'Input, execute: 'Input -> CancellationToken -> Task<'Result>, reconcile: 'Result -> QueryEdit list) =
            let graph = client.Graph
            let result = TaskCompletionSource<MutationOutcome<'Result>>()
            let cancellation = new CancellationTokenSource ()
            let mutable cancelled = false
            let mutable terminal = false

            let operation =
                { new IQueryMutation with
                    member _.Cancel() =
                        if not terminal then
                            terminal <- true
                            cancelled <- true
                            cancelResult result

                            try
                                cancellation.Cancel ()
                            with error ->
                                graph.Root.OnCleanup (fun () -> raise error)

                            cancellation.Dispose ()

                    member _.Start(finished) =
                        let apply outcome =
                            if not cancelled && not client.IsDisposed then
                                let accepted =
                                    match outcome with
                                    | Result.Error error -> MutationOutcome.RequestFailed error
                                    | Result.Ok value ->
                                        try
                                            client.Reconcile (fun () -> reconcile value)
                                            MutationOutcome.Applied value
                                        with error ->
                                            if not client.IsDisposed then
                                                client.InvalidateAll ()

                                            MutationOutcome.ReconciliationFailed (value, error)

                                if not cancelled then
                                    terminal <- true
                                    result.SetResult accepted

                                cancellation.Dispose ()
                                finished ()

                        let request =
                            try
                                let remote = graph.Untrack (fun () -> execute input cancellation.Token)

                                if isNull remote then
                                    invalidOp "The mutation execute function returned a null task."

                                Result.Ok remote
                            with error ->
                                Result.Error error

                        match request with
                        | Result.Error error -> apply (Result.Error error)
                        | Result.Ok remote -> observeRequest graph remote apply
                }

            try
                client.Enqueue operation
            with error ->
                cancellation.Dispose ()
                raise error

            result.Task
