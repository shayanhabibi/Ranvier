namespace Ranvier.Dom

open System

/// <summary>The modifier keys of a mouse or keyboard event.</summary>
[<Flags>]
type EventModifiers =
    | None = 0
    | Shift = 1
    | Control = 2
    | Alt = 4
    | Meta = 8

/// <summary>The EventInit fields shared by every constructed event.</summary>
type EventOptions =
    {
        Bubbles: bool
        Cancelable: bool
        Composed: bool
    }

    /// <summary>A non-bubbling, non-cancelable, non-composed event.</summary>
    static member Default =
        {
            Bubbles = false
            Cancelable = false
            Composed = false
        }

/// <summary>The MouseEventInit fields; <c>RelatedTarget</c> is null for none.</summary>
/// <remarks>Both measured engines, native WebCore and Chromium, expose ClientX and ClientY as integers.</remarks>
type MouseFields =
    {
        Detail: int
        ScreenX: float
        ScreenY: float
        ClientX: float
        ClientY: float
        Button: int
        Buttons: int
        Modifiers: EventModifiers
        RelatedTarget: EventTarget
    }

    /// <summary>Zero coordinates, the primary button, no buttons pressed and no related target.</summary>
    static member Default =
        {
            Detail = 0
            ScreenX = 0.0
            ScreenY = 0.0
            ClientX = 0.0
            ClientY = 0.0
            Button = 0
            Buttons = 0
            Modifiers = EventModifiers.None
            RelatedTarget = null
        }

/// <summary>The KeyboardEventInit fields.</summary>
type KeyboardFields =
    {
        Detail: int
        Key: string
        Code: string
        Location: int
        Repeat: bool
        IsComposing: bool
        Modifiers: EventModifiers
    }

    /// <summary>Empty key and code at the standard location.</summary>
    static member Default =
        {
            Detail = 0
            Key = ""
            Code = ""
            Location = 0
            Repeat = false
            IsComposing = false
            Modifiers = EventModifiers.None
        }

/// <summary>The engine constructor and init dictionary of a constructed event.</summary>
[<RequireQualifiedAccess>]
type EventConstruction =
    | Event of options: EventOptions
    | Custom of options: EventOptions * detail: TimingDetail
    | UI of options: EventOptions * detail: int
    | Mouse of options: EventOptions * fields: MouseFields
    | Keyboard of options: EventOptions * fields: KeyboardFields
    | Focus of options: EventOptions * detail: int * relatedTarget: EventTarget

/// <summary>Constructs untrusted events and standalone event targets in one document.</summary>
/// <remarks>Dispatch them with <c>EventTarget.dispatchEvent</c>; constructed events report <c>isTrusted</c> false.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type EventService =
    inherit IDisposable
    /// <summary>Constructs an event of <c>eventType</c> with the engine constructor that <c>construction</c> selects.</summary>
    /// <remarks>
    /// Rejects a custom detail containing a non-finite number, non-finite coordinates, a button outside -32768..32767,
    /// buttons outside 0..65535 and a negative location with ArgumentException. A custom detail is copied.
    /// </remarks>
    abstract Create: eventType: string * construction: EventConstruction -> Event
    /// <summary>The copied detail of a CustomEvent, or None for another event.</summary>
    abstract Detail: event: Event -> TimingDetail option
    /// <summary>Creates a standalone EventTarget that is not a node.</summary>
    abstract CreateTarget: unit -> EventTarget

/// <summary>Argument validation shared by event construction providers.</summary>
module EventContract =
    let private finite (name: string) (value: float) =
        if Double.IsNaN value || Double.IsInfinity value then
            raise (ArgumentException ($"{name} must be finite", name))

    /// <summary>Copies a detail tree whose numbers are all finite.</summary>
    let detail (value: TimingDetail) =
        let copy = TimingContract.copyDetail value

        let rec check value =
            match value with
            | TimingDetail.Number number -> finite "detail" number
            | TimingDetail.Array items -> Array.iter check items
            | TimingDetail.Object fields -> fields |> Array.iter (fun (_, item) -> check item)
            | _ -> ()

        check copy
        copy

    /// <summary>Validates and copies a construction for a provider.</summary>
    let construction (eventType: string) (value: EventConstruction) =
        if isNull eventType then
            nullArg "eventType"

        if isNull (box value) then
            nullArg "construction"

        match value with
        | EventConstruction.Custom (options, detailValue) -> EventConstruction.Custom (options, detail detailValue)
        | EventConstruction.Mouse (_, fields) ->
            for name, number in
                [
                    "ScreenX", fields.ScreenX
                    "ScreenY", fields.ScreenY
                    "ClientX", fields.ClientX
                    "ClientY", fields.ClientY
                ] do
                finite name number

            if fields.Button < -32768 || fields.Button > 32767 then
                raise (ArgumentOutOfRangeException ("Button", "Button must be a 16-bit signed integer"))

            if fields.Buttons < 0 || fields.Buttons > 65535 then
                raise (ArgumentOutOfRangeException ("Buttons", "Buttons must be a 16-bit unsigned integer"))

            value
        | EventConstruction.Keyboard (_, fields) ->
            if isNull fields.Key then
                nullArg "Key"

            if isNull fields.Code then
                nullArg "Code"

            if fields.Location < 0 then
                raise (ArgumentOutOfRangeException ("Location", "Location must not be negative"))

            value
        | other -> other
