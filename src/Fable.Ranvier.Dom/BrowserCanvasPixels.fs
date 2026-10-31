namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>ImageData construction and pixel bytes of the active document realm.</summary>
module BrowserCanvasPixels =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("new ($0.defaultView.ImageData)($1, $2)")>]
    let private create (document: Document) (width: int) (height: int) : ImageData = jsNative

    [<Emit("new ($0.defaultView.ImageData)(new ($0.defaultView.Uint8ClampedArray)($1), $2, $3)")>]
    let private createFrom (document: Document) (pixels: byte array) (width: int) (height: int) : ImageData = jsNative

    [<Emit("[$0.width, $0.height, String($0.colorSpace ?? 'srgb'), String($0.pixelFormat ?? 'rgba-unorm8'), $0.data.BYTES_PER_ELEMENT, $0.data.byteLength]")>]
    let private shape (data: ImageData) : obj array = jsNative

    [<Emit("new Uint8Array($0.data.buffer, $0.data.byteOffset + $1, $2).slice()")>]
    let private slice (data: ImageData) (offset: int) (count: int) : byte array = jsNative

    [<Emit("new Uint8Array($0.data.buffer, $0.data.byteOffset, $0.data.byteLength).set($1, $2)")>]
    let private assign (data: ImageData) (bytes: byte array) (offset: int) : unit = jsNative

    [<Emit("$0.data.byteLength")>]
    let private byteLength (data: ImageData) : int = jsNative

    [<Emit("new ($0.defaultView.RangeError)($1)")>]
    let private rangeError (document: Document) (message: string) : exn = jsNative

    /// <summary>An ImageData service for <c>document</c>.</summary>
    /// <remarks>Raises NotSupportedException without an active document Window.</remarks>
    let forDocument (document: Document) : ImageDataService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "ImageData requires an active document Window")

        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof ImageDataService))

        let present (data: ImageData) =
            check ()

            if isNull (box data) then
                nullArg "data"

        let range (data: ImageData) (offset: int) (count: int) =
            let length = byteLength data

            if
                offset < 0
                || count < 0
                || offset > length
                || count > length - offset
            then
                raise (rangeError document "The range lies outside the ImageData's data")

        { new ImageDataService with
            member _.Create(width, height) =
                check ()
                ImageDataContract.checkDimensions width height
                create document width height

            member _.CreateFrom(pixels, width, height) =
                check ()

                if isNull pixels then
                    nullArg "pixels"

                ImageDataContract.checkDimensions width height
                createFrom document pixels width height

            member _.Layout data =
                present data
                let values = shape data
                let width = unbox<int> values[0]
                let perPixel = 4 * unbox<int> values[4]

                {
                    Width = width
                    Height = unbox values[1]
                    ColorSpace =
                        (if unbox<string> values[2] = "display-p3" then
                             CanvasColorSpace.DisplayP3
                         else
                             CanvasColorSpace.Srgb)
                    PixelFormat =
                        (if unbox<string> values[3] = "rgba-float16" then
                             ImageDataPixelFormat.RgbaFloat16
                         else
                             ImageDataPixelFormat.RgbaUnorm8)
                    BytesPerPixel = perPixel
                    RowBytes = width * perPixel
                    ByteLength = unbox values[5]
                }

            member _.Read(data, offset, count) =
                present data
                ImageDataContract.checkCount count
                range data offset count
                slice data offset count

            member _.Write(data, offset, bytes) =
                present data

                if isNull bytes then
                    nullArg "bytes"

                range data offset bytes.Length
                assign data bytes offset

          interface IDisposable with
              member _.Dispose() =
                  disposed.Value <- true
        }
