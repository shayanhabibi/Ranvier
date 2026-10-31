namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>ToggleEvent and CommandEvent construction backed by the active document realm.</summary>
module BrowserToggleEvents =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.ToggleEvent === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("typeof $0[$1] === 'function'")>]
    let private exposes (realm: obj) (name: string) : bool = jsNative

    [<Emit("new ($0[$1])($2, $3)")>]
    let private construct (realm: obj) (name: string) (eventType: string) (init: obj) : Event = jsNative

    let private options (value: EventOptions) (source: Element) =
        let raw = createEmpty<obj>
        raw?bubbles <- value.Bubbles
        raw?cancelable <- value.Cancelable
        raw?composed <- value.Composed
        raw?source <- source
        raw

    /// <summary>A toggle event service for <c>document</c>'s realm.</summary>
    /// <remarks>CreateCommandEvent raises NotSupportedException in a realm without CommandEvent.</remarks>
    let forDocument (document: Document) : ToggleEventService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "ToggleEvent requires an active document Window")

        let window = realm document
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof ToggleEventService))

            if not (current document) then
                raise (InvalidOperationException "The ToggleEvent document is no longer active")

        { new ToggleEventService with
            member _.CreateToggleEvent(eventType, init, fields) =
                check ()
                let raw = options init fields.Source
                raw?oldState <- ToggleEventContract.text "OldState" fields.OldState
                raw?newState <- ToggleEventContract.text "NewState" fields.NewState
                unbox<ToggleEvent>(construct window "ToggleEvent" (ToggleEventContract.eventType eventType) raw)

            member _.CreateCommandEvent(eventType, init, fields) =
                check ()

                if not (exposes window "CommandEvent") then
                    raise (NotSupportedException "This realm has no CommandEvent")

                let raw = options init fields.Source
                raw?command <- ToggleEventContract.text "Command" fields.Command
                unbox<CommandEvent>(construct window "CommandEvent" (ToggleEventContract.eventType eventType) raw)

            member _.Dispose() =
                disposed.Value <- true
        }
