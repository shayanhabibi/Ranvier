namespace Ranvier.Dom

open System
open System.Collections.Generic
open System.Threading.Tasks

/// <summary>A touch contact's size, rotation and force: <c>Touch.radiusX</c>, <c>radiusY</c>, <c>rotationAngle</c> and
/// <c>force</c>, which WebKit spells with a <c>webkit</c> prefix.</summary>
type TouchGeometry =
    {
        /// <summary>CSS pixels.</summary>
        RadiusX: float
        /// <summary>CSS pixels.</summary>
        RadiusY: float
        /// <summary>Degrees.</summary>
        RotationAngle: float
        /// <summary>0 to 1.</summary>
        Force: float
    }

/// <summary>A snapshot of one <c>Touch</c> of a <c>TouchList</c>.</summary>
type TouchContact =
    {
        Identifier: int
        ClientX: float
        ClientY: float
        PageX: float
        PageY: float
        ScreenX: float
        ScreenY: float
        /// <summary>The element the contact started on, for the contact's whole life.</summary>
        Target: EventTarget
        Geometry: TouchGeometry
    }

/// <summary>The pointer-lock state of a document's page.</summary>
[<RequireQualifiedAccess>]
type PointerLockState =
    | Unlocked
    /// <summary>The element asked for the lock and the embedder has not answered.</summary>
    | Pending of element: Element
    | Locked of element: Element

/// <summary>Why the engine refused a pointer-lock request before asking the embedder.</summary>
[<RequireQualifiedAccess>]
type PointerLockRefusal =
    /// <summary><c>NotAllowedError</c>: no user gesture, and no relock allowed after an exit.</summary>
    | NotAllowed
    /// <summary><c>WrongDocumentError</c>: the element is not connected to an active document.</summary>
    | WrongDocument
    /// <summary><c>NotSupportedError</c>: unadjusted movement, which the provider does not deliver.</summary>
    | NotSupported
    /// <summary><c>InvalidStateError</c>: the lock is held in another document.</summary>
    | InvalidState
    /// <summary>A DOMException of another name, or a provider without pointer lock.</summary>
    | Other of name: string

/// <summary>A pointer-lock change, delivered once to each subscriber in the order the engine queued its event.</summary>
[<RequireQualifiedAccess>]
type PointerLockChange =
    /// <summary>A <c>pointerlockchange</c> event while an element holds the lock.</summary>
    | Locked of element: Element
    /// <summary>A <c>pointerlockchange</c> event after the lock ended.</summary>
    | Unlocked
    /// <summary>A <c>pointerlockerror</c> event: a refused or denied request.</summary>
    | Error

/// <summary>The engine operations behind pointer input services.</summary>
type PointerInputProvider =
    {
        /// <summary>The geometry of a <c>Touch</c>.</summary>
        TouchGeometry: Touch -> TouchGeometry
        /// <summary><c>Element.requestPointerLock</c>. A refusal known when the call returns is an <c>Error</c>; other
        /// outcomes arrive as <c>pointerlockchange</c> or <c>pointerlockerror</c>.</summary>
        RequestLock: Element * bool -> Result<unit, PointerLockRefusal>
        /// <summary><c>Document.exitPointerLock</c>.</summary>
        ExitLock: unit -> unit
        /// <summary>The page's lock state.</summary>
        LockState: unit -> PointerLockState
    }

/// <summary>Pointer lock of one document: requests, exit, state and changes.</summary>
/// <remarks>
/// A lock target receives every mouse and pen event with <c>clientX</c> and <c>clientY</c> held at the lock position and
/// the motion in <c>movementX</c> and <c>movementY</c>. It differs from <c>setPointerCapture</c>, which retargets one
/// pointer's events and keeps their positions. Observers run on the dispatching thread in subscription order. Disposing
/// the service removes its document listeners and its subscribers.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type PointerLockService =
    inherit IDisposable
    abstract Current: unit -> PointerLockState
    /// <summary>Requests the lock for <c>element</c>; the engine requires a user gesture.</summary>
    abstract Request: element: Element * unadjustedMovement: bool -> Result<unit, PointerLockRefusal>
    abstract Exit: unit -> unit
    /// <summary>Adds an observer and returns an idempotent removal handle.</summary>
    abstract Subscribe: observer: (PointerLockChange -> unit) -> IDisposable

