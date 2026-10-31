namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary><c>IntersectionObserverInit.root</c>: the box targets are intersected with.</summary>
[<RequireQualifiedAccess>]
type IntersectionObserverRoot =
    /// <summary>A null root: the viewport of the top-level document.</summary>
    | Implicit
    /// <summary>The observer's document.</summary>
    | Document
    /// <summary>An element; targets outside its containing block chain never intersect.</summary>
    | Element of Element

/// <summary><c>IntersectionObserverInit</c>.</summary>
type IntersectionObserverInit =
    {
        Root: IntersectionObserverRoot
        /// <summary>A CSS margin of one to four lengths or percentages, such as <c>"10px"</c> or <c>"10% 0px"</c>,
        /// that grows or shrinks the root's bounds.</summary>
        RootMargin: string
        /// <summary>A CSS margin that grows the scrollports of the scroll containers between a target and the
        /// root.</summary>
        ScrollMargin: string
        /// <summary>The intersection ratios, from 0 to 1, whose crossing reports a target.</summary>
        Threshold: float array
    }

    /// <summary>The implicit root, no margins and the threshold 0.</summary>
    static member Default =
        {
            Root = IntersectionObserverRoot.Implicit
            RootMargin = "0px"
            ScrollMargin = "0px"
            Threshold = [| 0.0 |]
        }

/// <summary>A rectangle of an <c>IntersectionObserverEntry</c> in CSS pixels of the viewport.</summary>
type IntersectionObserverRect =
    {
        X: float
        Y: float
        Width: float
        Height: float
    }

/// <summary>A copied <c>IntersectionObserverEntry</c>, valid after its delivery returns.</summary>
/// <remarks>
/// A target outside the root's containing block chain, a disconnected target and a target of a disconnected root
/// report every rectangle as 0, 0, 0, 0.
/// </remarks>
type IntersectionObserverEntry =
    {
        Target: Element
        /// <summary>The time the intersection was computed, on the clock of
        /// <c>IntersectionObservationService.Now</c>.</summary>
        Time: float
        /// <summary>The root's bounds with the root margin; None for a cross-origin target.</summary>
        RootBounds: IntersectionObserverRect option
        BoundingClientRect: IntersectionObserverRect
        IntersectionRect: IntersectionObserverRect
        IsIntersecting: bool
        IntersectionRatio: float
    }

/// <summary>An invalid <c>IntersectionObserverInit</c>, with the engine's exception name: SyntaxError for a margin,
/// RangeError for a threshold.</summary>
exception IntersectionObserverException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>One engine observer of an <c>IntersectionObservationProvider</c>.</summary>
type IntersectionObserverHandle =
    {
        Observe: Element -> unit
        Unobserve: Element -> unit
        Disconnect: unit -> unit
        /// <summary><c>IntersectionObserver.takeRecords</c>: removes and returns the queued entries.</summary>
        TakeRecords: unit -> IntersectionObserverEntry array
        /// <summary>Disconnects and frees the engine observer; its callback will not run after this returns.</summary>
        Release: unit -> unit
    }

/// <summary>The engine side of intersection observation for one document.</summary>
type IntersectionObservationProvider =
    {
        /// <summary>The document's <c>performance.now()</c>, the clock of entry times.</summary>
        Now: unit -> float
        /// <summary>Creates an engine observer that calls the function once per notification, with its entries in
        /// the order of their targets' observation. Raises IntersectionObserverException for an invalid margin or
        /// threshold. An exception the function raises is reported by the provider and leaves other observers
        /// delivering.</summary>
        Create: IntersectionObserverInit -> (IntersectionObserverEntry array -> unit) -> IntersectionObserverHandle
    }

/// <summary>An <c>IntersectionObserver</c>; disposing it disconnects and frees it.</summary>
/// <remarks>
/// Each target is reported when first observed, and again when its intersection ratio crosses a threshold, during the
/// rendering update. Unobserve and Disconnect drop the target's queued entries. The order between observers is the
/// engine's.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type IntersectionObserver =
    inherit IDisposable
    /// <summary>The options the observer was created with.</summary>
    abstract Init: IntersectionObserverInit
    abstract Observe: target: Element -> unit
    abstract Unobserve: target: Element -> unit
    /// <summary>Unobserves every target; the observer observes again afterwards.</summary>
    abstract Disconnect: unit -> unit
    /// <summary>Removes and returns the queued entries, which the next delivery would have passed.</summary>
    /// <remarks>A rendering update queues and delivers entries together, so entries are queued only while another
    /// observer's callback of the same update runs. A disposed observer has none.</remarks>
    abstract TakeRecords: unit -> IntersectionObserverEntry array

