namespace Ranvier.Dom

open System
open System.Threading

/// <summary>How a service's successful-response byte cache takes part in a request.</summary>
/// <remarks>The cache belongs to one service; browser HTTP caches and engine-wide caches are outside it.</remarks>
[<RequireQualifiedAccess>]
type ResourceCachePolicy =
    /// <summary>Reuses a successful response this service already holds for the URL.</summary>
    | UseCache
    /// <summary>Bypasses the held response and replaces it on success.</summary>
    | Reload
    /// <summary>Bypasses the held response and leaves the cache unchanged.</summary>
    | NoStore

/// <summary>An absolute http, https or data URL with its cache policy.</summary>
type ResourceRequest =
    {
        Url: string
        CachePolicy: ResourceCachePolicy
    }

/// <summary>A response whose bytes the caller owns; services copy them at every ownership boundary.</summary>
type ResourceResponse =
    {
        ContentType: string
        Bytes: byte array
    }

/// <summary>Why an operation ended without a value.</summary>
[<RequireQualifiedAccess>]
type ResourceFailure =
    /// <summary>The provider, transport or access check failed.</summary>
    | LoadFailed of message: string
    /// <summary>The bytes arrived but the engine cannot use them as the requested kind.</summary>
    | DecodeFailed of message: string
    /// <summary>The caller cancelled or disposed the operation.</summary>
    | Cancelled
    /// <summary>The source, target or document the operation observed was replaced or became inactive.</summary>
    | Invalidated of reason: string

/// <summary>The single terminal outcome of an operation.</summary>
[<RequireQualifiedAccess>]
type ResourceOutcome<'T> =
    | Succeeded of value: 'T
    | Failed of failure: ResourceFailure

/// <summary>An operation's state as seen on its owner context.</summary>
[<RequireQualifiedAccess>]
type ResourceState<'T> =
    | Pending
    | Settled of outcome: ResourceOutcome<'T>

/// <summary>A decoded image the operation owns until it is disposed.</summary>
/// <remarks><c>SourceUrl</c> is the requested URL; <c>TransportUrl</c> is the per-operation URL the engine loaded.</remarks>
type ResourceImage =
    {
        Width: int
        Height: int
        SourceUrl: string
        TransportUrl: string
    }

/// <summary>Decode readiness of an existing image element, with the source URL the element currently uses.</summary>
type ResourceImageInfo =
    {
        Width: int
        Height: int
        SourceUrl: string
    }

/// <summary>A font family registered with the document while the operation is held.</summary>
type ResourceFont = { Family: string }

/// <summary>
/// Starts a request and reports its result through <c>complete</c>, from any thread, synchronously or later, once or
/// repeatedly. The service uses only the first result. Disposing the returned handle withdraws the request.
/// </summary>
type ResourceProvider = ResourceRequest -> (Result<ResourceResponse, string> -> unit) -> IDisposable

/// <summary>A pending or settled operation. Cancel and Dispose are idempotent and run on the owner context.</summary>
/// <remarks>The terminal callback runs exactly once, including for cancellation; Dispose then releases any value.</remarks>
type ResourceOperation<'T> =
    inherit IDisposable
    abstract Snapshot: ResourceState<'T>
    abstract Cancel: unit -> unit

/// <summary>Loads resources and observes image and font readiness for one document or host.</summary>
/// <remarks>Terminal callbacks run on the service's owner context.</remarks>
type ResourceService =
    inherit IDisposable
    abstract Load: request: ResourceRequest * terminal: (ResourceOutcome<ResourceResponse> -> unit) -> ResourceOperation<ResourceResponse>
    abstract LoadImage: request: ResourceRequest * terminal: (ResourceOutcome<ResourceImage> -> unit) -> ResourceOperation<ResourceImage>
    abstract DecodeImage: target: Element * terminal: (ResourceOutcome<ResourceImageInfo> -> unit) -> ResourceOperation<ResourceImageInfo>

    abstract LoadFont:
        family: string * request: ResourceRequest * terminal: (ResourceOutcome<ResourceFont> -> unit) -> ResourceOperation<ResourceFont>

    /// <summary>Removes this service's cached successful responses.</summary>
    abstract ClearCache: unit -> unit

/// <summary>
/// Settles an operation once. <c>Settle</c> may be called from any thread and any number of times; the first call wins
/// and its effects run through <c>post</c> on the owner context. <c>Cancel</c> and <c>Dispose</c> run on the owner context.
/// </summary>
/// <param name="post">Runs an action on the owner context.</param>
/// <param name="terminal">Receives the one terminal outcome on the owner context.</param>
/// <param name="withdraw">Stops pending engine or provider work after cancellation; called at most once.</param>
/// <param name="release">Releases a delivered value when the operation is disposed; called at most once.</param>
type ResourceArbiter<'T>(post: (unit -> unit) -> unit, terminal: ResourceOutcome<'T> -> unit, withdraw: unit -> unit, release: 'T -> unit) =
    let mutable decided = 0
    let mutable state = ResourceState<'T>.Pending
    let mutable disposed = false
    let mutable released = false
    let mutable withdrawn = false

    let claim () =
#if FABLE_COMPILER
        if decided = 0 then
            decided <- 1
            true
        else
            false
#else
        Interlocked.CompareExchange (&decided, 1, 0) = 0
#endif

    let releaseValue () =
        match state with
        | ResourceState.Settled (ResourceOutcome.Succeeded value) when not released ->
            released <- true
            release value
        | _ -> ()

    let stopWork () =
        if not withdrawn then
            withdrawn <- true
            withdraw ()

    let deliver outcome =
        state <- ResourceState.Settled outcome
        terminal outcome

        if disposed then
            releaseValue ()

    /// <summary>Offers an outcome; returns true when it is the first.</summary>
    member _.Settle(outcome: ResourceOutcome<'T>) =
        if claim () then
            post (fun () -> deliver outcome)
            true
        else
            false

    member _.Snapshot = state

    member _.Cancel() =
        if claim () then
            stopWork ()
            deliver (ResourceOutcome.Failed ResourceFailure.Cancelled)

    member this.Dispose() =
        if not disposed then
            this.Cancel ()
            disposed <- true
            releaseValue ()

    interface ResourceOperation<'T> with
        member this.Snapshot = this.Snapshot

        member this.Cancel() =
            this.Cancel ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()
