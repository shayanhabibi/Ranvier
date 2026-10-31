namespace Ranvier.Dom

open System

/// <summary>A createImageBitmap source.</summary>
[<RequireQualifiedAccess>]
type ImageBitmapSource =
    | Image of image: HTMLImageElement
    | SvgImage of image: SVGImageElement
    | Canvas of canvas: HTMLCanvasElement
    | Bitmap of bitmap: ImageBitmap
    | ImageData of data: ImageData
    | Blob of blob: Blob

/// <summary>ImageOrientation, numbered as the native <c>RV_IMAGE_BITMAP_ORIENTATION_*</c> values.</summary>
type ImageBitmapOrientation =
    | FromImage = 0
    | FlipY = 1
    | None = 2

/// <summary>PremultiplyAlpha, numbered as the native <c>RV_IMAGE_BITMAP_PREMULTIPLY_*</c> values.</summary>
type ImageBitmapPremultiply =
    | None = 0
    | Premultiply = 1
    | Default = 2

/// <summary>ColorSpaceConversion, numbered as the native <c>RV_IMAGE_BITMAP_COLOR_SPACE_CONVERSION_*</c> values.</summary>
type ImageBitmapColorSpaceConversion =
    | None = 0
    | Default = 1

/// <summary>ResizeQuality, numbered as the native <c>RV_IMAGE_BITMAP_RESIZE_*</c> values.</summary>
type ImageBitmapResizeQuality =
    | Pixelated = 0
    | Low = 1
    | Medium = 2
    | High = 3

/// <summary>The source rectangle of <c>createImageBitmap(image, sx, sy, sw, sh)</c>; a negative size spans to the left or up.</summary>
type ImageBitmapCrop =
    {
        X: int
        Y: int
        Width: int
        Height: int
    }

/// <summary>ImageBitmapOptions, with the optional source rectangle.</summary>
type ImageBitmapOptions =
    {
        Crop: ImageBitmapCrop option
        Orientation: ImageBitmapOrientation
        PremultiplyAlpha: ImageBitmapPremultiply
        ColorSpaceConversion: ImageBitmapColorSpaceConversion
        ResizeWidth: int option
        ResizeHeight: int option
        ResizeQuality: ImageBitmapResizeQuality
    }

    /// <summary>The createImageBitmap defaults: the whole source, from-image, default, default, no resize and low.</summary>
    static member Default =
        {
            Crop = Option.None
            Orientation = ImageBitmapOrientation.FromImage
            PremultiplyAlpha = ImageBitmapPremultiply.Default
            ColorSpaceConversion = ImageBitmapColorSpaceConversion.Default
            ResizeWidth = Option.None
            ResizeHeight = Option.None
            ResizeQuality = ImageBitmapResizeQuality.Low
        }

/// <summary>Canvas export and ImageBitmap creation for one document.</summary>
/// <remarks>
/// Export encodes PNG, JPEG and WEBP where the provider's encoder has them; RGBA8 and an encoding the provider lacks
/// raise NotSupportedException, where a browser's own toDataURL falls back to PNG. A canvas whose origin is not clean
/// raises the provider's SecurityError. A zero-size canvas exports no bytes, the data URL <c>data:,</c> and no Blob.
/// <c>CreateImageBitmap</c> settles on the owner context after the call returns: <c>Succeeded</c> with a bitmap the
/// caller owns, or <c>DecodeFailed</c> with the DOMException name, such as <c>InvalidStateError</c> for a closed bitmap
/// or an undecodable Blob and <c>RangeError</c> for a crop with a zero width or height.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CanvasExportService =
    inherit IDisposable
    /// <summary>The encoded bytes of the canvas bitmap: the bytes toBlob's Blob holds.</summary>
    abstract Encode: canvas: HTMLCanvasElement * encoding: ImageEncoding * quality: float option -> byte array
    /// <summary>HTMLCanvasElement.toDataURL.</summary>
    abstract ToDataUrl: canvas: HTMLCanvasElement * encoding: ImageEncoding * quality: float option -> string
    /// <summary>HTMLCanvasElement.toBlob, during the call; None for a zero-size canvas.</summary>
    abstract ToBlob: canvas: HTMLCanvasElement * encoding: ImageEncoding * quality: float option -> Blob option
    /// <summary>getContext("bitmaprenderer"); None when the canvas already has a context of another type.</summary>
    abstract BitmapRendererContext: canvas: HTMLCanvasElement -> ImageBitmapRenderingContext option

    /// <summary>createImageBitmap(source, options), and with a crop createImageBitmap(source, sx, sy, sw, sh, options).</summary>
    abstract CreateImageBitmap:
        source: ImageBitmapSource * options: ImageBitmapOptions * terminal: (ResourceOutcome<ImageBitmap> -> unit) -> ResourceOperation<ImageBitmap>

/// <summary>Argument checks and names shared by canvas export providers.</summary>
module CanvasExportContract =
    /// <summary>The MIME type of an exportable encoding; raises NotSupportedException for RGBA8.</summary>
    let mimeType (encoding: ImageEncoding) =
        match encoding with
        | ImageEncoding.Png -> "image/png"
        | ImageEncoding.Jpeg -> "image/jpeg"
        | ImageEncoding.Webp -> "image/webp"
        | ImageEncoding.Rgba8 -> raise (NotSupportedException "Canvas export encodes PNG, JPEG or WEBP, not RGBA8")

    /// <summary>The quality a provider passes on: NaN for None, which selects the encoder's default.</summary>
    let quality (value: float option) =
        defaultArg value nan

    /// <summary>Raises ArgumentOutOfRangeException for a negative resize dimension.</summary>
    let checkOptions (options: ImageBitmapOptions) =
        match options.ResizeWidth with
        | Some width when width <= 0 -> raise (ArgumentOutOfRangeException ("ResizeWidth", "A resize width is positive"))
        | _ -> ()

        match options.ResizeHeight with
        | Some height when height <= 0 -> raise (ArgumentOutOfRangeException ("ResizeHeight", "A resize height is positive"))
        | _ -> ()
