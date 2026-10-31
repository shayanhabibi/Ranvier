namespace Ranvier.Dom

open System
open System.Text

/// <summary>A part of a Blob or File.</summary>
[<RequireQualifiedAccess>]
type BlobPart =
    /// <summary>Bytes, copied when the Blob or File is constructed.</summary>
    | Bytes of bytes: byte array
    /// <summary>The bytes of another Blob or File of the same provider.</summary>
    | Blob of blob: Blob
    /// <summary>Text, written as UTF-8. A lone surrogate is written as U+FFFD.</summary>
    | Text of text: string

/// <summary>How the line breaks of a Text part are written.</summary>
[<RequireQualifiedAccess>]
type BlobLineEndings =
    /// <summary>The line breaks are written unchanged.</summary>
    | Transparent
    /// <summary>Every CR, LF and CRLF is written as the platform's line break: CRLF on Windows, LF elsewhere.</summary>
    | Native

/// <summary>The type and line endings of a new Blob.</summary>
/// <remarks>A type with a character outside U+0020 to U+007E becomes the empty type; any other type is lowercased.</remarks>
type BlobOptions =
    {
        Type: string
        Endings: BlobLineEndings
    }

    /// <summary>The empty type, with transparent line endings.</summary>
    static member Default =
        {
            Type = ""
            Endings = BlobLineEndings.Transparent
        }

/// <summary>The type, line endings and modification time of a new File.</summary>
/// <remarks><c>LastModified</c> is in milliseconds since the Unix epoch; None is the time of construction.</remarks>
type FileOptions =
    {
        Type: string
        Endings: BlobLineEndings
        LastModified: int64 option
    }

    /// <summary>The empty type, transparent line endings and the time of construction.</summary>
    static member Default =
        {
            Type = ""
            Endings = BlobLineEndings.Transparent
            LastModified = None
        }

/// <summary>Constructs Blob, File and FileList values in one document, and reads their bytes.</summary>
/// <remarks>
/// The values are immutable. <c>Blob.slice</c>, <c>File.name</c> and <c>File.lastModified</c> are members of the values
/// themselves. A value outlives the service and its document.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type BinaryValueService =
    inherit IDisposable
    /// <summary>A Blob holding the bytes of <c>parts</c> in order.</summary>
    abstract CreateBlob: parts: BlobPart array * options: BlobOptions -> Blob
    /// <summary>A File named <c>name</c> holding the bytes of <c>parts</c> in order.</summary>
    abstract CreateFile: parts: BlobPart array * name: string * options: FileOptions -> File
    /// <summary>A FileList holding <c>files</c> in order, for <c>HTMLInputElement.files</c>.</summary>
    abstract CreateFileList: files: File array -> FileList
    /// <summary>Reads the bytes of <c>blob</c>.</summary>
    /// <remarks>
    /// <c>terminal</c> runs exactly once on the service's owner context, after ReadBytes returns. Cancelling the
    /// operation before the bytes arrive delivers <c>ResourceFailure.Cancelled</c>.
    /// </remarks>
    abstract ReadBytes: blob: Blob * terminal: (ResourceOutcome<byte array> -> unit) -> ResourceOperation<byte array>

/// <summary>Argument validation and the byte decodings shared by binary value providers.</summary>
module BinaryValueContract =
    /// <summary>Raises ArgumentNullException for a null array or a null part, Blob, byte array or text.</summary>
    let parts (value: BlobPart array) =
        if isNull value then
            nullArg "parts"

        for part in value do
            match part with
            | BlobPart.Bytes bytes when isNull bytes -> nullArg "parts"
            | BlobPart.Blob blob when isNull (box blob) -> nullArg "parts"
            | BlobPart.Text text when isNull text -> nullArg "parts"
            | _ when isNull (box part) -> nullArg "parts"
            | _ -> ()

        value

    /// <summary>Raises ArgumentNullException for a null type.</summary>
    let blobOptions (value: BlobOptions) =
        if isNull (box value) || isNull value.Type then
            nullArg "options"

        value

    /// <summary>Raises ArgumentNullException for a null type.</summary>
    let fileOptions (value: FileOptions) =
        if isNull (box value) || isNull value.Type then
            nullArg "options"

        value

    /// <summary>Raises ArgumentNullException for a null name.</summary>
    let name (value: string) =
        if isNull value then
            nullArg "name"

        value

    /// <summary>Raises ArgumentNullException for a null array or a null File.</summary>
    let files (value: File array) =
        if
            isNull value
            || value
               |> Array.exists (fun file -> isNull (box file))
        then
            nullArg "files"

        value

    /// <summary>Raises ArgumentNullException for a null Blob.</summary>
    let blob (value: Blob) =
        if isNull (box value) then
            nullArg "blob"

        value

#if FABLE_COMPILER
    [<Fable.Core.Emit("new TextDecoder().decode($0)")>]
    let private decode (bytes: byte array) : string =
        Fable.Core.Util.jsNative
#else
    let private decoder = UTF8Encoding (false, false)

    let private decode (bytes: byte array) =
        let start =
            if
                bytes.Length >= 3
                && bytes[0] = 0xEFuy
                && bytes[1] = 0xBBuy
                && bytes[2] = 0xBFuy
            then
                3
            else
                0

        decoder.GetString (bytes, start, bytes.Length - start)
#endif

    /// <summary>The text of <c>bytes</c>, as <c>Blob.text()</c> and <c>FileReader.readAsText</c> decode it.</summary>
    /// <remarks>UTF-8 without a leading byte order mark; each invalid sequence becomes U+FFFD.</remarks>
    let text (bytes: byte array) : string =
        if isNull bytes then
            nullArg "bytes"

        decode bytes

    /// <summary>The data URL of <c>bytes</c> with the type <c>blobType</c>, as <c>FileReader.readAsDataURL</c> writes it.</summary>
    /// <remarks>The empty type is written as <c>application/octet-stream</c>.</remarks>
    let dataUrl (blobType: string) (bytes: byte array) : string =
        if isNull blobType then
            nullArg "blobType"

        if isNull bytes then
            nullArg "bytes"

        let mediaType =
            if blobType = "" then
                "application/octet-stream"
            else
                blobType

        "data:"
        + mediaType
        + ";base64,"
        + Convert.ToBase64String bytes
