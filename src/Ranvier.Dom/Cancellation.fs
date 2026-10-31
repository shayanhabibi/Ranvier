namespace Ranvier.Dom

open System
open System.Collections.Generic
open System.Threading

/// <summary>The reason a signal aborted: the default AbortError, a timeout's TimeoutError, or a copied portable payload.</summary>
[<RequireQualifiedAccess>]
type AbortReason =
    | Abort
    | Timeout
    | Custom of TimingDetail

/// <summary>Raised by <c>ThrowIfAborted</c>; carries the signal's reason.</summary>
exception AbortedException of reason: AbortReason with
    override this.Message =
        match this.reason with
        | AbortReason.Abort -> "AbortError: The operation was aborted"
        | AbortReason.Timeout -> "TimeoutError: The operation timed out"
        | AbortReason.Custom _ -> "AbortError: The operation was aborted with a custom reason"

/// <summary>A signal that aborts at most once; its reason is fixed from the moment it aborts.</summary>
type AbortSignal =
    /// <summary>Whether the signal has aborted.</summary>
    abstract Aborted: bool
    /// <summary>The abort reason, or None before the signal aborts; repeated reads return equal values.</summary>
    /// <remarks>A custom Object or Array payload keeps its instance across reads and across composite signals.</remarks>
    abstract Reason: AbortReason option
    /// <summary>Raises <c>AbortedException</c> with the reason when the signal has aborted.</summary>
    abstract ThrowIfAborted: unit -> unit
    /// <summary>Runs <c>callback</c> once when the signal aborts, after its internal cleanup, in registration order.</summary>
    /// <remarks>A signal that has already aborted never runs the callback. Disposing the result unregisters it.</remarks>
    abstract OnAbort: callback: (AbortReason -> unit) -> IDisposable

/// <summary>The owner of one signal.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type AbortController =
    /// <summary>The controller's signal; every read returns the same signal.</summary>
    abstract Signal: AbortSignal
    /// <summary>Aborts the signal with <c>AbortReason.Abort</c>; later calls leave the signal unchanged.</summary>
    abstract Abort: unit -> unit
    /// <summary>Aborts the signal with a copy of <c>reason</c>; later calls leave the signal unchanged.</summary>
    abstract Abort: reason: AbortReason -> unit

/// <summary>A borrowed cancellation domain: controllers, timeouts, composition, listener signals and token mapping.</summary>
/// <remarks>Signals belong to the service that created them. Disposal stops pending timeouts and token mappings.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CancellationService =
    inherit IDisposable
    /// <summary>Creates a controller with a signal that has not aborted.</summary>
    abstract CreateController: unit -> AbortController
    /// <summary>Returns a signal that has already aborted with a copy of <c>reason</c>.</summary>
    abstract Abort: reason: AbortReason -> AbortSignal
    /// <summary>Returns a signal that aborts with <c>AbortReason.Timeout</c> after <c>milliseconds</c>, on a later turn.</summary>
    /// <remarks>Rejects a negative, non-finite or above 2^53-1 duration with ArgumentOutOfRangeException.</remarks>
    abstract Timeout: milliseconds: float -> AbortSignal
    /// <summary>Returns a signal that aborts with the reason of the first of <c>signals</c> to abort.</summary>
    /// <remarks>When one of <c>signals</c> has already aborted, the result has already aborted with its reason.</remarks>
    abstract Any: signals: AbortSignal array -> AbortSignal

    /// <summary>Adds an event listener; when the signal aborts it removes the registration matching type, listener and capture.</summary>
    /// <remarks>An aborted signal adds nothing. A duplicate of an existing registration adds nothing and the signal leaves the existing one in place.</remarks>
    abstract Listen:
        target: EventTarget * eventType: string * listener: EventListener * options: AddEventListenerOptions * signal: AbortSignal -> unit

    /// <summary>Returns a token cancelled when the signal aborts; repeated calls return the same token.</summary>
    abstract ToCancellationToken: signal: AbortSignal -> CancellationToken
    /// <summary>Returns a signal that aborts with <c>AbortReason.Abort</c> on the turn after <c>token</c> is cancelled.</summary>
    /// <remarks>Cancellation never changes <c>Aborted</c> synchronously; a token cancelled at the call returns an aborted signal.</remarks>
    abstract FromCancellationToken: token: CancellationToken -> AbortSignal

