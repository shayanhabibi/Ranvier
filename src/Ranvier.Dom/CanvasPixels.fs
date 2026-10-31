namespace Ranvier.Dom

open System

/// <summary>An ImageDataPixelFormat, numbered as the native <c>RV_DOM_IMAGE_DATA_PIXEL_FORMAT_*</c> values.</summary>
type ImageDataPixelFormat =
    | RgbaUnorm8 = 0
    | RgbaFloat16 = 1

/// <summary>The layout of an ImageData's pixels.</summary>
/// <remarks>
/// Rows run top to bottom with no padding, so <c>RowBytes</c> is <c>Width * BytesPerPixel</c>. Each pixel holds R, G, B
/// and A in that order, and the colour channels are not premultiplied by alpha.
/// </remarks>
type ImageDataLayout =
    {
        Width: int
        Height: int
        ColorSpace: CanvasColorSpace
        PixelFormat: ImageDataPixelFormat
        BytesPerPixel: int
        RowBytes: int
        ByteLength: int
    }

/// <summary>ImageData construction and pixel bytes for one document.</summary>
/// <remarks>
/// Every provider creates sRGB ImageData in the rgba-unorm8 format. Constructor and range errors raise the provider's DOM
/// exception: IndexSizeError for a zero dimension or a length that does not match the dimensions, InvalidStateError for
/// a length that is not a non-zero multiple of 4, and RangeError for a byte range past the end of the data, which then
/// reads or writes nothing. The generated CanvasRenderingContext2D members createImageData, getImageData, putImageData
/// and drawImage move the pixels into and out of a canvas.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ImageDataService =
    inherit IDisposable
    /// <summary><c>new ImageData(width, height)</c>: transparent black pixels.</summary>
    abstract Create: width: int * height: int -> ImageData
    /// <summary><c>new ImageData(data, width, height)</c> over a copy of <c>pixels</c>, in the layout of ImageDataLayout.</summary>
    abstract CreateFrom: pixels: byte array * width: int * height: int -> ImageData
    /// <summary>The layout of the pixels of <c>data</c>.</summary>
    abstract Layout: data: ImageData -> ImageDataLayout
    /// <summary>A copy of <c>count</c> pixel bytes of <c>data</c> from <c>offset</c>.</summary>
    abstract Read: data: ImageData * offset: int * count: int -> byte array
    /// <summary>Copies <c>bytes</c> into the pixels of <c>data</c> at <c>offset</c>, as <c>data.data.set</c> does.</summary>
    abstract Write: data: ImageData * offset: int * bytes: byte array -> unit

/// <summary>Argument checks shared by ImageData providers.</summary>
module ImageDataContract =
    /// <summary>Raises ArgumentOutOfRangeException for a negative dimension.</summary>
    let checkDimensions (width: int) (height: int) =
        if width < 0 then
            raise (ArgumentOutOfRangeException (nameof width, "ImageData dimensions are unsigned"))

        if height < 0 then
            raise (ArgumentOutOfRangeException (nameof height, "ImageData dimensions are unsigned"))

    /// <summary>Raises ArgumentOutOfRangeException for a negative byte count.</summary>
    let checkCount (count: int) =
        if count < 0 then
            raise (ArgumentOutOfRangeException (nameof count, "A byte count is zero or more"))
