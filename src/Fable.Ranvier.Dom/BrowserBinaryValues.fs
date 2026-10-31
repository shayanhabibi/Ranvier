namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Blob, File and FileList construction and FileReader reads backed by the active document realm.</summary>
[<RequireQualifiedAccess>]
module BrowserBinaryValues =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.Blob === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("new $0.Blob($1, $2)")>]
    let private blob (realm: obj) (parts: obj array) (options: obj) : Blob = jsNative

    [<Emit("new $0.File($1, $2, $3)")>]
    let private file (realm: obj) (parts: obj array) (name: string) (options: obj) : File = jsNative

    [<Emit("(() => { const t = new $0.DataTransfer(); for (const f of $1) t.items.add(f); return t.files; })()")>]
    let private fileList (realm: obj) (files: File array) : FileList = jsNative

    [<Emit("$0 instanceof $1.Blob")>]
    let private isBlob (value: obj) (realm: obj) : bool = jsNative

    [<Emit("$0 instanceof $1.File")>]
    let private isFile (value: obj) (realm: obj) : bool = jsNative

    [<Emit("new $0.FileReader()")>]
    let private reader (realm: obj) : obj = jsNative

    [<Emit("$0.onload = () => $1(new Uint8Array($0.result)); $0.onerror = () => $2(String($0.error?.name ?? 'Error')); $0.readAsArrayBuffer($3)")>]
    let private readArrayBuffer (reader: obj) (ok: byte array -> unit) (error: string -> unit) (blob: Blob) : unit = jsNative

    [<Emit("$0.onload = null; $0.onerror = null; if ($0.readyState === 1) $0.abort()")>]
    let private abort (reader: obj) : unit = jsNative

    [<Emit("queueMicrotask($0)")>]
    let private post (action: unit -> unit) : unit = jsNative

    [<Emit("$0.toWellFormed()")>]
    let private wellFormed (text: string) : string = jsNative

    let private endings value =
        match value with
        | BlobLineEndings.Transparent -> "transparent"
        | BlobLineEndings.Native -> "native"

    /// <summary>A binary value service for <c>document</c>'s realm.</summary>
    let forDocument (document: Document) : BinaryValueService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Blob requires an active document Window")

        let window = realm document
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof BinaryValueService))

            if not (current document) then
                raise (InvalidOperationException "The Blob document is no longer active")

        let parts (values: BlobPart array) =
            BinaryValueContract.parts values
            |> Array.map (fun part ->
                match part with
                | BlobPart.Bytes bytes -> box bytes
                | BlobPart.Blob value ->
                    if not (isBlob value window) then
                        invalidArg "parts" "The Blob belongs to another provider"

                    box value
                | BlobPart.Text text -> box (wellFormed text))

        { new BinaryValueService with
            member _.CreateBlob(values, options) =
                check ()
                let options = BinaryValueContract.blobOptions options
                let raw = createEmpty<obj>
                raw?``type`` <- options.Type
                raw?endings <- endings options.Endings
                blob window (parts values) raw

            member _.CreateFile(values, name, options) =
                check ()
                let options = BinaryValueContract.fileOptions options
                let raw = createEmpty<obj>
                raw?``type`` <- options.Type
                raw?endings <- endings options.Endings

                match options.LastModified with
                | Some time -> raw?lastModified <- time
                | None -> ()

                file window (parts values) (wellFormed (BinaryValueContract.name name)) raw

            member _.CreateFileList files =
                check ()

                for value in BinaryValueContract.files files do
                    if not (isFile value window) then
                        invalidArg "files" "The File belongs to another provider"

                fileList window files

            member _.ReadBytes(value, terminal) =
                check ()
                let value = BinaryValueContract.blob value

                if not (isBlob value window) then
                    invalidArg "blob" "The Blob belongs to another provider"

                let fileReader = reader window

                let arbiter =
                    new ResourceArbiter<byte array> (post, terminal, (fun () -> abort fileReader), ignore)

                readArrayBuffer
                    fileReader
                    (fun bytes ->
                        arbiter.Settle (ResourceOutcome.Succeeded bytes)
                        |> ignore)
                    (fun name ->
                        arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed name))
                        |> ignore)
                    value

                arbiter :> ResourceOperation<byte array>

            member _.Dispose() =
                disposed.Value <- true
        }
