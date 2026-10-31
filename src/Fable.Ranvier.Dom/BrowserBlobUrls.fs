namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Object URLs, blob reads and FileReader backed by the active document realm.</summary>
[<RequireQualifiedAccess>]
module BrowserBlobUrls =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.FileReader === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("$0 instanceof $1.Blob")>]
    let private isBlob (value: obj) (realm: obj) : bool = jsNative

    [<Emit("$0 instanceof $1.FileReader")>]
    let private isReader (value: obj) (realm: obj) : bool = jsNative

    [<Emit("$0.URL.createObjectURL($1)")>]
    let private createUrl (realm: obj) (blob: Blob) : string = jsNative

    [<Emit("$0.URL.revokeObjectURL($1)")>]
    let private revokeUrl (realm: obj) (url: string) : unit = jsNative

    [<Emit("new $0.FileReader()")>]
    let private newReader (realm: obj) : FileReader = jsNative

    [<Emit("$0.fetch($1, {signal: $2.signal}).then(r => r.arrayBuffer().then(b => $3(r.headers.get('content-type') ?? '', new Uint8Array(b))), e => $4(String(e?.name ?? 'Error')))")>]
    let private fetchUrl (realm: obj) (url: string) (controller: obj) (ok: string -> byte array -> unit) (error: string -> unit) : unit = jsNative

    [<Emit("new AbortController()")>]
    let private controller () : obj = jsNative

    [<Emit("$0.abort()")>]
    let private abortController (controller: obj) : unit = jsNative

    [<Emit("$0.text().then($1, e => $2(String(e?.name ?? 'Error')))")>]
    let private text (blob: Blob) (ok: string -> unit) (error: string -> unit) : unit = jsNative

    [<Emit("$0.bytes().then($1, e => $2(String(e?.name ?? 'Error')))")>]
    let private bytes (blob: Blob) (ok: byte array -> unit) (error: string -> unit) : unit = jsNative

    [<Emit("$0.result")>]
    let private result (reader: FileReader) : obj = jsNative

    [<Emit("$0 instanceof ArrayBuffer")>]
    let private isArrayBuffer (value: obj) : bool = jsNative

    [<Emit("new Uint8Array($0.slice(0))")>]
    let private copy (value: obj) : byte array = jsNative

    [<Emit("$0.error?.name ?? null")>]
    let private errorName (reader: FileReader) : string = jsNative

    [<Emit("queueMicrotask($0)")>]
    let private post (action: unit -> unit) : unit = jsNative

    /// <summary>A blob URL service for <c>document</c>'s realm.</summary>
    let forDocument (document: Document) : BlobUrlService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "FileReader requires an active document Window")

        let window = realm document
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof BlobUrlService))

            if not (current document) then
                raise (InvalidOperationException "The FileReader document is no longer active")

        let own (value: Blob) =
            let value = BlobUrlContract.blob value

            if not (isBlob value window) then
                invalidArg "blob" "The Blob belongs to another provider"

            value

        let ownReader (value: FileReader) =
            let value = BlobUrlContract.reader value

            if not (isReader value window) then
                invalidArg "reader" "The FileReader belongs to another provider"

            value

        let promise (start: ('T -> unit) -> (string -> unit) -> unit) terminal =
            let arbiter = new ResourceArbiter<'T> (post, terminal, ignore, ignore)

            start
                (fun value ->
                    arbiter.Settle (ResourceOutcome.Succeeded value)
                    |> ignore)
                (fun name ->
                    arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed name))
                    |> ignore)

            arbiter :> ResourceOperation<'T>

        { new BlobUrlService with
            member _.CreateObjectUrl blob =
                check ()
                createUrl window (own blob)

            member _.RevokeObjectUrl url =
                check ()
                revokeUrl window (BlobUrlContract.url url)

            member _.ReadUrl(url, terminal) =
                check ()
                let url = BlobUrlContract.blobUrl url
                let abort = controller ()

                let arbiter =
                    new ResourceArbiter<ResourceResponse> (post, terminal, (fun () -> abortController abort), ignore)

                fetchUrl
                    window
                    url
                    abort
                    (fun contentType body ->
                        arbiter.Settle (
                            ResourceOutcome.Succeeded
                                {
                                    ContentType = contentType
                                    Bytes = body
                                }
                        )
                        |> ignore)
                    (fun name ->
                        arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed name))
                        |> ignore)

                arbiter :> ResourceOperation<ResourceResponse>

            member _.ReadText(blob, terminal) =
                check ()
                let blob = own blob
                promise (text blob) terminal

            member _.ReadBytes(blob, terminal) =
                check ()
                let blob = own blob
                promise (bytes blob) terminal

            member _.CreateFileReader() =
                check ()
                newReader window

            member _.Result reader =
                let value = result (ownReader reader)

                if isNull value then
                    FileReaderResult.Empty
                elif isArrayBuffer value then
                    FileReaderResult.Bytes (copy value)
                else
                    FileReaderResult.Text (unbox<string> value)

            member _.Error reader =
                match errorName (ownReader reader) with
                | null -> None
                | name -> Some name

            member _.Dispose() =
                disposed.Value <- true
        }
