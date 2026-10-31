namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Resize observation of a browser document through its Window's <c>ResizeObserver</c>.</summary>
module BrowserResizeObservation =
    [<Emit("$0.defaultView")>]
    let private windowOf (document: Document) : obj = jsNative

    [<Emit("typeof $0.ResizeObserverEntry === 'function' && 'devicePixelContentBoxSize' in $0.ResizeObserverEntry.prototype")>]
    let private hasDevicePixel (window: obj) : bool = jsNative

    [<Emit("new $0.ResizeObserver($1)")>]
    let private construct (window: obj, callback: obj array -> unit) : obj = jsNative

    [<Emit("$0.observe($1, { box: $2 })")>]
    let private observe (observer: obj, target: Element, box: string) : unit = jsNative

    [<Emit("$0.unobserve($1)")>]
    let private unobserve (observer: obj, target: Element) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let private disconnect (observer: obj) : unit = jsNative

    // [x, y, width, height, then inline and block of each content, border and device-pixel size], with the counts of
    // the three size lists at indices 4, 5 and 6.
    [<Emit("""(() => { const e = $0, r = e.contentRect, l = s => s ? Array.from(s) : [];
        const c = l(e.contentBoxSize), b = l(e.borderBoxSize), d = l(e.devicePixelContentBoxSize);
        return [r.x, r.y, r.width, r.height, c.length, b.length, d.length].concat(
            ...[c, b, d].map(s => s.flatMap(z => [z.inlineSize, z.blockSize]))); })()""")>]
    let private values (entry: obj) : float array = jsNative

    [<Emit("$0.target")>]
    let private targetOf (entry: obj) : Element = jsNative

    let private copy (entry: obj) : ResizeObserverEntry =
        let v = values entry
        let counts = [| int v[4]; int v[5]; int v[6] |]
        let starts = [| 7; 7 + 2 * counts[0]; 7 + 2 * (counts[0] + counts[1]) |]

        let sizes index =
            let start = starts[index]

            Array.init counts[index] (fun item ->
                {
                    InlineSize = v[start + 2 * item]
                    BlockSize = v[start + 2 * item + 1]
                })

        {
            Target = targetOf entry
            ContentRect =
                {
                    X = v[0]
                    Y = v[1]
                    Width = v[2]
                    Height = v[3]
                }
            ContentBoxSize = sizes 0
            BorderBoxSize = sizes 1
            DevicePixelContentBoxSize = sizes 2
        }

    let private boxName (observed: ResizeObserverBox) =
        match observed with
        | ResizeObserverBox.ContentBox -> "content-box"
        | ResizeObserverBox.BorderBox -> "border-box"
        | ResizeObserverBox.DevicePixelContentBox -> "device-pixel-content-box"

    /// <summary>The provider over <c>document</c>'s Window.</summary>
    /// <remarks>An exception a callback raises reaches the window's error event; other observers keep delivering.</remarks>
    let provider (document: Document) : ResizeObservationProvider =
        if isNull (box document) then
            nullArg "document"

        let window = windowOf document

        if isNull window then
            raise (NotSupportedException "Resize observation requires a document with a Window")

        let devicePixel = hasDevicePixel window

        {
            Supports =
                fun observed ->
                    match observed with
                    | ResizeObserverBox.DevicePixelContentBox -> devicePixel
                    | _ -> true
            Create =
                fun deliver ->
                    let observer = construct (window, (fun entries -> deliver (Array.map copy entries)))

                    {
                        Observe = fun target observed -> observe (observer, target, boxName observed)
                        Unobserve = fun target -> unobserve (observer, target)
                        Disconnect = fun () -> disconnect observer
                        Release = fun () -> disconnect observer
                    }
        }

    /// <summary>A resize observation service of <c>document</c>.</summary>
    let forDocument (document: Document) : ResizeObservationService =
        ResizeObservation.create (provider document)