/// <summary>The host services a managed cancellation domain runs on.</summary>
type CancellationHost =
    /// <summary>Raises when the caller may not use the host: wrong thread or disposed host.</summary>
    abstract Check: unit -> unit
    /// <summary>Runs <c>action</c> after <c>milliseconds</c> on a later host turn; the result cancels the timer.</summary>
    abstract SetTimeout: action: (unit -> unit) * milliseconds: float -> (unit -> unit)
    /// <summary>Queues <c>action</c> for a later host turn; callable from any thread.</summary>
    abstract Post: action: (unit -> unit) -> unit
    /// <summary>Records an exception raised by an abort callback.</summary>
    abstract Report: error: exn -> unit
    /// <summary>Adds an event listener; the result removes the registration matching type, listener and capture, or is None when nothing was added.</summary>
    abstract Listen: target: EventTarget * eventType: string * listener: EventListener * options: AddEventListenerOptions -> (unit -> unit) option

/// <summary>Argument validation shared by cancellation providers.</summary>
module internal CancellationContract =
    let maximumTimeout = 9007199254740991.0

    let timeout (milliseconds: float) =
        if
            Double.IsNaN milliseconds
            || Double.IsInfinity milliseconds
            || milliseconds < 0.0
            || milliseconds > maximumTimeout
        then
            raise (ArgumentOutOfRangeException (nameof milliseconds, "A timeout must be a finite duration from 0 to 2^53-1 milliseconds"))

        Math.Truncate milliseconds

    let reason (value: AbortReason) =
        if isNull (box value) then
            nullArg "reason"

        match value with
        | AbortReason.Custom detail -> AbortReason.Custom (TimingContract.copyDetail detail)
        | other -> other

    let signals (values: AbortSignal array) =
        if isNull values then
            nullArg "signals"

        for value in values do
            if isNull (box value) then
                nullArg "signals"

        Array.copy values

/// <summary>A managed signal: reason, ordered callbacks, composite links and cleanup.</summary>
type internal ManagedSignal(owner: obj, check: unit -> unit, report: exn -> unit, composite: bool) =
    let algorithms = ResizeArray<unit -> unit>()
    let handlers = List<int * (AbortReason -> unit)>()
    let mutable nextHandler = 0
    let mutable reason: AbortReason option = None
    let mutable token: CancellationTokenSource = null

    member _.Owner = owner
    member _.Composite = composite
    member val Sources = ResizeArray<ManagedSignal>()
    member val Dependents = ResizeArray<ManagedSignal>()

    member _.Current = reason

    member _.SetReason value =
        reason <- Some value

    member _.AddAlgorithm(action: unit -> unit) =
        algorithms.Add action

    /// <summary>Runs internal cleanup, then the abort callbacks registered when the signal aborted.</summary>
    member _.RunAbortSteps() =
        let pending = algorithms.ToArray ()
        algorithms.Clear ()

        for action in pending do
            try
                action ()
            with error ->
                report error

        let snapshot = handlers.ToArray ()
        let value = reason.Value

        for id, callback in snapshot do
            if handlers.RemoveAll (fun (candidate, _) -> candidate = id) > 0 then
                try
                    callback value
                with error ->
                    report error

        handlers.Clear ()

    member this.Token =
        if isNull token then
            let source = new CancellationTokenSource ()
            token <- source

            match reason with
            | Some _ -> source.Cancel ()
            | None -> this.AddAlgorithm (fun () -> source.Cancel ())

        token.Token

    interface AbortSignal with
        member _.Aborted =
            check ()
            reason.IsSome

        member _.Reason =
            check ()
            reason

        member _.ThrowIfAborted() =
            check ()

            match reason with
            | Some value -> raise (AbortedException value)
            | None -> ()

        member _.OnAbort callback =
            check ()

            if isNull (box callback) then
                nullArg "callback"

            match reason with
            | Some _ ->
                { new IDisposable with
                    member _.Dispose() = ()
                }
            | None ->
                let id = nextHandler
                nextHandler <- nextHandler + 1
                handlers.Add ((id, callback))

                { new IDisposable with
                    member _.Dispose() =
                        handlers.RemoveAll (fun (candidate, _) -> candidate = id)
                        |> ignore
                }

