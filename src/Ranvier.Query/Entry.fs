namespace Ranvier.Query

open System
open System.Threading
open System.Threading.Tasks
open Ranvier

/// <summary>Cached data, request state, and the initial-load suspension source for an entry.</summary>
type internal QueryEntry<'T>(graph: Graph, fetch: CancellationToken -> Task<'T>, guard: unit -> unit) =
    let source initial =
        graph.Run (fun () -> createSignalWithComparer (PublicationComparer<_>()) initial)

    let data = source None
    let metadata = source (FetchStatus.Idle, None, true)
    let gate = source (AsyncSource<'T>(graph))
    let comparer = graph.Options.Equality.Comparer<'T>()
    let mutable generation = 0L
    let mutable disposed = false
    let mutable cancellation: CancellationTokenSource option = None

    let cancel () =
        let old = cancellation
        cancellation <- None

        match old with
        | None -> ()
        | Some cts ->
            try
                cts.Cancel ()
            with error ->
                graph.Root.OnCleanup (fun () -> raise error)

            cts.Dispose ()

    let publish current outcome =
        if not disposed && generation = current then
            match outcome with
            | Result.Ok value ->
                try
                    let initial = data.Peek.IsNone

                    let changed =
                        match data.Peek with
                        | None -> true
                        | Some old -> not (comparer.Equals (old, value))

                    if not disposed && generation = current then
                        graph.Batch (fun () ->
                            if changed then
                                data.Value <- Some value

                            metadata.Value <- FetchStatus.Idle, None, false

                            if initial then
                                gate.Peek.Settle value)
                with error ->
                    if not disposed && generation = current then
                        graph.Batch (fun () ->
                            metadata.Value <- FetchStatus.Idle, Some error, true

                            if data.Peek.IsNone then
                                gate.Peek.Fail error)
            | Result.Error error ->
                graph.Batch (fun () ->
                    metadata.Value <- FetchStatus.Idle, Some error, true

                    if data.Peek.IsNone then
                        gate.Peek.Fail error)

    let beginRequest current =
        let cts = new CancellationTokenSource ()
        cancellation <- Some cts

        graph.Batch (fun () ->
            if data.Peek.IsNone then
                gate.Value <- AsyncSource<'T>(graph)

            metadata.Value <- FetchStatus.Fetching, None, true)

        let request =
            try
                let result = graph.Untrack (fun () -> fetch cts.Token)

                if isNull result then
                    raise (InvalidOperationException "The query fetch returned a null task.")

                Result.Ok result
            with error ->
                Result.Error error

        match request with
        | Result.Error error -> publish current (Result.Error error)
        | Result.Ok request ->
            task {
                let! outcome =
                    task {
                        try
                            let! value = request
                            return Result.Ok value
                        with error ->
                            return Result.Error error
                    }

                graph.Dispatch (fun () -> publish current outcome)
            }
            |> ignore

    let start () =
        guard ()

        if disposed then
            raise (ObjectDisposedException "QueryEntry")

        generation <- generation + 1L
        let current = generation
        cancel ()

        if not disposed && generation = current then
            beginRequest current

    /// <summary>A tracked value read; suspends until initial data arrives and retains data during refresh.</summary>
    member _.Value =
        guard ()

        if disposed then
            raise (ObjectDisposedException "QueryEntry")

        match data.Value with
        | Some value -> value
        | None -> gate.Value.Value

    /// <summary>A tracked state read that remains available during initial loading.</summary>
    member _.State =
        guard ()

        if disposed then
            raise (ObjectDisposedException "QueryEntry")

        let status, error, stale = metadata.Value

        {
            Data = data.Value
            FetchStatus = status
            Error = error
            IsStale = stale
        }

    /// <summary>Starts a missing or stale query and joins an existing request.</summary>
    member _.Ensure() =
        guard ()
        let status, _, stale = metadata.Peek

        if
            status <> FetchStatus.Fetching
            && (data.Peek.IsNone || stale)
        then
            start ()

    /// <summary>Supersedes the current request and starts another.</summary>
    member _.Refresh() = start ()

    /// <summary>Creates a typed draft without modifying requests or sources.</summary>
    member this.Draft(context: QueryStaging) =
        context.Get (
            this,
            fun () ->
                let initial = data.Peek
                let _, error, _ = metadata.Peek

                QueryDraft (
                    initial,
                    fun next stale updated ->
                        let changed =
                            match updated, initial, next with
                            | false, _, _ -> false
                            | true, Some old, Some value -> not (comparer.Equals (old, value))
                            | true, None, None -> false
                            | _ -> true

                        let replacement = if next.IsNone then Some (AsyncSource<'T>(graph)) else None
                        let mutable retired = None

                        let write () =
                            generation <- generation + 1L
                            retired <- cancellation
                            cancellation <- None

                            if changed then
                                data.Value <- next

                            metadata.Value <- FetchStatus.Idle, (if stale then error else None), stale

                            match replacement with
                            | Some pending -> gate.Value <- pending
                            | None -> ()

                        let stop () =
                            match retired with
                            | Some cts ->
                                try
                                    cts.Cancel ()
                                with failure ->
                                    graph.Root.OnCleanup (fun () -> raise failure)

                                cts.Dispose ()
                            | None -> ()

                        write, stop
                )
        )

    /// <summary>Retires outstanding completion and requests cancellation.</summary>
    member _.Dispose() =
        if not disposed then
            disposed <- true
            generation <- generation + 1L
            cancel ()
