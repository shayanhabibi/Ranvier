namespace Ranvier.Query

open System.Collections.Generic

/// <summary>A staged entry whose validation precedes all publication.</summary>
type internal IQueryDraft =
    abstract Prepare: unit -> unit
    abstract Publish: unit -> unit
    abstract Cancel: unit -> unit

/// <summary>A typed editable draft shared by ordered descriptions targeting one entry.</summary>
type internal QueryDraft<'T>(initial: 'T option, prepare: 'T option -> bool -> bool -> (unit -> unit) * (unit -> unit)) =
    let mutable data = initial
    let mutable stale = true
    let mutable updated = false
    let mutable publish = ignore
    let mutable cancel = ignore

    member _.Update(updater: 'T -> 'T) =
        match data with
        | Some value ->
            data <- Some (updater value)
            stale <- false
            updated <- true
        | None -> ()

    member _.Invalidate() =
        stale <- true

    interface IQueryDraft with
        member _.Prepare() =
            let write, retire = prepare data stale updated in
            publish <- write
            cancel <- retire

        member _.Publish() =
            publish ()

        member _.Cancel() = cancel ()

/// <summary>A transaction's heterogeneous drafts; family lookup remains fully typed.</summary>
type internal QueryStaging() =
    let entries = Dictionary<obj, obj>(HashIdentity.Reference)
    let drafts = ResizeArray<IQueryDraft>()

    member _.Get<'T>(identity: obj, create: unit -> QueryDraft<'T>) =
        match entries.TryGetValue identity with
        | true, value -> unbox<QueryDraft<'T>> value
        | false, _ ->
            let draft = create ()
            entries.Add (identity, box draft)
            drafts.Add draft
            draft

    member _.Drafts = drafts

/// <summary>An opaque, client-bound description of a cache update or invalidation.</summary>
[<Sealed>]
type QueryEdit internal (client: obj, stage: QueryStaging -> unit) =
    member internal _.Client = client

    member internal _.Stage(context) =
        stage context

/// <summary>A queued remote operation with client-owned cancellation.</summary>
type internal IQueryMutation =
    abstract Start: (unit -> unit) -> unit
    abstract Cancel: unit -> unit

/// <summary>The current request activity, independent of cached data availability.</summary>
[<RequireQualifiedAccess>]
type FetchStatus =
    | Idle
    | Fetching

/// <summary>Cached data and request state. A refresh failure retains the last accepted data.</summary>
type QuerySnapshot<'T> =
    {
        Data: 'T option
        FetchStatus: FetchStatus
        Error: exn option
        IsStale: bool
    }

/// <summary>The result of a remote mutation and its local cache reconciliation.</summary>
[<RequireQualifiedAccess>]
type MutationOutcome<'T> =
    | Applied of 'T
    | RequestFailed of exn
    | ReconciliationFailed of result: 'T * error: exn

/// <summary>A publication comparer that accepts prevalidated source writes.</summary>
type internal PublicationComparer<'T>() =
    interface IEqualityComparer<'T> with
        member _.Equals(_, _) = false
        member _.GetHashCode(_) = 0
