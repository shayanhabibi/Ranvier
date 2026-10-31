namespace Ranvier.Dom

open System
open System.Collections.Generic
open System.Threading.Tasks

/// <summary>The kind of the most recent trusted user input since the service was created.</summary>
[<RequireQualifiedAccess>]
type InputModality =
    | Unknown
    | Keyboard
    | Pointer

/// <summary>Live window activation, modality, focus visibility and touch capacity of one document.</summary>
type InputSnapshot =
    {
        /// <summary><c>document.hasFocus()</c>.</summary>
        Focused: bool
        Modality: InputModality
        /// <summary>The active element matches <c>:focus-visible</c>.</summary>
        FocusVisible: bool
        /// <summary><c>navigator.maxTouchPoints</c>; 0 when the engine delivers no touch input.</summary>
        MaxTouchPoints: int
    }

/// <summary>A change delivered once to each subscriber, in the order the engine dispatched its event.</summary>
[<RequireQualifiedAccess>]
type InputChange =
    /// <summary>The window gained (true) or lost (false) activation.</summary>
    | Activation of focused: bool
    | Modality of modality: InputModality
    /// <summary>A <c>gotpointercapture</c> event: <c>target</c> holds capture of the pointer.</summary>
    | CaptureGained of pointerId: int * pointerType: string * target: Element
    /// <summary>A <c>lostpointercapture</c> event: <c>target</c> released capture of the pointer.</summary>
    | CaptureLost of pointerId: int * pointerType: string * target: Element

/// <summary>Window activation, input modality and pointer capture of one document, observed through its window.</summary>
/// <remarks>
/// Trusted <c>keydown</c> without Control, Alt or Meta, other than a modifier key itself, sets <c>Keyboard</c>; trusted
/// <c>pointerdown</c> and <c>mousedown</c> set <c>Pointer</c>. Observers run on the dispatching thread in subscription
/// order. Disposing the service removes its window listeners and its subscribers; after the window's provider is
/// disposed it removes only the subscribers.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type InputStateService =
    inherit IDisposable
    /// <summary>Reads the live snapshot.</summary>
    abstract Current: unit -> InputSnapshot
    /// <summary>The element holding capture of the pointer since its last <c>gotpointercapture</c>, until its
    /// <c>lostpointercapture</c>.</summary>
    abstract Captured: pointerId: int -> Element option
    /// <summary>Adds an observer and returns an idempotent removal handle.</summary>
    abstract Subscribe: observer: (InputChange -> unit) -> IDisposable