/// <summary>Creates managed cancellation domains over a host's timers, owner queue and listeners.</summary>
module Cancellation =
    /// <summary>A cancellation domain whose signals run on <c>host</c>; every member checks <c>host</c> first.</summary>
    let create (host: CancellationHost) : CancellationService =
        if isNull (box host) then
            nullArg "host"

        host.Check ()
        let identity = obj ()
        let disposed = ref false
        let cleanups = HashSet<unit -> unit>(HashIdentity.Reference)

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof CancellationService))

            host.Check ()

        let signal composite =
            ManagedSignal (identity, check, host.Report, composite)

        let own (value: AbortSignal) =
            match value with
            | :? ManagedSignal as managed when obj.ReferenceEquals (managed.Owner, identity) -> managed
            | _ -> raise (ArgumentException "The signal belongs to another cancellation service")

        let track (target: ManagedSignal) (cleanup: unit -> unit) =
            cleanups.Add cleanup |> ignore

            target.AddAlgorithm (fun () ->
                cleanups.Remove cleanup |> ignore
                cleanup ())

        let abort (target: ManagedSignal) value =
            if target.Current.IsNone then
                target.SetReason value

                let dependents =
                    target.Dependents
                    |> Seq.filter (fun dependent -> dependent.Current.IsNone)
                    |> Seq.toArray

                for dependent in dependents do
                    dependent.SetReason value

                target.RunAbortSteps ()

                for dependent in dependents do
                    dependent.RunAbortSteps ()

        let aborted value =
            let result = signal false
            result.SetReason value
            result

        { new CancellationService with
            member _.CreateController() =
                check ()
                let target = signal false

                { new AbortController with
                    member _.Signal = target :> AbortSignal

                    member _.Abort() =
                        check ()
                        abort target AbortReason.Abort

                    member _.Abort(reason: AbortReason) =
                        check ()
                        let value = CancellationContract.reason reason
                        abort target value
                }

            member _.Abort reason =
                check ()
                aborted (CancellationContract.reason reason) :> AbortSignal

            member _.Timeout milliseconds =
                check ()
                let duration = CancellationContract.timeout milliseconds
                let target = signal false

                let clear =
                    host.SetTimeout (
                        (fun () ->
                            if not disposed.Value then
                                abort target AbortReason.Timeout),
                        duration
                    )

                track target clear
                target :> AbortSignal

            member _.Any signals =
                check ()

                let sources =
                    CancellationContract.signals signals
                    |> Array.map own

                match
                    sources
                    |> Array.tryFind (fun source -> source.Current.IsSome)
                with
                | Some source -> aborted source.Current.Value :> AbortSignal
                | None ->
                    let result = signal true

                    let link (source: ManagedSignal) =
                        if not (result.Sources.Contains source) then
                            result.Sources.Add source
                            source.Dependents.Add result

                    for source in sources do
                        if source.Composite then
                            for inner in source.Sources do
                                link inner
                        else
                            link source

                    result :> AbortSignal

            member _.Listen(target, eventType, listener, options, abortSignal) =
                check ()

                if isNull (box target) then
                    nullArg "target"

                if isNull eventType then
                    nullArg "eventType"

                let source = own abortSignal

                if source.Current.IsNone then
                    match host.Listen (target, eventType, listener, options) with
                    | Some remove -> source.AddAlgorithm remove
                    | None -> ()

            member _.ToCancellationToken abortSignal =
                check ()
                (own abortSignal).Token

            member _.FromCancellationToken token =
                check ()

                if token.IsCancellationRequested then
                    aborted AbortReason.Abort :> AbortSignal
                else
                    let target = signal false

                    let live = ref true

                    let registration =
                        token.Register (fun () ->
                            host.Post (fun () ->
                                if live.Value && not disposed.Value then
                                    abort target AbortReason.Abort))

                    track target (fun () ->
                        live.Value <- false
#if !FABLE_COMPILER
                        registration.Dispose ()
#else
                        ignore registration
#endif
                    )

                    target :> AbortSignal

            member _.Dispose() =
                if not disposed.Value then
                    disposed.Value <- true

                    for cleanup in Seq.toArray cleanups do
                        cleanup ()

                    cleanups.Clear ()
        }
