namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Event detail construction backed by the active document realm.</summary>
module BrowserEventDetails =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.ErrorEvent === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("typeof $0[$1] === 'function'")>]
    let private exposes (realm: obj) (name: string) : bool = jsNative

    [<Emit("new ($0[$1])($2, $3)")>]
    let private construct (realm: obj) (name: string) (eventType: string) (init: obj) : Event = jsNative

    let private options (value: EventOptions) =
        let raw = createEmpty<obj>
        raw?bubbles <- value.Bubbles
        raw?cancelable <- value.Cancelable
        raw?composed <- value.Composed
        raw

    let private time (name: string) (value: float option) : obj =
        match value with
        | Some number -> box (EventDetailContract.finite name number)
        | None -> null

    /// <summary>An event detail service for <c>document</c>'s realm.</summary>
    /// <remarks>A constructor the realm lacks raises NotSupportedException.</remarks>
    let forDocument (document: Document) : EventDetailService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Event details require an active document Window")

        let window = realm document
        let disposed = ref false

        let create (name: string) (eventType: string) (raw: obj) : Event =
            if disposed.Value then
                raise (ObjectDisposedException (nameof EventDetailService))

            if not (current document) then
                raise (InvalidOperationException "The event detail document is no longer active")

            if not (exposes window name) then
                raise (NotSupportedException ("This realm has no " + name))

            construct window name (EventDetailContract.eventType eventType) raw

        { new EventDetailService with
            member _.CreateErrorEvent(eventType, init, fields) =
                let raw = options init
                raw?message <- EventDetailContract.text "Message" fields.Message
                raw?filename <- EventDetailContract.text "Filename" fields.Filename
                raw?lineno <- fields.Lineno
                raw?colno <- fields.Colno
                unbox<ErrorEvent>(create "ErrorEvent" eventType raw)

            member _.CreateProgressEvent(eventType, init, fields) =
                let raw = options init
                raw?lengthComputable <- fields.LengthComputable
                raw?loaded <- EventDetailContract.finite "Loaded" fields.Loaded
                raw?total <- EventDetailContract.finite "Total" fields.Total
                unbox<ProgressEvent>(create "ProgressEvent" eventType raw)

            member _.CreateAnimationEvent(eventType, init, fields) =
                let raw = options init
                raw?animationName <- EventDetailContract.text "Name" fields.Name
                raw?elapsedTime <- EventDetailContract.finite "ElapsedTime" fields.ElapsedTime
                raw?pseudoElement <- EventDetailContract.text "PseudoElement" fields.PseudoElement
                unbox<AnimationEvent>(create "AnimationEvent" eventType raw)

            member _.CreateTransitionEvent(eventType, init, fields) =
                let raw = options init
                raw?propertyName <- EventDetailContract.text "Name" fields.Name
                raw?elapsedTime <- EventDetailContract.finite "ElapsedTime" fields.ElapsedTime
                raw?pseudoElement <- EventDetailContract.text "PseudoElement" fields.PseudoElement
                unbox<TransitionEvent>(create "TransitionEvent" eventType raw)

            member _.CreateContentVisibilityEvent(eventType, init, skipped) =
                let raw = options init
                raw?skipped <- skipped
                unbox<ContentVisibilityAutoStateChangeEvent>(create "ContentVisibilityAutoStateChangeEvent" eventType raw)

            member _.CreateAnimationPlaybackEvent(eventType, init, fields) =
                let raw = options init
                raw?currentTime <- time "CurrentTime" fields.CurrentTime
                raw?timelineTime <- time "TimelineTime" fields.TimelineTime
                unbox<AnimationPlaybackEvent>(create "AnimationPlaybackEvent" eventType raw)

            member _.CreatePageTransitionEvent(eventType, init, persisted) =
                let raw = options init
                raw?persisted <- persisted
                unbox<PageTransitionEvent>(create "PageTransitionEvent" eventType raw)

            member _.CreateHashChangeEvent(eventType, init, fields) =
                let raw = options init
                raw?oldURL <- EventDetailContract.text "OldURL" fields.OldURL
                raw?newURL <- EventDetailContract.text "NewURL" fields.NewURL
                unbox<HashChangeEvent>(create "HashChangeEvent" eventType raw)

            member _.CreateMediaQueryListEvent(eventType, init, fields) =
                let raw = options init
                raw?media <- EventDetailContract.text "Media" fields.Media
                raw?matches <- fields.Matches
                unbox<MediaQueryListEvent>(create "MediaQueryListEvent" eventType raw)

            member _.CreatePopStateEvent(eventType, init, hasUAVisualTransition) =
                let raw = options init
                raw?hasUAVisualTransition <- hasUAVisualTransition
                unbox<PopStateEvent>(create "PopStateEvent" eventType raw)

            member _.CreateCompositionEvent(eventType, init, fields) =
                let raw = options init
                raw?data <- EventDetailContract.text "Data" fields.Data
                raw?detail <- fields.Detail
                unbox<CompositionEvent>(create "CompositionEvent" eventType raw)

            member _.Dispose() =
                disposed.Value <- true
        }
