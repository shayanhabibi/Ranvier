namespace Ranvier.Dom

open System

/// <summary>The InputEventInit fields; a null <c>Data</c> is a null data.</summary>
type InputFields =
    {
        Detail: int
        Data: string
        IsComposing: bool
        InputType: string
    }

    /// <summary>Null data, not composing and an empty input type.</summary>
    static member Default =
        {
            Detail = 0
            Data = null
            IsComposing = false
            InputType = ""
        }

/// <summary>The WheelEventInit fields beyond MouseEventInit.</summary>
/// <remarks>A zero delta with a nonzero legacy wheel delta takes the delta the engine derives from it.</remarks>
type WheelFields =
    {
        DeltaX: float
        DeltaY: float
        DeltaZ: float
        DeltaMode: uint32
        WheelDeltaX: int
        WheelDeltaY: int
    }

    /// <summary>Zero deltas in pixel mode.</summary>
    static member Default =
        {
            DeltaX = 0.0
            DeltaY = 0.0
            DeltaZ = 0.0
            DeltaMode = 0u
            WheelDeltaX = 0
            WheelDeltaY = 0
        }

/// <summary>The PointerEventInit fields beyond MouseEventInit; None omits a tilt component.</summary>
type PointerFields =
    {
        PointerId: int
        Width: float
        Height: float
        Pressure: float32
        TangentialPressure: float32
        TiltX: int option
        TiltY: int option
        Twist: int
        PointerType: string
        IsPrimary: bool
        CoalescedEvents: PointerEvent array
        PredictedEvents: PointerEvent array
    }

    /// <summary>The IDL defaults: pointer 0, a 1 by 1 contact, no pressure or tilt, an empty type and no sub-events.</summary>
    static member Default =
        {
            PointerId = 0
            Width = 1.0
            Height = 1.0
            Pressure = 0.0f
            TangentialPressure = 0.0f
            TiltX = None
            TiltY = None
            Twist = 0
            PointerType = ""
            IsPrimary = false
            CoalescedEvents = [||]
            PredictedEvents = [||]
        }

/// <summary>An engine exception raised by a value factory, preserving its name and message.</summary>
exception ValueFactoryException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>Constructs standalone nodes, ranges, documents, events and matrices in one document's context.</summary>
/// <remarks>
/// Nodes and ranges belong to that document and start disconnected; a node inserted into another document is adopted.
/// Events are untrusted; dispatch them with <c>EventTarget.dispatchEvent</c>. Native providers reject nodes, targets
/// and events of another host session with ArgumentException.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ValueFactoryService =
    inherit IDisposable
    /// <summary>A Text node holding <c>data</c>.</summary>
    abstract CreateText: data: string -> Text
    /// <summary>A Comment node holding <c>data</c>.</summary>
    abstract CreateComment: data: string -> Comment
    /// <summary>An empty DocumentFragment.</summary>
    abstract CreateDocumentFragment: unit -> DocumentFragment
    /// <summary>A Range collapsed at the start of the document.</summary>
    abstract CreateRange: unit -> Range
    /// <summary>A StaticRange over the given boundary points; offsets past a node's length are kept as given.</summary>
    /// <remarks>An Attr or DocumentType container raises ValueFactoryException InvalidNodeTypeError.</remarks>
    abstract CreateStaticRange: startContainer: Node * startOffset: uint32 * endContainer: Node * endOffset: uint32 -> StaticRange
    /// <summary>A new empty XML Document with the context document's origin and without a browsing context.</summary>
    abstract CreateDocument: unit -> Document
    /// <summary>A new HTML Document parsed from <c>html</c> with scripts inert; declarative shadow roots attach.</summary>
    abstract ParseHtmlUnsafe: html: string -> Document
    /// <summary>An untrusted InputEvent of <c>eventType</c>.</summary>
    abstract CreateInputEvent: eventType: string * options: EventOptions * fields: InputFields -> InputEvent
    /// <summary>An untrusted WheelEvent of <c>eventType</c>.</summary>
    abstract CreateWheelEvent: eventType: string * options: EventOptions * mouse: MouseFields * fields: WheelFields -> WheelEvent
    /// <summary>An untrusted PointerEvent of <c>eventType</c>.</summary>
    abstract CreatePointerEvent: eventType: string * options: EventOptions * mouse: MouseFields * fields: PointerFields -> PointerEvent
    /// <summary>A mutable matrix of six (2D) or sixteen (3D) single-precision elements, widened to double.</summary>
    /// <remarks>Another length raises ValueFactoryException TypeError.</remarks>
    abstract MatrixFromFloat32Array: values: float32 array -> DOMMatrix
    /// <summary>A mutable matrix of six (2D) or sixteen (3D) elements.</summary>
    /// <remarks>Another length raises ValueFactoryException TypeError.</remarks>
    abstract MatrixFromFloat64Array: values: float array -> DOMMatrix
    /// <summary>A read-only matrix of six or sixteen single-precision elements, widened to double.</summary>
    abstract ReadOnlyMatrixFromFloat32Array: values: float32 array -> DOMMatrixReadOnly
    /// <summary>A read-only matrix of six or sixteen elements.</summary>
    abstract ReadOnlyMatrixFromFloat64Array: values: float array -> DOMMatrixReadOnly

