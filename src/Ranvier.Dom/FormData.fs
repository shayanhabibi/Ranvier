namespace Ranvier.Dom

open System

/// <summary>A FormDataEntryValue: a string, or a File.</summary>
/// <remarks><c>BinaryValueService.ReadBytes</c> reads the contents of a File.</remarks>
[<RequireQualifiedAccess>]
type FormDataEntryValue =
    | String of value: string
    | File of file: File

/// <summary>A FormData entry, in entry-list order.</summary>
type FormDataEntry =
    {
        Name: string
        Value: FormDataEntryValue
    }

/// <summary>Constructs FormData objects, SubmitEvents and FormDataEvents in one document, and reads FormData entries.</summary>
/// <remarks>
/// Append, set, delete and has are members of <c>DOMFormData</c> itself. A form-bound construction fires the
/// form's <c>formdata</c> event synchronously.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type FormDataService =
    inherit IDisposable
    /// <summary>An empty FormData.</summary>
    abstract Create: unit -> DOMFormData
    /// <summary>The entry list of <c>form</c>, with the entry of <c>submitter</c> when it is not null.</summary>
    /// <remarks>
    /// Raises the engine's TypeError for a submitter that is not a submit button, and its NotFoundError for a
    /// submitter of another form.
    /// </remarks>
    abstract FromForm: form: HTMLFormElement * submitter: HTMLElement -> DOMFormData
    /// <summary>The first value named <c>name</c>, or None.</summary>
    abstract Get: formData: DOMFormData * name: string -> FormDataEntryValue option
    /// <summary>Every value named <c>name</c>, in entry-list order.</summary>
    abstract GetAll: formData: DOMFormData * name: string -> FormDataEntryValue array
    /// <summary>A snapshot of the entry list.</summary>
    abstract Entries: formData: DOMFormData -> FormDataEntry array
    /// <summary>An untrusted SubmitEvent; <c>submitter</c> is null for none.</summary>
    abstract CreateSubmitEvent: eventType: string * options: EventOptions * submitter: HTMLElement -> SubmitEvent
    /// <summary>An untrusted FormDataEvent carrying <c>formData</c>.</summary>
    abstract CreateFormDataEvent: eventType: string * options: EventOptions * formData: DOMFormData -> FormDataEvent

/// <summary>Argument validation and value access shared by FormData providers.</summary>
module FormDataContract =
    /// <summary>Raises ArgumentNullException for a null name.</summary>
    let name (value: string) =
        if isNull value then
            nullArg "name"

        value

    /// <summary>Raises ArgumentNullException for a null FormData.</summary>
    let formData (value: DOMFormData) =
        if isNull (box value) then
            nullArg "formData"

        value

    /// <summary>Raises ArgumentNullException for a null form.</summary>
    let form (value: HTMLFormElement) =
        if isNull (box value) then
            nullArg "form"

        value

    /// <summary>Raises ArgumentNullException for a null event type.</summary>
    let eventType (value: string) =
        if isNull value then
            nullArg "eventType"

        value
