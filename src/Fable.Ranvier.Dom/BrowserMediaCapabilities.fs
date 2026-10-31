namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Media capability queries backed by the active document realm's <c>navigator.mediaCapabilities</c>.</summary>
[<RequireQualifiedAccess>]
module BrowserMediaCapabilities =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.navigator.mediaCapabilities?.decodingInfo === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.navigator.mediaCapabilities")>]
    let private capabilities (document: Document) : obj = jsNative

    [<Emit("$0[$1]($2).then(i => $3(i.supported, i.smooth, i.powerEfficient), e => $4(String(e?.name ?? 'Error')))")>]
    let private query (capabilities: obj) (name: string) (configuration: obj) (ok: bool -> bool -> bool -> unit) (error: string -> unit) : unit =
        jsNative

    [<Emit("queueMicrotask($0)")>]
    let private post (action: unit -> unit) : unit = jsNative

    let private hdr value =
        match value with
        | MediaHdrMetadataType.SmpteSt2086 -> "smpteSt2086"
        | MediaHdrMetadataType.SmpteSt2094_10 -> "smpteSt2094-10"
        | MediaHdrMetadataType.SmpteSt2094_40 -> "smpteSt2094-40"

    let private gamut value =
        match value with
        | MediaColorGamut.Srgb -> "srgb"
        | MediaColorGamut.P3 -> "p3"
        | MediaColorGamut.Rec2020 -> "rec2020"

    let private transfer value =
        match value with
        | MediaTransferFunction.Srgb -> "srgb"
        | MediaTransferFunction.Pq -> "pq"
        | MediaTransferFunction.Hlg -> "hlg"

    let private video (value: MediaVideoConfiguration) =
        let result =
            createObj
                [
                    "contentType" ==> value.ContentType
                    "width" ==> value.Width
                    "height" ==> value.Height
                    "bitrate" ==> float value.Bitrate
                    "framerate" ==> value.Framerate
                ]

        value.AlphaChannel
        |> Option.iter (fun flag -> result?alphaChannel <- flag)

        value.HdrMetadataType
        |> Option.iter (fun kind -> result?hdrMetadataType <- hdr kind)

        value.ColorGamut
        |> Option.iter (fun kind -> result?colorGamut <- gamut kind)

        value.TransferFunction
        |> Option.iter (fun kind -> result?transferFunction <- transfer kind)

        result

    let private audio (value: MediaAudioConfiguration) =
        let result = createObj [ "contentType" ==> value.ContentType ]

        value.Channels
        |> Option.iter (fun channels -> result?channels <- channels)

        value.Bitrate
        |> Option.iter (fun bitrate -> result?bitrate <- float bitrate)

        value.Samplerate
        |> Option.iter (fun samplerate -> result?samplerate <- samplerate)

        value.SpatialRendering
        |> Option.iter (fun flag -> result?spatialRendering <- flag)

        result

    /// <summary>A media capability service for <c>document</c>'s realm.</summary>
    let forDocument (document: Document) : MediaCapabilitiesService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "navigator.mediaCapabilities requires an active document Window")

        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof MediaCapabilitiesService))

            if not (current document) then
                raise (InvalidOperationException "The media capabilities document is no longer active")

        let start name kind (videoValue: MediaVideoConfiguration option) (audioValue: MediaAudioConfiguration option) terminal =
            check ()
            MediaCapabilitiesContract.configuration videoValue audioValue
            let configuration = createObj [ "type" ==> kind ]

            videoValue
            |> Option.iter (fun value -> configuration?video <- video value)

            audioValue
            |> Option.iter (fun value -> configuration?audio <- audio value)

            let arbiter =
                new ResourceArbiter<MediaCapabilitiesInfo> (post, terminal, ignore, ignore)

            query
                (capabilities document)
                name
                configuration
                (fun supported smooth powerEfficient ->
                    arbiter.Settle (
                        ResourceOutcome.Succeeded
                            {
                                Supported = supported
                                Smooth = smooth
                                PowerEfficient = powerEfficient
                            }
                    )
                    |> ignore)
                (fun error ->
                    let failure =
                        if error = "TypeError" then
                            MediaCapabilitiesContract.rejected
                        else
                            ResourceFailure.LoadFailed error

                    arbiter.Settle (ResourceOutcome.Failed failure)
                    |> ignore)

            arbiter :> ResourceOperation<MediaCapabilitiesInfo>

        { new MediaCapabilitiesService with
            member _.DecodingInfo(kind, videoValue, audioValue, terminal) =
                let name =
                    match kind with
                    | MediaDecodingType.File -> "file"
                    | MediaDecodingType.MediaSource -> "media-source"
                    | MediaDecodingType.WebRtc -> "webrtc"

                start "decodingInfo" name videoValue audioValue terminal

            member _.EncodingInfo(kind, videoValue, audioValue, terminal) =
                let name =
                    match kind with
                    | MediaEncodingType.Record -> "record"
                    | MediaEncodingType.WebRtc -> "webrtc"

                start "encodingInfo" name videoValue audioValue terminal

            member _.Dispose() =
                disposed.Value <- true
        }
