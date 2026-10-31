namespace Ranvier.Dom

open System
open System.Threading
open System.Threading.Tasks
open Fable.Core

/// <summary>Passive capability queries from the active document's actual browser realm.</summary>
[<RequireQualifiedAccess>]
module BrowserCapabilities =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("typeof $0.defaultView.ImageDecoder?.isTypeSupported === 'function'")>]
    let private imageQueryAvailable (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.ImageDecoder.isTypeSupported($1)")>]
    let private imageSupported (document: Document) (mime: string) : JS.Promise<bool> = jsNative

    [<Emit("typeof $0.defaultView.CSS?.supports === 'function' && $0.defaultView.CSS.supports('font-format(woff)')")>]
    let private fontQueryAvailable (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.CSS.supports('font-format(' + $1 + ')')")>]
    let private fontSupported (document: Document) (format: string) : bool = jsNative

    [<Emit("(() => { const w=$0.defaultView, n=w.navigator; switch($1) { case 'webgl': return typeof w.WebGLRenderingContext !== 'undefined'; case 'webgpu': return 'gpu' in n; case 'webxr': return 'xr' in n; case 'video': return typeof w.HTMLVideoElement !== 'undefined'; case 'web-audio': return typeof w.AudioContext !== 'undefined'; case 'media-stream': return typeof w.MediaStream !== 'undefined'; case 'media-recorder': return typeof w.MediaRecorder !== 'undefined'; case 'web-codecs': return typeof w.VideoDecoder !== 'undefined'; case 'encrypted-media': return typeof n.requestMediaKeySystemAccess === 'function'; case 'network': return typeof w.fetch === 'function'; case 'crypto-digest': return typeof w.crypto?.subtle?.digest === 'function'; case 'crypto-other-algorithms': return typeof w.crypto?.subtle?.encrypt === 'function'; case 'indexeddb': return 'indexedDB' in w; case 'cache-storage': return 'caches' in w; case 'service-worker': return 'serviceWorker' in n; case 'shared-worker': return typeof w.SharedWorker !== 'undefined'; case 'websocket': return typeof w.WebSocket !== 'undefined'; case 'webtransport': return typeof w.WebTransport !== 'undefined'; case 'file-system': return typeof n.storage?.getDirectory === 'function'; case 'push': return typeof w.PushManager !== 'undefined'; case 'webassembly': return typeof w.WebAssembly !== 'undefined'; case 'clipboard': return 'clipboard' in n; case 'notifications': return typeof w.Notification !== 'undefined'; case 'speech-synthesis': return 'speechSynthesis' in w; case 'gamepad': return typeof n.getGamepads === 'function'; default: return null; } })()")>]
    let private exported (document: Document) (name: string) : bool option = jsNative

    [<Emit("(() => { const p=$0.permissionsPolicy ?? $0.featurePolicy; const names = { 'webgpu': 'webgpu', 'webxr': 'xr-spatial-tracking', 'clipboard': 'clipboard-read', 'encrypted-media': 'encrypted-media' }; const n=names[$1]; if (!n || typeof p?.features !== 'function' || typeof p?.allowsFeature !== 'function' || !p.features().includes(n)) return null; return p.allowsFeature(n); })()")>]
    let private permitted (document: Document) (name: string) : bool option = jsNative

    [<Emit("(() => { const w=$0.defaultView, f=() => $1(); w.addEventListener('pagehide',f); return { Dispose: () => w.removeEventListener('pagehide',f) }; })()")>]
    let private onPageHide (document: Document) (callback: unit -> unit) : IDisposable = jsNative

    let private images =
        [|
            "image/png"
            "image/jpeg"
            "image/gif"
            "image/bmp"
            "image/x-icon"
            "image/webp"
            "image/avif"
            "image/jxl"
            "image/svg+xml"
        |]

    let private fonts =
        [|
            "truetype"
            "opentype"
            "woff"
            "woff2"
            "collection"
            "truetype-variations"
            "opentype-variations"
            "woff-variations"
            "woff2-variations"
        |]

    let private facilities =
        [|
            "webgl"
            "webgpu"
            "webxr"
            "video"
            "web-audio"
            "media-stream"
            "media-recorder"
            "web-codecs"
            "encrypted-media"
            "network"
            "crypto-digest"
            "crypto-other-algorithms"
            "indexeddb"
            "cache-storage"
            "service-worker"
            "shared-worker"
            "websocket"
            "webtransport"
            "file-system"
            "push"
            "webassembly"
            "clipboard"
            "notifications"
            "speech-synthesis"
            "gamepad"
        |]

    /// <summary>Creates an extensible borrowed realm provider; pagehide or explicit disposal invalidates pending reports.</summary>
    /// <remarks>Format facts describe the actual ImageDecoder/CSS query; property presence establishes only Exported.
    /// Hardware readiness and unobservable native build/settings remain Unknown. Discovery prompts for no permission.</remarks>
    let forDocument (document: Document) : CapabilityService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (CapabilityContextException "Capability queries require an active document Window.")

        let service = Capabilities.create ()
        let mutable disposed = false

        let invalidate () =
            if not disposed then
                disposed <- true
                service.Dispose ()

        let active () =
            if disposed then
                raise (CapabilityContextException "CapabilityService is disposed.")

            if not (current document) then
                invalidate ()
                raise (CapabilityContextException "The capability document is no longer active.")

        let fact reason value =
            {
                State = (if value then CapabilityState.Yes else CapabilityState.No)
                Reason = reason
            }

        let observe key (token: CancellationToken) =
            task {
                active ()
                token.ThrowIfCancellationRequested ()

                let mutable observation =
                    Capabilities.unknown
                        key
                        "browser-realm"
                        "passive realm observation"
                        "current document realm"
                        "No passive observation for this fact."

                match key.Category with
                | CapabilityCategory.ImageFormat when imageQueryAvailable document ->
                    let! supported =
                        imageSupported document key.Name
                        |> Async.AwaitPromise
                        |> Async.StartAsTask

                    active ()
                    token.ThrowIfCancellationRequested ()

                    observation <-
                        { observation with
                            Scope = "ImageDecoder.isTypeSupported / current document realm; HTML image rendering is separate."
                            Exported = fact "Actual realm ImageDecoder.isTypeSupported function presence." true
                            Implemented =
                                fact "ImageDecoder format-support query; HTML image support and actual decode/render remain unverified." supported
                            Value = Some (CapabilityValue.Boolean supported)
                        }
                | CapabilityCategory.FontFormat when fontQueryAvailable document ->
                    let supported = fontSupported document key.Name

                    observation <-
                        { observation with
                            Exported = fact "CSS.supports accepts the font-format query grammar." true
                            Implemented = fact "Actual realm CSS font-format support query; actual font loading remains unverified." supported
                            Value = Some (CapabilityValue.Boolean supported)
                        }
                | CapabilityCategory.PlatformFacility ->
                    observation <-
                        { observation with
                            Exported =
                                (match exported document key.Name with
                                 | Some value -> fact "Actual realm API/property presence; behavior is unverified." value
                                 | None -> observation.Exported)
                            Permitted =
                                (match permitted document key.Name with
                                 | Some value -> fact "Current document Permission Policy allowsFeature result." value
                                 | None -> observation.Permitted)
                        }
                | _ -> ()

                return Some observation
            }

        let knownKeys =
            [|
                images
                |> Array.map (Capabilities.key CapabilityCategory.ImageFormat)
                fonts
                |> Array.map (Capabilities.key CapabilityCategory.FontFormat)
                facilities
                |> Array.map (Capabilities.key CapabilityCategory.PlatformFacility)
            |]
            |> Array.concat

        let builtin =
            service.Register
                {
                    Id = "browser-realm"
                    KnownKeys = knownKeys
                    Observe = observe
                }

        let pageHide = onPageHide document invalidate

        { new CapabilityService with
            member _.KnownKeys() =
                active ()
                service.KnownKeys ()

            member _.Register provider =
                active ()
                service.Register provider

            member _.Observe(key, token) =
                if isNull (box key) then
                    nullArg "key"

                let key = Capabilities.key key.Category key.Name

                if token.IsCancellationRequested then
                    CapabilityTasks.cancelled<CapabilityReport> token
                else
                    active ()

                    task {
                        let! report = service.Observe (key, token)
                        active ()
                        token.ThrowIfCancellationRequested ()
                        return report
                    }

            member _.Dispose() =
                invalidate ()
                builtin.Dispose ()
                pageHide.Dispose ()
        }
