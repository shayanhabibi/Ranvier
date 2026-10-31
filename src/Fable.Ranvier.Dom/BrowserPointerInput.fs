namespace Ranvier.Dom

open Fable.Core

/// <summary>Touch geometry and pointer lock of a browser document.</summary>
module BrowserPointerInput =
    [<Emit("({ RadiusX: $0.radiusX, RadiusY: $0.radiusY, RotationAngle: $0.rotationAngle, Force: $0.force })")>]
    let private geometry (touch: Touch) : TouchGeometry = jsNative

    [<Emit("$0.requestPointerLock($1 ? { unadjustedMovement: true } : undefined)")>]
    let private requestLock (element: Element, unadjustedMovement: bool) : obj = jsNative

    [<Emit("$0 && typeof $0.catch === 'function' && $0.catch(() => undefined)")>]
    let private ignoreRejection (promise: obj) : unit = jsNative

    [<Emit("$0.exitPointerLock()")>]
    let private exitLock (document: Document) : unit = jsNative

    [<Emit("$0.pointerLockElement")>]
    let private lockElement (document: Document) : Element = jsNative

    /// <summary>The provider of <c>document</c>. A browser rejects a request through its promise, after the call
    /// returns, so a refusal arrives only as <c>PointerLockChange.Error</c>, and a request never reports
    /// <c>Pending</c>.</summary>
    let provider (document: Document) : PointerInputProvider =
        if isNull (box document) then
            nullArg "document"

        {
            TouchGeometry = geometry
            RequestLock =
                fun (element, unadjustedMovement) ->
                    ignoreRejection (requestLock (element, unadjustedMovement))
                    Ok ()
            ExitLock = fun () -> exitLock document
            LockState =
                fun () ->
                    match lockElement document with
                    | null -> PointerLockState.Unlocked
                    | element -> PointerLockState.Locked element
        }

    /// <summary>A pointer lock service over <c>document</c>.</summary>
    let lockForDocument (document: Document) : PointerLockService =
        PointerInput.lock (provider document) document

    /// <summary>The capability provider "browser-pointer-input".</summary>
    let capabilities () : CapabilityProvider =
        let yes reason =
            {
                State = CapabilityState.Yes
                Reason = reason
            }

        PointerInput.capabilities
            "browser-pointer-input"
            "Touch events, PointerEvent pen fields and Element.requestPointerLock of the browser."
            (yes "The browser dispatches touch events from touch hardware.")
            (yes "The browser reports pen pressure, tilt and twist from pen hardware.")
            (yes "The browser implements Element.requestPointerLock.")