/// <summary>Argument validation shared by value factory providers.</summary>
module ValueFactoryContract =
    let private finite (name: string) (value: float) =
        if Double.IsNaN value || Double.IsInfinity value then
            invalidArg name "An IDL double is finite"

    /// <summary>Raises ArgumentNullException for a null string.</summary>
    let text (name: string) (value: string) =
        if isNull value then
            nullArg name

        value

    /// <summary>Raises ArgumentNullException for a null node.</summary>
    let node (name: string) (value: Node) =
        if isNull (box value) then
            nullArg name

        value

    /// <summary>Raises ArgumentNullException for a null array.</summary>
    let values (value: 'T array) =
        if isNull value then
            nullArg "values"

        value

    /// <summary>Validates the event type and the MouseEventInit fields as the event construction contract does.</summary>
    let mouse (eventType: string) (options: EventOptions) (fields: MouseFields) =
        if isNull (box fields) then
            nullArg "mouse"

        EventContract.construction eventType (EventConstruction.Mouse (options, fields))
        |> ignore

        fields

    /// <summary>Validates the InputEventInit fields; <c>Data</c> may be null.</summary>
    let input (eventType: string) (fields: InputFields) =
        text "eventType" eventType |> ignore

        if isNull (box fields) then
            nullArg "fields"

        text "InputType" fields.InputType |> ignore
        fields

    /// <summary>Validates the WheelEventInit deltas, which are finite IDL doubles.</summary>
    let wheel (fields: WheelFields) =
        if isNull (box fields) then
            nullArg "fields"

        finite "DeltaX" fields.DeltaX
        finite "DeltaY" fields.DeltaY
        finite "DeltaZ" fields.DeltaZ
        fields

    /// <summary>Validates the PointerEventInit fields: finite sizes and pressures, a type and non-null sub-events.</summary>
    let pointer (fields: PointerFields) =
        if isNull (box fields) then
            nullArg "fields"

        finite "Width" fields.Width
        finite "Height" fields.Height
        finite "Pressure" (float fields.Pressure)
        finite "TangentialPressure" (float fields.TangentialPressure)
        text "PointerType" fields.PointerType |> ignore

        for name, events in
            [
                "CoalescedEvents", fields.CoalescedEvents
                "PredictedEvents", fields.PredictedEvents
            ] do
            if isNull events then
                nullArg name

            if
                events
                |> Array.exists (fun item -> isNull (box item))
            then
                invalidArg name "Pointer sub-events are non-null"

        fields
