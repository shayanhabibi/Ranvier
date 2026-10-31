namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Browser providers for Web Animations constructors in a document's active realm.</summary>
module BrowserAnimations =
    [<Emit("""(() => {
        try { const realm=$0.defaultView; return realm!=null && realm.document===$0; }
        catch { return false; }
    })()""")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("""(() => {
        const r=$0.defaultView;
        return r!=null && ['KeyframeEffect','Animation','DocumentTimeline'].every(name => typeof r[name]==='function');
    })()""")>]
    let private available (document: Document) : bool = jsNative

    // The browser's keyframe objects: each property by name, then offset, easing and composite when present.
    [<Emit("""($0==null?null:$0.map((f)=>{const r={};for(const p of f.properties??[])r[p.name]=p.value;
        if(f.offset!=null)r.offset=f.offset;if(f.easing!=null)r.easing=f.easing;if(f.composite!=null)r.composite=f.composite;return r;}))""")>]
    let private keyframes (value: Keyframe array) : obj = jsNative

    // The browser's options dictionary: each field, with the duration union's value unwrapped.
    [<Emit("""(($0)==null?undefined:(()=>{const o=$0,r={};
        for(const k of ['delay','endDelay','fill','iterationStart','iterations','direction','easing','iterationComposite','composite','pseudoElement'])
            if(o[k]!==undefined)r[k]=o[k];
        const d=o.duration;r.duration=d!=null&&typeof d==='object'&&Array.isArray(d.fields)?d.fields[0]:d;return r;})())""")>]
    let private effectOptions (value: KeyframeEffectOptions) : obj = jsNative

    [<Emit("new $0.defaultView.KeyframeEffect($1,$2,$3)")>]
    let private effect (document: Document) (target: Element) (keyframes: obj) (options: obj) : KeyframeEffect = jsNative

    [<Emit("new $0.defaultView.KeyframeEffect($1)")>]
    let private copy (document: Document) (source: KeyframeEffect) : KeyframeEffect = jsNative

    [<Emit("new $0.defaultView.Animation($1)")>]
    let private animation (document: Document) (effect: AnimationEffect) : WebAnimation = jsNative

    [<Emit("new $0.defaultView.Animation($1,$2)")>]
    let private animationOn (document: Document) (effect: AnimationEffect) (timeline: AnimationTimeline) : WebAnimation = jsNative

    [<Emit("new $0.defaultView.DocumentTimeline({originTime:$1})")>]
    let private timeline (document: Document) (originTime: float) : DocumentTimeline = jsNative

    [<Emit("$0.name || 'Error'")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("$0.message || String($0)")>]
    let private errorMessage (error: obj) : string = jsNative

    /// <summary>The Web Animations constructors of the document's realm.</summary>
    let forDocument (document: Document) : AnimationService =
        if isNull (box document) then
            nullArg (nameof document)

        if not (current document) || not (available document) then
            raise (NotSupportedException "Web Animations require an active document with the animation constructors")

        let mutable disposed = false

        let active () =
            if disposed then
                raise (ObjectDisposedException "AnimationService")

            if not (current document) then
                raise (InvalidOperationException "The animation document is no longer active")

        let engine action =
            try
                action ()
            with error ->
                raise (AnimationException (errorName (box error), errorMessage (box error)))

        { new AnimationService with
            member _.CreateKeyframeEffect(target, frames, ?options) =
                active ()
                engine (fun () -> effect document target (keyframes frames) (effectOptions (defaultArg options null)))

            member _.CreateKeyframeEffectFor(target, frames, duration) =
                active ()
                engine (fun () -> effect document target (keyframes frames) (box duration))

            member _.CopyKeyframeEffect source =
                active ()

                if isNull (box source) then
                    nullArg (nameof source)

                engine (fun () -> copy document source)

            member _.CreateAnimation(?effect, ?timeline) =
                active ()
                let effect = defaultArg effect null

                match timeline with
                | Some timeline when not (isNull (box timeline)) -> engine (fun () -> animationOn document effect timeline)
                | _ -> engine (fun () -> animation document effect)

            member _.CreateAnimationWithoutTimeline?effect =
                active ()
                engine (fun () -> animationOn document (defaultArg effect null) null)

            member _.CreateDocumentTimeline?originTime =
                active ()
                engine (fun () -> timeline document (defaultArg originTime 0.))

          interface IDisposable with
              member _.Dispose() =
                  disposed <- true
        }