/// <summary>Touch contact snapshots, pointer lock services and the pointer-input capabilities.</summary>
[<RequireQualifiedAccess>]
module PointerInput =
    /// <summary>The PlatformFacility keys of <c>capabilities</c>.</summary>
    let keys =
        [| "input.touch-events"; "input.pen-geometry"; "input.pointer-lock" |]
        |> Array.map (Capabilities.key CapabilityCategory.PlatformFacility)

    /// <summary>The contacts of <c>list</c> in list order.</summary>
    let contacts (provider: PointerInputProvider) (list: TouchList) : TouchContact array =
        if isNull (box list) then
            nullArg "list"

        Array.init (int list.length) (fun index ->
            let touch = list.item (uint32 index)

            {
                Identifier = touch.identifier
                ClientX = touch.clientX
                ClientY = touch.clientY
                PageX = touch.pageX
                PageY = touch.pageY
                ScreenX = touch.screenX
                ScreenY = touch.screenY
                Target = touch.target
                Geometry = provider.TouchGeometry touch
            })

    /// <summary>The refusal named by a DOMException name.</summary>
    let refusal (name: string) : PointerLockRefusal =
        match name with
        | "NotAllowedError" -> PointerLockRefusal.NotAllowed
        | "WrongDocumentError" -> PointerLockRefusal.WrongDocument
        | "NotSupportedError" -> PointerLockRefusal.NotSupported
        | "InvalidStateError" -> PointerLockRefusal.InvalidState
        | other -> PointerLockRefusal.Other other

    /// <summary>A pointer lock service over <c>document</c>, whose page <c>provider</c> controls.</summary>
    let lock (provider: PointerInputProvider) (document: Document) : PointerLockService =
        if isNull (box document) then
            nullArg "document"

        let mutable disposed = false
        let observers = List<int * (PointerLockChange -> unit)>()
        let mutable nextObserver = 0

        let publish change =
            for _, observer in observers.ToArray () do
                if not disposed then
                    observer change

        let onChange (_: Event) =
            match provider.LockState () with
            | PointerLockState.Locked element -> publish (PointerLockChange.Locked element)
            | _ -> publish PointerLockChange.Unlocked

        let onError (_: Event) =
            publish PointerLockChange.Error

        let listeners: (string * (Event -> unit)) array =
            [| "pointerlockchange", onChange; "pointerlockerror", onError |]

        for eventType, listener in listeners do
            document.addEventListener (eventType, listener, false)

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof PointerLockService))

        { new PointerLockService with
            member _.Current() =
                live ()
                provider.LockState ()

            member _.Request(element, unadjustedMovement) =
                live ()

                if isNull (box element) then
                    nullArg "element"

                provider.RequestLock (element, unadjustedMovement)

            member _.Exit() =
                live ()
                provider.ExitLock ()

            member _.Subscribe observer =
                live ()

                if isNull (box observer) then
                    nullArg "observer"

                let id = nextObserver
                nextObserver <- nextObserver + 1
                observers.Add ((id, observer))

                { new IDisposable with
                    member _.Dispose() =
                        observers.RemoveAll (fun (key, _) -> key = id)
                        |> ignore
                }

            member _.Dispose() =
                if not disposed then
                    disposed <- true
                    observers.Clear ()

                    for eventType, listener in listeners do
                        try
                            document.removeEventListener (eventType, listener, false)
                        with :? ObjectDisposedException ->
                            ()
        }

    /// <summary>A capability provider for <c>keys</c> with the provider's facts for each facility.</summary>
    /// <remarks>
    /// Each fact is the Implemented fact of its key; a key is Available when its fact is Yes. Touch events and pen
    /// geometry are delivered as the platform's input; pointer lock additionally needs the embedder's grant per request.
    /// </remarks>
    let capabilities
        (providerId: string)
        (mechanism: string)
        (touch: CapabilityFact)
        (pen: CapabilityFact)
        (pointerLock: CapabilityFact)
        : CapabilityProvider =
        let yes reason =
            {
                State = CapabilityState.Yes
                Reason = reason
            }

        let observe (key: CapabilityKey) =
            if not (keys |> Array.contains key) then
                None
            else
                let fact =
                    match key.Name with
                    | "input.touch-events" -> touch
                    | "input.pen-geometry" -> pen
                    | _ -> pointerLock

                let unknown = Capabilities.unknown key providerId mechanism "document"

                Some
                    { unknown "" with
                        Exported = fact
                        Implemented = fact
                        Enabled = fact
                        Permitted =
                            if key.Name = "input.pointer-lock" then
                                {
                                    State = CapabilityState.Unknown
                                    Reason = "The embedder grants or denies each request."
                                }
                            else
                                yes "No permission gates the input."
                        Available = fact
                        Value = Some (CapabilityValue.Boolean (fact.State = CapabilityState.Yes))
                    }

        {
            Id = providerId
            KnownKeys = Array.copy keys
            Observe = fun key _ -> Task.FromResult (observe key)
        }
