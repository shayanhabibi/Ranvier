namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Value factories backed by the active document realm's constructors.</summary>
module BrowserValueFactories =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.StaticRange === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("new ($0[$1])()")>]
    let private construct0 (realm: obj) (name: string) : obj = jsNative

    [<Emit("new ($0[$1])($2)")>]
    let private construct1 (realm: obj) (name: string) (argument: obj) : obj = jsNative

    [<Emit("new ($0[$1])($2, $3)")>]
    let private construct2 (realm: obj) (name: string) (first: obj) (second: obj) : obj = jsNative

    [<Emit("$0.Document.parseHTMLUnsafe($1)")>]
    let private parseHtml (realm: obj) (html: string) : Document = jsNative

    [<Emit("$0[$1][$2](new ($0[$3])($4))")>]
    let private fromArray (realm: obj) (cls: string) (factory: string) (arrayType: string) (values: obj) : obj = jsNative

    [<Emit("(e => ({ name: e && e.name || 'Error', message: e && e.message || String(e) }))($0)")>]
    let private describe (error: obj) : obj = jsNative

    let private options (value: EventOptions) =
        let raw = createEmpty<obj>
        raw?bubbles <- value.Bubbles
        raw?cancelable <- value.Cancelable
        raw?composed <- value.Composed
        raw

    let private mouse (raw: obj) (fields: MouseFields) =
        raw?detail <- fields.Detail
        raw?screenX <- fields.ScreenX
        raw?screenY <- fields.ScreenY
        raw?clientX <- fields.ClientX
        raw?clientY <- fields.ClientY
        raw?button <- fields.Button
        raw?buttons <- fields.Buttons
        raw?relatedTarget <- fields.RelatedTarget
        raw?shiftKey <- fields.Modifiers.HasFlag EventModifiers.Shift
        raw?ctrlKey <- fields.Modifiers.HasFlag EventModifiers.Control
        raw?altKey <- fields.Modifiers.HasFlag EventModifiers.Alt
        raw?metaKey <- fields.Modifiers.HasFlag EventModifiers.Meta

    /// <summary>A value factory service for <c>document</c>'s realm.</summary>
    /// <remarks>An engine exception raises ValueFactoryException with its name and message.</remarks>
    let forDocument (document: Document) : ValueFactoryService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Value factories require an active document Window")

        let window = realm document
        let disposed = ref false

        let run (action: unit -> obj) : obj =
            if disposed.Value then
                raise (ObjectDisposedException (nameof ValueFactoryService))

            if not (current document) then
                raise (InvalidOperationException "The value factory document is no longer active")

            try
                action ()
            with error ->
                let described = describe (box error)
                raise (ValueFactoryException (described?name, described?message))

        { new ValueFactoryService with
            member _.CreateText data =
                let data = ValueFactoryContract.text "data" data
                unbox<Text>(run (fun () -> construct1 window "Text" data))

            member _.CreateComment data =
                let data = ValueFactoryContract.text "data" data
                unbox<Comment>(run (fun () -> construct1 window "Comment" data))

            member _.CreateDocumentFragment() =
                unbox<DocumentFragment>(run (fun () -> construct0 window "DocumentFragment"))

            member _.CreateRange() =
                unbox<Range>(run (fun () -> construct0 window "Range"))

            member _.CreateStaticRange(startContainer, startOffset, endContainer, endOffset) =
                let raw = createEmpty<obj>
                raw?startContainer <- ValueFactoryContract.node "startContainer" startContainer
                raw?startOffset <- startOffset
                raw?endContainer <- ValueFactoryContract.node "endContainer" endContainer
                raw?endOffset <- endOffset
                unbox<StaticRange>(run (fun () -> construct1 window "StaticRange" raw))

            member _.CreateDocument() =
                unbox<Document>(run (fun () -> construct0 window "Document"))

            member _.ParseHtmlUnsafe html =
                let html = ValueFactoryContract.text "html" html
                unbox<Document>(run (fun () -> box (parseHtml window html)))

            member _.CreateInputEvent(eventType, init, fields) =
                let fields = ValueFactoryContract.input eventType fields
                let raw = options init
                raw?detail <- fields.Detail
                raw?data <- fields.Data
                raw?isComposing <- fields.IsComposing
                raw?inputType <- fields.InputType
                unbox<InputEvent>(run (fun () -> construct2 window "InputEvent" eventType raw))

            member _.CreateWheelEvent(eventType, init, pointer, fields) =
                let pointer = ValueFactoryContract.mouse eventType init pointer
                let fields = ValueFactoryContract.wheel fields
                let raw = options init
                mouse raw pointer
                raw?deltaX <- fields.DeltaX
                raw?deltaY <- fields.DeltaY
                raw?deltaZ <- fields.DeltaZ
                raw?deltaMode <- fields.DeltaMode
                raw?wheelDeltaX <- fields.WheelDeltaX
                raw?wheelDeltaY <- fields.WheelDeltaY
                unbox<WheelEvent>(run (fun () -> construct2 window "WheelEvent" eventType raw))

            member _.CreatePointerEvent(eventType, init, pointer, fields) =
                let pointer = ValueFactoryContract.mouse eventType init pointer
                let fields = ValueFactoryContract.pointer fields
                let raw = options init
                mouse raw pointer
                raw?pointerId <- fields.PointerId
                raw?width <- fields.Width
                raw?height <- fields.Height
                raw?pressure <- fields.Pressure
                raw?tangentialPressure <- fields.TangentialPressure

                fields.TiltX
                |> Option.iter (fun value -> raw?tiltX <- value)

                fields.TiltY
                |> Option.iter (fun value -> raw?tiltY <- value)

                raw?twist <- fields.Twist
                raw?pointerType <- fields.PointerType
                raw?isPrimary <- fields.IsPrimary
                raw?coalescedEvents <- fields.CoalescedEvents
                raw?predictedEvents <- fields.PredictedEvents
                unbox<PointerEvent>(run (fun () -> construct2 window "PointerEvent" eventType raw))

            member _.MatrixFromFloat32Array values =
                let values = ValueFactoryContract.values values
                unbox<DOMMatrix>(run (fun () -> fromArray window "DOMMatrix" "fromFloat32Array" "Float32Array" (box values)))

            member _.MatrixFromFloat64Array values =
                let values = ValueFactoryContract.values values
                unbox<DOMMatrix>(run (fun () -> fromArray window "DOMMatrix" "fromFloat64Array" "Float64Array" (box values)))

            member _.ReadOnlyMatrixFromFloat32Array values =
                let values = ValueFactoryContract.values values
                unbox<DOMMatrixReadOnly>(run (fun () -> fromArray window "DOMMatrixReadOnly" "fromFloat32Array" "Float32Array" (box values)))

            member _.ReadOnlyMatrixFromFloat64Array values =
                let values = ValueFactoryContract.values values
                unbox<DOMMatrixReadOnly>(run (fun () -> fromArray window "DOMMatrixReadOnly" "fromFloat64Array" "Float64Array" (box values)))

            member _.Dispose() =
                disposed.Value <- true
        }
