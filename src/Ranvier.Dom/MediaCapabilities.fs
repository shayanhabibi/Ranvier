namespace Ranvier.Dom

open System

/// <summary><c>MediaDecodingType</c>.</summary>
[<RequireQualifiedAccess>]
type MediaDecodingType =
    | File
    | MediaSource
    | WebRtc

/// <summary><c>MediaEncodingType</c>.</summary>
[<RequireQualifiedAccess>]
type MediaEncodingType =
    | Record
    | WebRtc

/// <summary><c>HdrMetadataType</c>.</summary>
[<RequireQualifiedAccess>]
type MediaHdrMetadataType =
    | SmpteSt2086
    | SmpteSt2094_10
    | SmpteSt2094_40

/// <summary><c>ColorGamut</c>.</summary>
[<RequireQualifiedAccess>]
type MediaColorGamut =
    | Srgb
    | P3
    | Rec2020

/// <summary><c>TransferFunction</c>.</summary>
[<RequireQualifiedAccess>]
type MediaTransferFunction =
    | Srgb
    | Pq
    | Hlg

/// <summary><c>VideoConfiguration</c>; <c>None</c> leaves an optional member absent.</summary>
type MediaVideoConfiguration =
    {
        ContentType: string
        Width: uint32
        Height: uint32
        Bitrate: uint64
        Framerate: float
        AlphaChannel: bool option
        HdrMetadataType: MediaHdrMetadataType option
        ColorGamut: MediaColorGamut option
        TransferFunction: MediaTransferFunction option
    }

/// <summary><c>AudioConfiguration</c>; <c>None</c> leaves an optional member absent.</summary>
type MediaAudioConfiguration =
    {
        ContentType: string
        Channels: string option
        Bitrate: uint64 option
        Samplerate: uint32 option
        SpatialRendering: bool option
    }

/// <summary><c>MediaCapabilitiesInfo</c>.</summary>
type MediaCapabilitiesInfo =
    {
        Supported: bool
        Smooth: bool
        PowerEfficient: bool
    }

/// <summary>
/// <c>navigator.mediaCapabilities.decodingInfo()</c> and <c>encodingInfo()</c> for one document (Ranvier.WebCore #116).
/// </summary>
/// <remarks>
/// A configuration the engine rejects settles with <c>ResourceFailure.LoadFailed "TypeError"</c>; each other
/// configuration succeeds with the engine's answer. Terminal callbacks run on the service's owner context, after the
/// starting call returns. A query enables no playback, codec or capture: an engine without a media engine
/// configuration provider answers every valid configuration as unsupported, not smooth and not power efficient, and
/// reports the provider through the <c>media-decoding-provider</c> and <c>media-encoding-provider</c> capabilities.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type MediaCapabilitiesService =
    inherit IDisposable

    /// <summary><c>decodingInfo({ type, video, audio })</c>.</summary>
    abstract DecodingInfo:
        kind: MediaDecodingType *
        video: MediaVideoConfiguration option *
        audio: MediaAudioConfiguration option *
        terminal: (ResourceOutcome<MediaCapabilitiesInfo> -> unit) ->
            ResourceOperation<MediaCapabilitiesInfo>

    /// <summary><c>encodingInfo({ type, video, audio })</c>.</summary>
    abstract EncodingInfo:
        kind: MediaEncodingType *
        video: MediaVideoConfiguration option *
        audio: MediaAudioConfiguration option *
        terminal: (ResourceOutcome<MediaCapabilitiesInfo> -> unit) ->
            ResourceOperation<MediaCapabilitiesInfo>

/// <summary>Values shared by media capability providers.</summary>
module MediaCapabilitiesContract =
    /// <summary>The failure of a configuration the engine rejects.</summary>
    let rejected = ResourceFailure.LoadFailed "TypeError"

    /// <summary>Raises ArgumentNullException for a null content type in either configuration.</summary>
    let configuration (video: MediaVideoConfiguration option) (audio: MediaAudioConfiguration option) =
        match video with
        | Some value when isNull value.ContentType -> nullArg "video"
        | _ -> ()

        match audio with
        | Some value when isNull value.ContentType -> nullArg "audio"
        | Some { Channels = Some channels } when isNull channels -> nullArg "audio"
        | _ -> ()
