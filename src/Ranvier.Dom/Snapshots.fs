namespace Ranvier.Dom

open System

/// <summary>An image encoding, shared by snapshots and canvas export.</summary>
/// <remarks>
/// <c>Rgba8</c> is unpremultiplied RGBA8 rows, <c>Width * 4</c> bytes each, in the image's color space. PNG ignores
/// quality. JPEG and WEBP take a quality from 0 to 1; <c>None</c>, NaN or a value outside [0, 1] selects the encoder's
/// default, as <c>toBlob</c> does, and WEBP at exactly 1 is lossless.
/// </remarks>
[<RequireQualifiedAccess>]
type ImageEncoding =
    | Rgba8
    | Png
    | Jpeg
    | Webp

/// <summary>The color space of a snapshot's pixels.</summary>
[<RequireQualifiedAccess>]
type SnapshotColorSpace =
    | Srgb
    | DisplayP3

/// <summary>What a snapshot paints.</summary>
[<RequireQualifiedAccess>]
type SnapshotSubject =
    /// <summary>The viewport, as a frame paints it.</summary>
    | Viewport
    /// <summary>The element's subtree alone, over a transparent background.</summary>
    | Element of element: Element

/// <summary>An area in CSS pixels from the subject's top left; width and height are positive.</summary>
type SnapshotCrop =
    {
        X: float
        Y: float
        Width: float
        Height: float
    }

/// <summary>How a snapshot is painted and encoded.</summary>
/// <remarks>
/// <c>Scale</c> is device pixels per CSS pixel from 0.25 to 8, or <c>None</c> for the host's device scale. A crop is
/// expanded to whole CSS pixels and intersected with the subject.
/// </remarks>
type SnapshotOptions =
    {
        Scale: float option
        Crop: SnapshotCrop option
        ColorSpace: SnapshotColorSpace
        Encoding: ImageEncoding
        Quality: float option
    }

/// <summary>A captured image; the operation owns its data until it is disposed.</summary>
type SnapshotImage =
    {
        PixelWidth: int
        PixelHeight: int
        Format: ImageEncoding
        PixelColorSpace: SnapshotColorSpace
        Data: byte array
    }

/// <summary>Snapshots of a host's viewport or elements (Ranvier.WebCore #98).</summary>
/// <remarks>
/// The subject is painted during <c>Capture</c>; the terminal callback runs on the service's owner context after the
/// call returns. An ordinary browser context has no DOM capture: its service reports <c>Available = false</c> and
/// <c>Capture</c> raises NotSupportedException, never painting another subject in its place.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type SnapshotService =
    inherit IDisposable
    /// <summary>Whether this provider can capture its document at all.</summary>
    abstract Available: bool
    /// <summary>Whether this provider supports the color space.</summary>
    abstract SupportsColorSpace: colorSpace: SnapshotColorSpace -> bool
    /// <summary>Whether this provider supports the encoding.</summary>
    abstract SupportsEncoding: encoding: ImageEncoding -> bool

    /// <summary>Paints and encodes the subject.</summary>
    /// <exception cref="T:System.NotSupportedException">Capture, the color space or the encoding is unavailable.</exception>
    /// <exception cref="T:System.ArgumentOutOfRangeException">The scale, crop or quality is out of range.</exception>
    abstract Capture:
        subject: SnapshotSubject * options: SnapshotOptions * terminal: (ResourceOutcome<SnapshotImage> -> unit) -> ResourceOperation<SnapshotImage>

/// <summary>Defaults and validation shared by snapshot providers.</summary>
module SnapshotContract =
    /// <summary>An sRGB RGBA8 snapshot at the host's device scale.</summary>
    let defaults =
        {
            Scale = None
            Crop = None
            ColorSpace = SnapshotColorSpace.Srgb
            Encoding = ImageEncoding.Rgba8
            Quality = None
        }

    let private finite (value: float) =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Raises ArgumentOutOfRangeException for a scale or crop out of range, and ArgumentNullException for a null element.</summary>
    let options (subject: SnapshotSubject) (value: SnapshotOptions) =
        match subject with
        | SnapshotSubject.Element element when isNull (box element) -> nullArg "subject"
        | _ -> ()

        match value.Scale with
        | Some scale when not (scale >= 0.25 && scale <= 8.0) -> raise (ArgumentOutOfRangeException ("options", "Scale must be from 0.25 to 8"))
        | _ -> ()

        match value.Crop with
        | Some crop when
            not (
                finite crop.X
                && finite crop.Y
                && finite crop.Width
                && finite crop.Height
                && crop.Width > 0.0
                && crop.Height > 0.0
            )
            ->
            raise (ArgumentOutOfRangeException ("options", "A crop needs finite coordinates and a positive size"))
        | _ -> ()

        value
