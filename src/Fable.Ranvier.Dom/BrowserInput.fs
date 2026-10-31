namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Input state of a browser document, observed through its Window.</summary>
module BrowserInput =
    [<Emit("$0.defaultView")>]
    let private windowOf (document: Document) : EventTarget = jsNative

    [<Emit("$0.defaultView.navigator.maxTouchPoints")>]
    let private touchPoints (document: Document) : int = jsNative

    /// <summary>An input state service over <c>document</c>'s Window.</summary>
    let forDocument (document: Document) : InputStateService =
        if isNull (box document) then
            nullArg "document"

        let window = windowOf document

        if isNull (box window) then
            raise (NotSupportedException "Input state requires a document with a Window")

        InputState.create window document (fun () -> touchPoints document)

    /// <summary>The capability provider "browser-input" over <c>service</c>.</summary>
    let capabilities (service: InputStateService) : CapabilityProvider =
        InputState.capabilities
            "browser-input"
            "Window focus, blur, input and pointer capture events, document.hasFocus, :focus-visible and navigator.maxTouchPoints."
            {
                State = CapabilityState.Yes
                Reason = "The browser dispatches touch events from touch hardware."
            }
            service
