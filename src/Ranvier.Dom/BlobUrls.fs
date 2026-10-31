namespace Ranvier.Dom

open System

/// <summary>The value of <c>FileReader.result</c>.</summary>
[<RequireQualifiedAccess>]
type FileReaderResult =
    /// <summary>No result: before a read completes, and after an abort or an error.</summary>
    | Empty
    /// <summary>The string of <c>readAsText</c>, <c>readAsBinaryString</c> or <c>readAsDataURL</c>.</summary>
    | Text of text: string
    /// <summary>The ArrayBuffer of <c>readAsArrayBuffer</c>, copied.</summary>
    | Bytes of bytes: byte array

/// <summary>
/// Object URLs, reads of blobs and blob URLs, and FileReader for one document (Ranvier.WebCore #144).
/// </summary>
/// <remarks>
/// <c>CreateObjectUrl</c> returns <c>blob:&lt;origin&gt;/&lt;UUID&gt;</c>, with <c>null</c> as the origin of an
/// opaque document. A URL loads in the document that created it, for an image source or <c>ReadUrl</c>, until it is
/// revoked or the document is replaced. Terminal callbacks run on the service's owner context, after the starting
/// call returns. A FileReader dispatches <c>loadstart</c>, <c>progress</c>, <c>load</c>, <c>abort</c>,
/// <c>error</c> and <c>loadend</c> through its own listeners; the number of <c>progress</c> events depends on how the
/// engine delivers the bytes.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type BlobUrlService =
    inherit IDisposable
    /// <summary><c>URL.createObjectURL(blob)</c>.</summary>
    abstract CreateObjectUrl: blob: Blob -> string
    /// <summary><c>URL.revokeObjectURL(url)</c>; an unknown or malformed URL is ignored.</summary>
    abstract RevokeObjectUrl: url: string -> unit
    /// <summary>
    /// Reads the type and bytes of the blob a blob URL refers to. <c>ResourceFailure.LoadFailed</c> for a revoked,
    /// unknown or other-origin URL.
    /// </summary>
    /// <exception cref="T:System.ArgumentException"><c>url</c> is not a blob URL.</exception>
    abstract ReadUrl: url: string * terminal: (ResourceOutcome<ResourceResponse> -> unit) -> ResourceOperation<ResourceResponse>
    /// <summary><c>blob.text()</c>: the bytes decoded as UTF-8 without a leading byte order mark.</summary>
    abstract ReadText: blob: Blob * terminal: (ResourceOutcome<string> -> unit) -> ResourceOperation<string>
    /// <summary><c>blob.arrayBuffer()</c> and <c>blob.bytes()</c>.</summary>
    abstract ReadBytes: blob: Blob * terminal: (ResourceOutcome<byte array> -> unit) -> ResourceOperation<byte array>
    /// <summary><c>new FileReader()</c> in the document.</summary>
    abstract CreateFileReader: unit -> FileReader
    /// <summary><c>reader.result</c>.</summary>
    abstract Result: reader: FileReader -> FileReaderResult
    /// <summary>The name of <c>reader.error</c>, such as <c>AbortError</c> or <c>NotFoundError</c>.</summary>
    abstract Error: reader: FileReader -> string option

/// <summary>Argument validation shared by blob URL providers.</summary>
module BlobUrlContract =
    /// <summary>Raises ArgumentNullException for a null Blob.</summary>
    let blob (value: Blob) =
        if isNull (box value) then
            nullArg "blob"

        value

    /// <summary>Raises ArgumentNullException for a null FileReader.</summary>
    let reader (value: FileReader) =
        if isNull (box value) then
            nullArg "reader"

        value

    /// <summary>Raises ArgumentNullException for a null URL.</summary>
    let url (value: string) =
        if isNull value then
            nullArg "url"

        value

    /// <summary>Raises ArgumentNullException for a null URL and ArgumentException for a URL outside the blob scheme.</summary>
    let blobUrl (value: string) =
        if isNull value then
            nullArg "url"

        if not (value.StartsWith ("blob:", StringComparison.OrdinalIgnoreCase)) then
            invalidArg "url" "The URL is not a blob URL"

        value