/// <summary>Creates input state services over a window EventTarget and reports their capabilities.</summary>
[<RequireQualifiedAccess>]
module InputState =
    let private modifierKeys =
        set
            [
                "Shift"
                "Control"
                "Alt"
                "Meta"
                "AltGraph"
                "CapsLock"
                "Fn"
                "FnLock"
                "Hyper"
                "Super"
                "Symbol"
                "SymbolLock"
                "NumLock"
                "ScrollLock"
                "OS"
            ]

    /// <summary>The PlatformFacility keys of <c>capabilities</c>.</summary>
    let keys =
        [|
            "input.window-activation"
            "input.keyboard-modality"
            "input.touch-points"
            "input.pointer-capture"
        |]
        |> Array.map (Capabilities.key CapabilityCategory.PlatformFacility)

    /// <summary>A service over <c>window</c>, the window of <c>document</c>, with the provider's touch capacity.</summary>
    let create (window: EventTarget) (document: Document) (maxTouchPoints: unit -> int) : InputStateService =
        if isNull (box window) then
            nullArg "window"

        if isNull (box document) then
            nullArg "document"

        let mutable disposed = false
        let mutable modality = InputModality.Unknown
        let mutable focused = document.hasFocus ()
        let observers = List<int * (InputChange -> unit)>()
        let mutable nextObserver = 0
        let captures = Dictionary<int, Element>()

        let publish change =
            for _, observer in observers.ToArray () do
                if not disposed then
                    observer change

        let setModality value =
            if modality <> value then
                modality <- value
                publish (InputChange.Modality value)

        let activation value (event: Event) =
            if event.eventPhase = 2us then
                if focused <> value then
                    focused <- value
                    publish (InputChange.Activation value)

        let onKey (event: Event) =
            if event.isTrusted then
                let key = event :?> KeyboardEvent

                if
                    not (
                        key.ctrlKey
                        || key.altKey
                        || key.metaKey
                        || modifierKeys.Contains key.key
                    )
                then
                    setModality InputModality.Keyboard

        let onPointer (event: Event) =
            if event.isTrusted then
                setModality InputModality.Pointer

        let onCapture gained (event: Event) =
            let pointer = event :?> PointerEvent
            let target = event.target :?> Element

            if gained then
                captures[pointer.pointerId] <- target
                publish (InputChange.CaptureGained (pointer.pointerId, pointer.pointerType, target))
            else
                captures.Remove pointer.pointerId |> ignore
                publish (InputChange.CaptureLost (pointer.pointerId, pointer.pointerType, target))

        let listeners: (string * (Event -> unit) * bool) array =
            [|
                "focus", activation true, false
                "blur", activation false, false
                "keydown", onKey, true
                "pointerdown", onPointer, true
                "mousedown", onPointer, true
                "gotpointercapture", onCapture true, true
                "lostpointercapture", onCapture false, true
            |]

        for eventType, listener, capture in listeners do
            window.addEventListener (eventType, listener, capture)

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof InputStateService))

        { new InputStateService with
            member _.Current() =
                live ()
                let active = document.activeElement

                {
                    Focused = document.hasFocus ()
                    Modality = modality
                    FocusVisible =
                        not (isNull (box active))
                        && active.matches ":focus-visible"
                    MaxTouchPoints = maxTouchPoints ()
                }

            member _.Captured pointerId =
                live ()

                match captures.TryGetValue pointerId with
                | true, element -> Some element
                | _ -> None

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
                    captures.Clear ()

                    for eventType, listener, capture in listeners do
                        try
                            window.removeEventListener (eventType, listener, capture)
                        with :? ObjectDisposedException ->
                            ()
        }

    /// <summary>A capability provider for <c>keys</c>, observing <c>service</c> live.</summary>
    /// <remarks>
    /// <c>touchDelivery</c> is the Implemented fact of touch points. Touch points are Available when the capacity is
    /// positive and touch delivery is implemented. A disposed service reports every key as unknown.
    /// </remarks>
    let capabilities (providerId: string) (mechanism: string) (touchDelivery: CapabilityFact) (service: InputStateService) : CapabilityProvider =
        if isNull (box service) then
            nullArg "service"

        let yes reason =
            {
                State = CapabilityState.Yes
                Reason = reason
            }

        let no reason =
            {
                State = CapabilityState.No
                Reason = reason
            }

        let observe (key: CapabilityKey) =
            if not (keys |> Array.contains key) then
                None
            else
                let unknown = Capabilities.unknown key providerId mechanism "document"

                match
                    (try
                        Ok (service.Current ())
                     with :? ObjectDisposedException ->
                         Error ())
                with
                | Error () -> Some (unknown "The input state service is disposed.")
                | Ok snapshot ->
                    let present =
                        yes "The provider observes the input state through the document window."

                    let observation value available =
                        { unknown "" with
                            Exported = present
                            Implemented = present
                            Enabled = present
                            Permitted = yes "No permission gates the observation."
                            Available = available
                            Value = Some value
                        }

                    match key.Name with
                    | "input.window-activation" -> Some (observation (CapabilityValue.Boolean snapshot.Focused) present)
                    | "input.keyboard-modality" ->
                        let name =
                            match snapshot.Modality with
                            | InputModality.Unknown -> "unknown"
                            | InputModality.Keyboard -> "keyboard"
                            | InputModality.Pointer -> "pointer"

                        Some (observation (CapabilityValue.Text name) present)
                    | "input.touch-points" ->
                        let available =
                            if touchDelivery.State <> CapabilityState.Yes then
                                no touchDelivery.Reason
                            elif snapshot.MaxTouchPoints > 0 then
                                yes "navigator.maxTouchPoints is positive."
                            else
                                no "navigator.maxTouchPoints is 0."

                        Some
                            { observation (CapabilityValue.Number (float snapshot.MaxTouchPoints)) available with
                                Implemented = touchDelivery
                            }
                    | _ -> Some (observation (CapabilityValue.Boolean true) present)

        {
            Id = providerId
            KnownKeys = Array.copy keys
            Observe = fun key _ -> Task.FromResult (observe key)
        }