/// <summary>Creates intersection observers of one document.</summary>
/// <remarks>Disposing the service disposes every observer it created.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type IntersectionObservationService =
    inherit IDisposable
    /// <summary>The document's <c>performance.now()</c> in milliseconds, the clock of
    /// <c>IntersectionObserverEntry.Time</c>.</summary>
    abstract Now: unit -> float

    /// <summary>A new observer delivering each notification's entries to <c>callback</c>.</summary>
    /// <remarks>
    /// Raises IntersectionObserverException for an invalid margin or threshold. The observer is disposed when
    /// <c>signal</c> aborts; a signal that has already aborted returns a disposed observer.
    /// </remarks>
    abstract Create:
        callback: (IntersectionObserverEntry array -> unit) * init: IntersectionObserverInit * ?signal: AbortSignal -> IntersectionObserver

/// <summary>Creates intersection observation services over a provider.</summary>
[<RequireQualifiedAccess>]
module IntersectionObservation =
    /// <summary>A service over <c>provider</c>.</summary>
    let create (provider: IntersectionObservationProvider) : IntersectionObservationService =
        if isNull (box provider) then
            nullArg "provider"

        let mutable disposed = false
        let observers = List<IDisposable>()

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof IntersectionObservationService))

        let observer callback (init: IntersectionObserverInit) (signal: AbortSignal option) =
            let mutable released = false
            let mutable handle = None
            let mutable registration: IDisposable = null

            let active () =
                if released then
                    raise (ObjectDisposedException (nameof IntersectionObserver))

            let checkTarget (target: Element) =
                if isNull (box target) then
                    nullArg "target"

            let self =
                { new IntersectionObserver with
                    member _.Init = init

                    member _.Observe target =
                        active ()
                        checkTarget target
                        handle.Value.Observe target

                    member _.Unobserve target =
                        active ()
                        checkTarget target
                        handle.Value.Unobserve target

                    member _.Disconnect() =
                        active ()
                        handle.Value.Disconnect ()

                    member _.TakeRecords() =
                        if released then [||] else handle.Value.TakeRecords ()

                    member this.Dispose() =
                        if not released then
                            released <- true
                            observers.Remove this |> ignore

                            if not (isNull registration) then
                                registration.Dispose ()

                            match handle with
                            | Some engine -> engine.Release ()
                            | None -> ()
                }

            // The engine validates the options before an aborted signal is considered.
            let engine =
                provider.Create init (fun entries ->
                    if not released then
                        callback entries)

            match signal with
            | Some signal when signal.Aborted ->
                released <- true
                engine.Release ()
            | _ ->
                handle <- Some engine
                observers.Add self |> ignore

                match signal with
                | Some signal -> registration <- signal.OnAbort (fun _ -> self.Dispose ())
                | None -> ()

            self

        { new IntersectionObservationService with
            member _.Now() =
                live ()
                provider.Now ()

            member _.Create(callback, init, ?signal) =
                live ()

                if isNull (box callback) then
                    nullArg "callback"

                if isNull (box init) then
                    nullArg "init"

                let init =
                    { init with
                        RootMargin = (if isNull init.RootMargin then "0px" else init.RootMargin)
                        ScrollMargin =
                            (if isNull init.ScrollMargin then
                                 "0px"
                             else
                                 init.ScrollMargin)
                        Threshold =
                            (if isNull init.Threshold || init.Threshold.Length = 0 then
                                 [| 0.0 |]
                             else
                                 Array.copy init.Threshold)
                    }

                match init.Root with
                | IntersectionObserverRoot.Element element when isNull (box element) -> nullArg "init.Root"
                | _ -> ()

                observer callback init signal

            member _.Dispose() =
                if not disposed then
                    disposed <- true

                    for observer in Seq.toArray observers do
                        observer.Dispose ()
        }
