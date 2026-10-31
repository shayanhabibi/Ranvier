namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Canvas export and createImageBitmap of the active document realm.</summary>
module BrowserCanvasExport =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.toDataURL($1, $2)")>]
    let private dataUrl (canvas: HTMLCanvasElement) (mime: string) (quality: float) : string = jsNative

    [<Emit("Uint8Array.from(atob($0), c => c.charCodeAt(0))")>]
    let private decodeBase64 (text: string) : byte array = jsNative

    [<Emit("new ($0.defaultView.Blob)([$1], { type: $2 })")>]
    let private blob (document: Document) (bytes: byte array) (mime: string) : Blob = jsNative

    [<Emit("$0.getContext('bitmaprenderer')")>]
    let private bitmapContext (canvas: HTMLCanvasElement) : ImageBitmapRenderingContext = jsNative

    [<Emit("$0.defaultView.createImageBitmap($1, $2)")>]
    let private create (document: Document) (source: obj) (options: obj) : JS.Promise<ImageBitmap> = jsNative

    [<Emit("$0.defaultView.createImageBitmap($1, $2, $3, $4, $5, $6)")>]
    let private createCropped
        (document: Document)
        (source: obj)
        (x: int)
        (y: int)
        (width: int)
        (height: int)
        (options: obj)
        : JS.Promise<ImageBitmap> =
        jsNative

    [<Emit("$0.then(value => $1(value), error => $2(error))")>]
    let private settle (promise: JS.Promise<ImageBitmap>) (succeeded: ImageBitmap -> unit) (failed: obj -> unit) : unit = jsNative

    [<Emit("($0?.name ?? String($0))")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("queueMicrotask($0)")>]
    let private post (action: unit -> unit) : unit = jsNative

    [<Emit("$0.close()")>]
    let private close (bitmap: ImageBitmap) : unit = jsNative

    [<Emit("$0.ownerDocument === $1")>]
    let private ownedBy (node: obj) (document: Document) : bool = jsNative

    let private orientation =
        function
        | ImageBitmapOrientation.FlipY -> "flipY"
        | ImageBitmapOrientation.None -> "none"
        | _ -> "from-image"

    let private premultiply =
        function
        | ImageBitmapPremultiply.None -> "none"
        | ImageBitmapPremultiply.Premultiply -> "premultiply"
        | _ -> "default"

    let private resizeQuality =
        function
        | ImageBitmapResizeQuality.Pixelated -> "pixelated"
        | ImageBitmapResizeQuality.Medium -> "medium"
        | ImageBitmapResizeQuality.High -> "high"
        | _ -> "low"

    let private jsOptions (options: ImageBitmapOptions) : obj =
        let value =
            createObj
                [
                    "imageOrientation"
                    ==> orientation options.Orientation
                    "premultiplyAlpha"
                    ==> premultiply options.PremultiplyAlpha
                    "colorSpaceConversion"
                    ==> (if options.ColorSpaceConversion = ImageBitmapColorSpaceConversion.None then
                             "none"
                         else
                             "default")
                    "resizeQuality"
                    ==> resizeQuality options.ResizeQuality
                ]

        options.ResizeWidth
        |> Option.iter (fun width -> value?resizeWidth <- width)

        options.ResizeHeight
        |> Option.iter (fun height -> value?resizeHeight <- height)

        value

    /// <summary>A canvas export service for <c>document</c>.</summary>
    /// <remarks>Raises NotSupportedException without an active document Window.</remarks>
    let forDocument (document: Document) : CanvasExportService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Canvas export requires an active document Window")

        let disposed = ref false

        let check (canvas: HTMLCanvasElement) =
            if disposed.Value then
                raise (ObjectDisposedException (nameof CanvasExportService))

            if isNull (box canvas) then
                nullArg "canvas"

        // The encoded bytes, or None for a zero-size canvas; raises NotSupportedException when the browser substitutes PNG.
        let encode (canvas: HTMLCanvasElement) encoding quality =
            check canvas
            let mime = CanvasExportContract.mimeType encoding
            let url = dataUrl canvas mime (CanvasExportContract.quality quality)

            if url = "data:," then
                None
            else
                let prefix = "data:" + mime + ";base64,"

                if not (url.StartsWith prefix) then
                    raise (NotSupportedException ("This browser does not encode " + mime))

                Some (url, decodeBase64 (url.Substring prefix.Length))

        { new CanvasExportService with
            member _.Encode(canvas, encoding, quality) =
                match encode canvas encoding quality with
                | Some (_, bytes) -> bytes
                | None -> [||]

            member _.ToDataUrl(canvas, encoding, quality) =
                match encode canvas encoding quality with
                | Some (url, _) -> url
                | None -> "data:,"

            member _.ToBlob(canvas, encoding, quality) =
                encode canvas encoding quality
                |> Option.map (fun (_, bytes) -> blob document bytes (CanvasExportContract.mimeType encoding))

            member _.BitmapRendererContext canvas =
                check canvas
                let context = bitmapContext canvas
                if isNull (box context) then None else Some context

            member _.CreateImageBitmap(source, options, terminal) =
                if disposed.Value then
                    raise (ObjectDisposedException (nameof CanvasExportService))

                CanvasExportContract.checkOptions options

                let target: obj =
                    match source with
                    | ImageBitmapSource.Image image -> box image
                    | ImageBitmapSource.SvgImage image -> box image
                    | ImageBitmapSource.Canvas canvas -> box canvas
                    | ImageBitmapSource.Bitmap bitmap -> box bitmap
                    | ImageBitmapSource.ImageData data -> box data
                    | ImageBitmapSource.Blob value -> box value

                if isNull target then
                    nullArg "source"

                let arbiter = new ResourceArbiter<ImageBitmap> (post, terminal, ignore, ignore)

                let promise =
                    match options.Crop with
                    | Some crop -> createCropped document target crop.X crop.Y crop.Width crop.Height (jsOptions options)
                    | None -> create document target (jsOptions options)

                settle
                    promise
                    (fun bitmap ->
                        if not (arbiter.Settle (ResourceOutcome.Succeeded bitmap)) then
                            close bitmap)
                    (fun error ->
                        arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.DecodeFailed (errorName error)))
                        |> ignore)

                arbiter :> ResourceOperation<ImageBitmap>

          interface IDisposable with
              member _.Dispose() =
                  disposed.Value <- true
        }
