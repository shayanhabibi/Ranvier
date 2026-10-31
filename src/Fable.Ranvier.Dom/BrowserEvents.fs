namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Event construction backed by the active document realm's event constructors and EventTarget.</summary>
module BrowserEvents =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.CustomEvent === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("new ($0[$1])($2, $3)")>]
    let private construct (realm: obj) (name: string) (eventType: string) (init: obj) : Event = jsNative

    [<Emit("new $0.EventTarget()")>]
    let private newTarget (realm: obj) : EventTarget = jsNative

    [<Emit("$0 instanceof $1.CustomEvent")>]
    let private isCustom (value: obj) (realm: obj) : bool = jsNative

    [<Emit("$0.detail")>]
    let private detailOf (value: obj) : obj = jsNative

    let private options (value: EventOptions) =
        let raw = createEmpty<obj>
        raw?bubbles <- value.Bubbles
        raw?cancelable <- value.Cancelable
        raw?composed <- value.Composed
        raw

    let private modifiers (raw: obj) (value: EventModifiers) =
        raw?shiftKey <- value.HasFlag EventModifiers.Shift
        raw?ctrlKey <- value.HasFlag EventModifiers.Control
        raw?altKey <- value.HasFlag EventModifiers.Alt
        raw?metaKey <- value.HasFlag EventModifiers.Meta

    /// <summary>An event construction domain for <c>document</c>'s realm.</summary>
    let forDocument (document: Document) : EventService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Event construction requires an active document Window")

        let window = realm document
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof EventService))

            if not (current document) then
                raise (InvalidOperationException "The event document is no longer active")

        { new EventService with
            member _.Create(eventType, construction) =
                check ()

                match EventContract.construction eventType construction with
                | EventConstruction.Event value -> construct window "Event" eventType (options value)
                | EventConstruction.Custom (value, detail) ->
                    let raw = options value
                    raw?detail <- Timing.toRaw detail
                    construct window "CustomEvent" eventType raw
                | EventConstruction.UI (value, detail) ->
                    let raw = options value
                    raw?detail <- detail
                    construct window "UIEvent" eventType raw
                | EventConstruction.Mouse (value, fields) ->
                    let raw = options value
                    raw?detail <- fields.Detail
                    raw?screenX <- fields.ScreenX
                    raw?screenY <- fields.ScreenY
                    raw?clientX <- fields.ClientX
                    raw?clientY <- fields.ClientY
                    raw?button <- fields.Button
                    raw?buttons <- fields.Buttons
                    raw?relatedTarget <- fields.RelatedTarget
                    modifiers raw fields.Modifiers
                    construct window "MouseEvent" eventType raw
                | EventConstruction.Keyboard (value, fields) ->
                    let raw = options value
                    raw?detail <- fields.Detail
                    raw?key <- fields.Key
                    raw?code <- fields.Code
                    raw?location <- fields.Location
                    raw?repeat <- fields.Repeat
                    raw?isComposing <- fields.IsComposing
                    modifiers raw fields.Modifiers
                    construct window "KeyboardEvent" eventType raw
                | EventConstruction.Focus (value, detail, related) ->
                    let raw = options value
                    raw?detail <- detail
                    raw?relatedTarget <- related
                    construct window "FocusEvent" eventType raw

            member _.Detail event =
                check ()

                if isNull (box event) then
                    nullArg "event"

                if isCustom event window then
                    Some (Timing.fromRaw (detailOf event))
                else
                    None

            member _.CreateTarget() =
                check ()
                newTarget window

            member _.Dispose() =
                disposed.Value <- true
        }
