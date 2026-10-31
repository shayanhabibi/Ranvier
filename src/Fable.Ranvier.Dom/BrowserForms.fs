namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>FormData, SubmitEvent and FormDataEvent construction backed by the active document realm.</summary>
module BrowserForms =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.FormData === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("new $0.FormData()")>]
    let private empty (realm: obj) : DOMFormData = jsNative

    [<Emit("new $0.FormData($1, $2)")>]
    let private fromForm (realm: obj) (form: HTMLFormElement) (submitter: HTMLElement) : DOMFormData = jsNative

    [<Emit("new ($0[$1])($2, $3)")>]
    let private construct (realm: obj) (name: string) (eventType: string) (init: obj) : Event = jsNative

    [<Emit("$0.get($1)")>]
    let private get (formData: DOMFormData) (name: string) : obj = jsNative

    [<Emit("$0.getAll($1)")>]
    let private getAll (formData: DOMFormData) (name: string) : obj array = jsNative

    [<Emit("Array.from($0.entries())")>]
    let private entries (formData: DOMFormData) : (string * obj) array = jsNative

    [<Emit("typeof $0 === 'string'")>]
    let private isString (value: obj) : bool = jsNative

    let private value (raw: obj) =
        if isString raw then
            FormDataEntryValue.String (unbox<string> raw)
        else
            FormDataEntryValue.File (unbox<File> raw)

    let private options (value: EventOptions) =
        let raw = createEmpty<obj>
        raw?bubbles <- value.Bubbles
        raw?cancelable <- value.Cancelable
        raw?composed <- value.Composed
        raw

    /// <summary>A FormData service for <c>document</c>'s realm.</summary>
    let forDocument (document: Document) : FormDataService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "FormData requires an active document Window")

        let window = realm document
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof FormDataService))

            if not (current document) then
                raise (InvalidOperationException "The FormData document is no longer active")

        { new FormDataService with
            member _.Create() =
                check ()
                empty window

            member _.FromForm(form, submitter) =
                check ()
                fromForm window (FormDataContract.form form) submitter

            member _.Get(formData, name) =
                check ()

                match get (FormDataContract.formData formData) (FormDataContract.name name) with
                | null -> None
                | raw -> Some (value raw)

            member _.GetAll(formData, name) =
                check ()

                getAll (FormDataContract.formData formData) (FormDataContract.name name)
                |> Array.map value

            member _.Entries formData =
                check ()

                entries (FormDataContract.formData formData)
                |> Array.map (fun (name, raw) -> { Name = name; Value = value raw })

            member _.CreateSubmitEvent(eventType, init, submitter) =
                check ()
                let raw = options init
                raw?submitter <- submitter
                unbox<SubmitEvent>(construct window "SubmitEvent" (FormDataContract.eventType eventType) raw)

            member _.CreateFormDataEvent(eventType, init, formData) =
                check ()
                let raw = options init
                raw?formData <- FormDataContract.formData formData
                unbox<FormDataEvent>(construct window "FormDataEvent" (FormDataContract.eventType eventType) raw)

            member _.Dispose() =
                disposed.Value <- true
        }
