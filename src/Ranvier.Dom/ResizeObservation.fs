namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary><c>ResizeObserverOptions.box</c>: the box whose size changes are reported.</summary>
[<RequireQualifiedAccess>]
type ResizeObserverBox =
    /// <summary><c>"content-box"</c>, the default.</summary>
    | ContentBox
    /// <summary><c>"border-box"</c>.</summary>
    | BorderBox
    /// <summary><c>"device-pixel-content-box"</c>: the content box in device pixels.</summary>
    | DevicePixelContentBox

/// <summary>A <c>ResizeObserverSize</c>: a box size in the element's writing mode.</summary>
/// <remarks>Inline is the width in a horizontal writing mode and the height in a vertical one.</remarks>
type ResizeObserverSize = { InlineSize: float; BlockSize: float }

/// <summary><c>ResizeObserverEntry.contentRect</c> in CSS pixels: the content box at its offset in the padding box.</summary>
type ResizeObserverRect =
    {
        X: float
        Y: float
        Width: float
        Height: float
    }

/// <summary>A copied <c>ResizeObserverEntry</c>, valid after its delivery returns.</summary>
type ResizeObserverEntry =
    {
        Target: Element
        ContentRect: ResizeObserverRect
        /// <summary><c>contentBoxSize</c> in CSS pixels, one size per fragment.</summary>
        ContentBoxSize: ResizeObserverSize array
        /// <summary><c>borderBoxSize</c> in CSS pixels, one size per fragment.</summary>
        BorderBoxSize: ResizeObserverSize array
        /// <summary><c>devicePixelContentBoxSize</c>; empty where the engine lacks the device-pixel content box.</summary>
        DevicePixelContentBoxSize: ResizeObserverSize array
    }

/// <summary>One engine observer of a <c>ResizeObservationProvider</c>.</summary>
type ResizeObserverHandle =
    {
        /// <summary><c>ResizeObserver.observe</c> with a box the provider supports.</summary>
        Observe: Element -> ResizeObserverBox -> unit
        Unobserve: Element -> unit
        Disconnect: unit -> unit
        /// <summary>Disconnects and frees the engine observer; its callback will not run after this returns.</summary>
        Release: unit -> unit
    }

/// <summary>The engine side of resize observation for one document.</summary>
type ResizeObservationProvider =
    {
        Supports: ResizeObserverBox -> bool
        /// <summary>Creates an engine observer that calls the function once per notification, with its entries in
        /// observation order. An exception the function raises is reported by the provider and leaves other
        /// observers delivering.</summary>
        Create: (ResizeObserverEntry array -> unit) -> ResizeObserverHandle
    }

/// <summary>A <c>ResizeObserver</c>; disposing it disconnects and frees it.</summary>
/// <remarks>
/// Each target is reported when first observed, also at 0 by 0, and again when its observed box changes size, during
/// the rendering update. Observing an observed target with another box replaces its observation and reports it again;
/// with the same box it changes nothing. Unobserve and Disconnect drop pending notifications.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ResizeObserver =
    inherit IDisposable
    /// <summary>Observes <c>target</c>'s <c>box</c>, the content box by default.</summary>
    /// <remarks>Raises NotSupportedException for a box the service does not support.</remarks>
    abstract Observe: target: Element * ?box: ResizeObserverBox -> unit
    abstract Unobserve: target: Element -> unit
    /// <summary>Unobserves every target; the observer observes again afterwards.</summary>
    abstract Disconnect: unit -> unit

/// <summary>Creates resize observers of one document.</summary>
/// <remarks>Disposing the service disposes every observer it created.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ResizeObservationService =
    inherit IDisposable
    /// <summary>Whether <c>Observe</c> accepts <c>box</c>.</summary>
    abstract Supports: box: ResizeObserverBox -> bool
    /// <summary>A new observer delivering each notification's entries to <c>callback</c>.</summary>
    /// <remarks>
    /// The observer is disposed when <c>signal</c> aborts; a signal that has already aborted returns a disposed
    /// observer.
    /// </remarks>
    abstract Create: callback: (ResizeObserverEntry array -> unit) * ?signal: AbortSignal -> ResizeObserver

/// <summary>Creates resize observation services over a provider.</summary>
[<RequireQualifiedAccess>]
module ResizeObservation =
    /// <summary>A service over <c>provider</c>.</summary>
    let create (provider: ResizeObservationProvider) : ResizeObservationService =
        if isNull (box provider) then
            nullArg "provider"

        let mutable disposed = false
        let observers = List<IDisposable>()

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof ResizeObservationService))

        let observer (callback: ResizeObserverEntry array -> unit) (signal: AbortSignal option) =
            let mutable released = false
            let mutable handle = None
            let mutable registration: IDisposable = null

            let self =
                { new ResizeObserver with
                    member _.Observe(target, ?observed) =
                        if released then
                            raise (ObjectDisposedException (nameof ResizeObserver))

                        if isNull (box target) then
                            nullArg "target"

                        let observed = defaultArg observed ResizeObserverBox.ContentBox

                        if not (provider.Supports observed) then
                            raise (NotSupportedException "The engine does not support the requested box")

                        handle.Value.Observe target observed

                    member _.Unobserve target =
                        if released then
                            raise (ObjectDisposedException (nameof ResizeObserver))

                        if isNull (box target) then
                            nullArg "target"

                        handle.Value.Unobserve target

                    member _.Disconnect() =
                        if released then
                            raise (ObjectDisposedException (nameof ResizeObserver))

                        handle.Value.Disconnect ()

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

            match signal with
            | Some signal when signal.Aborted -> released <- true
            | _ ->
                handle <-
                    Some (
                        provider.Create (fun entries ->
                            if not released then
                                callback entries)
                    )

                observers.Add self |> ignore

                match signal with
                | Some signal -> registration <- signal.OnAbort (fun _ -> self.Dispose ())
                | None -> ()

            self

        { new ResizeObservationService with
            member _.Supports observed =
                live ()
                provider.Supports observed

            member _.Create(callback, ?signal) =
                live ()

                if isNull (box callback) then
                    nullArg "callback"

                observer callback signal

            member _.Dispose() =
                if not disposed then
                    disposed <- true

                    for observer in Seq.toArray observers do
                        observer.Dispose ()
        }
