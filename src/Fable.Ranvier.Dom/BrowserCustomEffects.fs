namespace Ranvier.Dom

open System
open System.Threading
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Browser provider of custom animation effects: a facade over a null-target KeyframeEffect and requestAnimationFrame.</summary>
/// <remarks>
/// Browsers without CustomEffect (Chromium) get the same contract from the facade: each frame, in creation order, every
/// live effect whose computed progress is not null calls its tick. The capability provider reports the facade honestly:
/// Exported is the realm's own CustomEffect presence, Implemented is the facade's, and the remaining facts stay Unknown.
/// </remarks>
module BrowserCustomEffects =
    [<Emit("""(() => {
        try { const realm=$0.defaultView; return realm!=null && realm.document===$0; }
        catch { return false; }
    })()""")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("""(() => { const r=$0.defaultView;
        return r!=null && typeof r.KeyframeEffect==='function' && typeof r.Animation==='function' && typeof r.requestAnimationFrame==='function'; })()""")>]
    let private available (document: Document) : bool = jsNative

    [<Emit("(() => { const r=$0.defaultView; return r!=null && typeof r.CustomEffect==='function'; })()")>]
    let private engineHasCustomEffect (document: Document) : bool = jsNative

    // The browser's timing dictionary: each field, with the duration union's value unwrapped.
    [<Emit("""(($0)==null?undefined:(()=>{const o=$0,r={};
        for(const k of ['delay','endDelay','fill','iterationStart','iterations','direction','easing'])
            if(o[k]!==undefined)r[k]=o[k];
        const d=o.duration;if(d!==undefined)r.duration=d!=null&&typeof d==='object'&&Array.isArray(d.fields)?d.fields[0]:d;return r;})())""")>]
    let private timing (value: obj) : obj = jsNative

    [<Emit("new $0.defaultView.KeyframeEffect(null,[],$1)")>]
    let private effect (document: Document) (timing: obj) : AnimationEffect = jsNative

    [<Emit("new $0.defaultView.Animation($1,$0.timeline)")>]
    let private animation (document: Document) (effect: AnimationEffect) : WebAnimation = jsNative

    [<Emit("$0.id=$1")>]
    let private setId (animation: WebAnimation) (id: string) : unit = jsNative

    [<Emit("$0.play()")>]
    let private play (animation: WebAnimation) : unit = jsNative

    [<Emit("$0.getComputedTiming().progress")>]
    let private progress (effect: AnimationEffect) : float option = jsNative

    [<Emit("$0.defaultView.requestAnimationFrame($1)")>]
    let private requestFrame (document: Document) (callback: float -> unit) : int = jsNative

    [<Emit("$0.defaultView.cancelAnimationFrame($1)")>]
    let private cancelFrame (document: Document) (handle: int) : unit = jsNative

    [<Emit("(() => { const r=$0.defaultView; if (typeof r.reportError==='function') r.reportError($1); else setTimeout(() => { throw $1; }); })()")>]
    let private report (document: Document) (error: obj) : unit = jsNative

    [<Emit("$0.name || 'Error'")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("$0.message || String($0)")>]
    let private errorMessage (error: obj) : string = jsNative

    type private Entry =
        {
            Effect: AnimationEffect
            Tick: float -> unit
            mutable Live: bool
        }

    /// <summary>The custom effect facade of the document's realm.</summary>
    let forDocument (document: Document) : CustomEffectService =
        if isNull (box document) then
            nullArg (nameof document)

        if not (current document) || not (available document) then
            raise (NotSupportedException "Custom effects require an active document with KeyframeEffect and requestAnimationFrame")

        let mutable disposed = false
        let entries = ResizeArray<Entry>()
        let mutable frame: int option = None

        let active () =
            if disposed then
                raise (ObjectDisposedException "CustomEffectService")

            if not (current document) then
                raise (InvalidOperationException "The custom effect document is no longer active")

        let engine action =
            try
                action ()
            with error ->
                raise (AnimationException (errorName (box error), errorMessage (box error)))

        let rec schedule () =
            if frame.IsNone && entries.Count > 0 then
                frame <- Some (requestFrame document run)

        and run (_: float) =
            frame <- None
            // Effects created or disposed by a tick take part from the next frame.
            for entry in entries.ToArray () do
                if entry.Live then
                    match progress entry.Effect with
                    | Some value when entry.Live ->
                        try
                            entry.Tick value
                        with error ->
                            report document (box error)
                    | _ -> ()

            entries.RemoveAll (fun entry -> not entry.Live)
            |> ignore

            if not disposed && current document then
                schedule ()

        let register (effect: AnimationEffect) tick =
            let entry =
                {
                    Effect = effect
                    Tick = tick
                    Live = true
                }

            entries.Add entry
            schedule ()
            entry

        let revoke (entry: Entry) =
            entry.Live <- false

        let create tick (value: obj) =
            active ()

            if isNull (box tick) then
                nullArg "tick"

            let created = engine (fun () -> effect document (timing value))
            created, register created tick

        { new CustomEffectService with
            member _.CreateCustomEffect(tick, ?options) =
                let created, entry = create tick (box (defaultArg options null))

                { new CustomEffectRegistration with
                    member _.Effect = created

                    member _.Dispose() =
                        revoke entry
                }

            member _.CreateCustomEffectFor(tick, duration) =
                let created, entry = create tick (box (createObj [ "duration" ==> duration ]))

                { new CustomEffectRegistration with
                    member _.Effect = created

                    member _.Dispose() =
                        revoke entry
                }

            member _.Animate(tick, ?options) =
                let options = defaultArg options null
                let created, entry = create tick (box options)
                let playing = engine (fun () -> animation document created)

                if not (isNull options) then
                    setId playing options.id

                engine (fun () -> play playing)

                { new CustomAnimation with
                    member _.Effect = created
                    member _.Animation = playing

                    member _.Dispose() =
                        revoke entry
                }

            member _.Dispose() =
                if not disposed then
                    disposed <- true

                    for entry in entries do
                        entry.Live <- false

                    entries.Clear ()

                    match frame with
                    | Some handle ->
                        cancelFrame document handle
                        frame <- None
                    | None -> ()
        }

    /// <summary>The facade's capability provider for PlatformFacility "animation-custom-effects".</summary>
    /// <remarks>
    /// Exported reports whether the realm itself has CustomEffect; Implemented reports the facade (KeyframeEffect,
    /// Animation and requestAnimationFrame present), never the engine; Enabled, Permitted and Available stay Unknown.
    /// </remarks>
    let capabilityProvider (document: Document) : CapabilityProvider =
        if isNull (box document) then
            nullArg (nameof document)

        let key =
            Capabilities.key CapabilityCategory.PlatformFacility "animation-custom-effects"

        let fact reason value =
            {
                State = (if value then CapabilityState.Yes else CapabilityState.No)
                Reason = reason
            }

        {
            Id = "browser-custom-effects-facade"
            KnownKeys = [| key |]
            Observe =
                fun requested (token: CancellationToken) ->
                    task {
                        token.ThrowIfCancellationRequested ()

                        if requested <> key then
                            return None
                        else
                            let observation =
                                Capabilities.unknown
                                    key
                                    "browser-custom-effects-facade"
                                    "Ranvier facade emulation"
                                    "current document realm"
                                    "The facade observes no engine setting, policy or availability."

                            if not (current document) then
                                return Some observation
                            else
                                return
                                    Some
                                        { observation with
                                            Exported =
                                                fact
                                                    "Actual realm CustomEffect presence; the facade does not use it."
                                                    (engineHasCustomEffect document)
                                            Implemented =
                                                fact
                                                    "Implemented by the Ranvier facade (null-target KeyframeEffect progress from requestAnimationFrame), not by the engine."
                                                    (available document)
                                        }
                    }
        }
