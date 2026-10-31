namespace Ranvier.Dom

open System
open System.Collections.Generic
open Fable.Core

/// <summary>A browser resource service over a provider, Image.decode, FontFace and document.fonts.</summary>
/// <remarks>
/// The browser reports every image decode failure as one EncodingError. Failures are therefore provider-qualified:
/// a changed source is Invalidated, an image with no usable data is LoadFailed and any other rejection is DecodeFailed.
/// </remarks>
[<RequireQualifiedAccess>]
module BrowserResources =
    [<Emit("URL.createObjectURL(new Blob([$0], { type: $1 }))")>]
    let private objectUrl (bytes: byte array) (kind: string) : string = jsNative

    [<Emit("URL.revokeObjectURL($0)")>]
    let private revoke (url: string) : unit = jsNative

    [<Emit("(() => { const image = new Image(); image.src = $0; return image; })()")>]
    let private newImage (url: string) : obj = jsNative

    [<Emit("$0.decode()")>]
    let private decode (image: obj) : JS.Promise<obj> = jsNative

    [<Emit("$0.naturalWidth ?? 0")>]
    let private naturalWidth (image: obj) : int = jsNative

    [<Emit("$0.naturalHeight ?? 0")>]
    let private naturalHeight (image: obj) : int = jsNative

    [<Emit("$0.currentSrc || $0.getAttribute('src') || $0.getAttribute('href') || ''")>]
    let private currentSource (element: obj) : string = jsNative

    [<Emit("(typeof $0.complete === 'boolean' && $0.complete && ($0.naturalWidth ?? 1) === 0)")>]
    let private unusable (element: obj) : bool = jsNative

    [<Emit("typeof $0.decode === 'function'")>]
    let private decodable (element: obj) : bool = jsNative

    [<Emit("new FontFace($0, $1)")>]
    let private fontFace (family: string) (bytes: byte array) : obj = jsNative

    [<Emit("$0.load()")>]
    let private loadFace (face: obj) : JS.Promise<obj> = jsNative

    [<Emit("$0.fonts.add($1)")>]
    let private addFont (document: Document) (face: obj) : unit = jsNative

    [<Emit("$0.fonts.delete($1)")>]
    let private deleteFont (document: Document) (face: obj) : unit = jsNative

    [<Emit("queueMicrotask($0)")>]
    let private post (action: unit -> unit) : unit = jsNative

    [<Emit("$0.then(value => $1(value), error => $2(error))")>]
    let private settled (promise: JS.Promise<'T>) (ok: 'T -> unit) (error: obj -> unit) : unit = jsNative

    [<Emit("($0?.name ?? String($0))")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("(() => { const w = $0.defaultView, f = () => $1(); w.addEventListener('pagehide', f); return { Dispose: () => w.removeEventListener('pagehide', f) }; })()")>]
    let private onPageHide (document: Document) (callback: unit -> unit) : IDisposable = jsNative

    [<Emit("(() => { const controller = new AbortController(); fetch($0, { cache: $1, signal: controller.signal }).then(r => r.ok ? r.arrayBuffer().then(b => $2(true, r.headers.get('content-type') ?? '', new Uint8Array(b), '')) : $2(false, '', null, 'HTTP ' + r.status), e => $2(false, '', null, e?.name ?? String(e))); return { Dispose: () => controller.abort() }; })()")>]
    let private fetchBytes (url: string) (cache: string) (complete: bool -> string -> byte array -> string -> unit) : IDisposable = jsNative

    let private copy (response: ResourceResponse) =
        {
            ContentType = response.ContentType
            Bytes = Array.copy response.Bytes
        }

    let private validate (request: ResourceRequest) =
        if isNull (box request) then
            nullArg "request"

        let url = request.Url

        if
            isNull url
            || not (
                url.StartsWith "http://"
                || url.StartsWith "https://"
                || url.StartsWith "data:"
            )
        then
            invalidArg "request" "Expected an absolute http, https or data URL."

    /// <summary>A provider over fetch, with the cache mode matching each request's policy and abort on disposal.</summary>
    let fetchProvider: ResourceProvider =
        fun request complete ->
            let mode =
                match request.CachePolicy with
                | ResourceCachePolicy.UseCache -> "default"
                | ResourceCachePolicy.Reload -> "reload"
                | ResourceCachePolicy.NoStore -> "no-store"

            fetchBytes request.Url mode (fun ok kind bytes message ->
                if ok then
                    complete (Ok { ContentType = kind; Bytes = bytes })
                else
                    complete (Error message))

    /// <summary>Creates a service for the document; terminal callbacks run as microtasks of its realm.</summary>
    /// <remarks>Disposal, or the document's pagehide, cancels pending operations, revokes object URLs, removes
    /// registered fonts and clears the cache.</remarks>
    let forDocument (document: Document) (provider: ResourceProvider) : ResourceService =
        if isNull (box document) then
            nullArg "document"

        if isNull (box provider) then
            nullArg "provider"

        let cache = Dictionary<string, ResourceResponse>()
        let live = HashSet<IDisposable>(HashIdentity.Reference)
        let mutable disposed = false

        let ensure () =
            if disposed then
                raise (ObjectDisposedException "ResourceService")

        let loadBytes (request: ResourceRequest) (onResult: Result<ResourceResponse, string> -> unit) : IDisposable =
            match request.CachePolicy, cache.TryGetValue request.Url with
            | ResourceCachePolicy.UseCache, (true, cached) ->
                let response = copy cached
                post (fun () -> onResult (Ok response))

                { new IDisposable with
                    member _.Dispose() = ()
                }
            | _ ->
                let mutable first = true

                provider request (fun result ->
                    if first then
                        first <- false
                        let result = result |> Result.map copy

                        post (fun () ->
                            match result with
                            | Ok response when
                                request.CachePolicy <> ResourceCachePolicy.NoStore
                                && not disposed
                                ->
                                cache[request.Url] <- copy response
                            | _ -> ()

                            onResult result))

        let createArbiter terminal withdraw release =
            let mutable arbiter = Unchecked.defaultof<ResourceArbiter<_>>

            arbiter <-
                new ResourceArbiter<_> (
                    post,
                    (fun outcome ->
                        live.Remove arbiter |> ignore
                        terminal outcome),
                    withdraw,
                    release
                )

            live.Add arbiter |> ignore
            arbiter

        let disposeAll () =
            if not disposed then
                disposed <- true

                for operation in List.ofSeq live do
                    operation.Dispose ()

                live.Clear ()
                cache.Clear ()

        let pageHide = onPageHide document disposeAll

        { new ResourceService with
            member _.Load(request, terminal) =
                ensure ()
                validate request
                let mutable pending: IDisposable = null

                let arbiter =
                    createArbiter
                        terminal
                        (fun () ->
                            if not (isNull pending) then
                                pending.Dispose ())
                        ignore

                pending <-
                    loadBytes request (fun result ->
                        match result with
                        | Ok response ->
                            arbiter.Settle (ResourceOutcome.Succeeded response)
                            |> ignore
                        | Error message ->
                            arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed message))
                            |> ignore)

                arbiter :> ResourceOperation<_>

            member _.LoadImage(request, terminal) =
                ensure ()
                validate request
                let mutable pending: IDisposable = null
                let mutable url: string = null

                let clean () =
                    if not (isNull url) then
                        revoke url
                        url <- null

                let arbiter =
                    createArbiter
                        (fun outcome ->
                            match outcome with
                            | ResourceOutcome.Failed _ -> clean ()
                            | ResourceOutcome.Succeeded _ -> ()

                            terminal outcome)
                        (fun () ->
                            if not (isNull pending) then
                                pending.Dispose ())
                        (fun _ -> clean ())

                pending <-
                    loadBytes request (fun result ->
                        match result with
                        | Error message ->
                            arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed message))
                            |> ignore
                        | Ok _ when
                            arbiter.Snapshot <> ResourceState.Pending
                            || disposed
                            ->
                            ()
                        | Ok response ->
                            url <- objectUrl response.Bytes response.ContentType
                            let transportUrl = url
                            let image = newImage url

                            settled
                                (decode image)
                                (fun _ ->
                                    arbiter.Settle (
                                        ResourceOutcome.Succeeded
                                            {
                                                Width = naturalWidth image
                                                Height = naturalHeight image
                                                SourceUrl = request.Url
                                                TransportUrl = transportUrl
                                            }
                                    )
                                    |> ignore)
                                (fun error ->
                                    arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.DecodeFailed ("browser " + errorName error)))
                                    |> ignore))

                arbiter :> ResourceOperation<_>

            member _.DecodeImage(target, terminal) =
                ensure ()
                let element = box target

                if isNull element || not (decodable element) then
                    invalidArg "target" "Expected an HTMLImageElement or SVGImageElement."

                let source = currentSource element
                let arbiter = createArbiter terminal ignore ignore

                settled
                    (decode element)
                    (fun _ ->
                        arbiter.Settle (
                            ResourceOutcome.Succeeded
                                {
                                    Width = naturalWidth element
                                    Height = naturalHeight element
                                    SourceUrl = currentSource element
                                }
                        )
                        |> ignore)
                    (fun error ->
                        let name = errorName error

                        let failure =
                            if currentSource element <> source then
                                ResourceFailure.Invalidated ("browser " + name + ": source changed")
                            elif unusable element then
                                ResourceFailure.LoadFailed ("browser " + name + ": no usable image data")
                            else
                                ResourceFailure.DecodeFailed ("browser " + name)

                        arbiter.Settle (ResourceOutcome.Failed failure)
                        |> ignore)

                arbiter :> ResourceOperation<_>

            member _.LoadFont(family, request, terminal) =
                ensure ()
                validate request

                if String.IsNullOrEmpty family then
                    invalidArg "family" "Expected a nonempty family name."

                let mutable pending: IDisposable = null
                let mutable face: obj = null

                let clean () =
                    if not (isNull face) then
                        deleteFont document face
                        face <- null

                let arbiter =
                    createArbiter
                        (fun outcome ->
                            match outcome with
                            | ResourceOutcome.Failed _ -> clean ()
                            | ResourceOutcome.Succeeded _ -> ()

                            terminal outcome)
                        (fun () ->
                            if not (isNull pending) then
                                pending.Dispose ())
                        (fun _ -> clean ())

                pending <-
                    loadBytes request (fun result ->
                        match result with
                        | Error message ->
                            arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed message))
                            |> ignore
                        | Ok _ when
                            arbiter.Snapshot <> ResourceState.Pending
                            || disposed
                            ->
                            ()
                        | Ok response ->
                            let candidate = fontFace family response.Bytes

                            settled
                                (loadFace candidate)
                                (fun _ ->
                                    if
                                        arbiter.Snapshot = ResourceState.Pending
                                        && not disposed
                                    then
                                        addFont document candidate
                                        face <- candidate

                                    arbiter.Settle (ResourceOutcome.Succeeded { Family = family })
                                    |> ignore)
                                (fun error ->
                                    arbiter.Settle (ResourceOutcome.Failed (ResourceFailure.DecodeFailed ("browser " + errorName error)))
                                    |> ignore))

                arbiter :> ResourceOperation<_>

            member _.ClearCache() =
                cache.Clear ()

            member _.Dispose() =
                pageHide.Dispose ()
                disposeAll ()
        }
