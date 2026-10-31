namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Coordinate conversion in a browser document, from the page's own geometry, computed transforms, scroll,
/// visual viewport and device pixel ratio.</summary>
module BrowserCoordinates =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0")>]
    let private current (document: Document) : bool = jsNative

    // [scrollX, scrollY, visualViewport.offsetLeft, offsetTop, scale, devicePixelRatio], or null without a Window.
    [<Emit("""(() => { const w = $0.defaultView; if (!w) return null; const v = w.visualViewport;
        return [w.scrollX, w.scrollY, v ? v.offsetLeft : 0, v ? v.offsetTop : 0, v ? v.scale : 1, w.devicePixelRatio]; })()""")>]
    let private viewport (document: Document) : float array = jsNative

    // The element's local-to-client map as [a, b, c, d, tx, ty] (client = [a c; b d] * local + t), then the border-box
    // size and the border, padding and margin widths (top, right, bottom, left each). The linear part is the product of
    // the 2D parts of the computed transforms of the element and its ancestors; the translation follows from
    // getBoundingClientRect, the bounding box of the mapped border box. A string names why the map is unavailable;
    // null is an element without a CSS box.
    [<Emit("""(() => { const el = $0, doc = $1, w = doc.defaultView;
        if (el.ownerDocument !== doc) return 'wrong';
        if (!w || !el.isConnected) return 'detached';
        const s = w.getComputedStyle(el), html = el.namespaceURI === 'http://www.w3.org/1999/xhtml';
        const replaced = /^(IMG|VIDEO|CANVAS|IFRAME|EMBED|OBJECT|INPUT|SELECT|TEXTAREA|BUTTON)$/.test(el.tagName);
        if (el.getClientRects().length === 0 || s.display === 'contents' || (html && s.display === 'inline' && !replaced)
            || (el.namespaceURI === 'http://www.w3.org/2000/svg' && el.ownerSVGElement)) return null;
        let a = 1, b = 0, c = 0, d = 1;
        for (let e = el; e; e = e.parentElement) {
          const t = w.getComputedStyle(e);
          if (t.rotate !== 'none' || t.scale !== 'none' || t.translate !== 'none' || (t.offsetPath && t.offsetPath !== 'none')) return 'individual';
          if (t.zoom && parseFloat(t.zoom) !== 1) return 'zoom';
          if (e !== el && (t.perspective !== 'none' || t.transformStyle === 'preserve-3d')) return 'projective';
          const m = new w.DOMMatrix(t.transform === 'none' ? undefined : t.transform);
          if (m.m14 !== 0 || m.m24 !== 0 || m.m44 !== 1) return 'projective';
          const na = m.a * a + m.c * b, nb = m.b * a + m.d * b, nc = m.a * c + m.c * d, nd = m.b * c + m.d * d;
          a = na; b = nb; c = nc; d = nd;
        }
        const px = n => parseFloat(s.getPropertyValue(n)) || 0;
        const sides = p => ['top', 'right', 'bottom', 'left'].map(x => px(p.replace('*', x)));
        const border = sides('border-*-width'), padding = sides('padding-*'), margin = sides('margin-*');
        let width = px('width'), height = px('height');
        if (s.boxSizing !== 'border-box') {
          width += padding[1] + padding[3] + border[1] + border[3];
          height += padding[0] + padding[2] + border[0] + border[2];
        }
        const xs = [0, a * width, a * width + c * height, c * height], ys = [0, b * width, b * width + d * height, d * height];
        const r = el.getBoundingClientRect();
        return [a, b, c, d, r.x - Math.min(...xs), r.y - Math.min(...ys), width, height, ...border, ...padding, ...margin]; })()""")>]
    let private affine (element: Element) (document: Document) : obj = jsNative

    let private individual =
        "The translate, rotate and scale properties and offset-path are not composed in a browser."

    let private zoom = "CSS zoom is not composed in a browser."

    let private projective =
        "A perspective, perspective() or preserve-3d chain is not derivable from a browser's geometry."

    /// <summary>A coordinate service for <c>document</c>.</summary>
    /// <remarks>
    /// An element space is the affine map of the element's computed transforms and its ancestors', placed by
    /// <c>getBoundingClientRect</c>. A chain with a perspective, perspective() or preserve-3d, the individual transform
    /// properties or CSS zoom answers <c>Unsupported</c>. <c>Page</c> is <c>Client</c> plus the window's scroll,
    /// <c>Host</c> follows the visual viewport, and <c>Device</c> is <c>Host</c> times <c>devicePixelRatio</c>.
    /// </remarks>
    /// <exception cref="T:System.NotSupportedException">The document has no Window.</exception>
    let forDocument (document: Document) : CoordinateService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Coordinates require an active document Window")

        let map (element: Element) : Result<float array, CoordinateUnavailable> =
            match affine element document with
            | null -> Error CoordinateUnavailable.NotRendered
            | :? string as reason ->
                match reason with
                | "wrong" -> Error CoordinateUnavailable.WrongDocument
                | "detached" -> Error CoordinateUnavailable.Detached
                | "individual" -> Error (CoordinateUnavailable.Unsupported individual)
                | "zoom" -> Error (CoordinateUnavailable.Unsupported zoom)
                | _ -> Error (CoordinateUnavailable.Unsupported projective)
            | value -> Ok (unbox value)

        let state () =
            match viewport document with
            | null -> Error CoordinateUnavailable.Detached
            | values -> Ok values

        // A space's map to and from client coordinates.
        let resolve (space: CoordinateSpace) (view: float array) =
            match space with
            | CoordinateSpace.Element element ->
                map element
                |> Result.map (fun m ->
                    let determinant = m[0] * m[3] - m[1] * m[2]

                    (fun (p: CoordinatePoint) ->
                        Ok
                            {
                                X = m[0] * p.X + m[2] * p.Y + m[4]
                                Y = m[1] * p.X + m[3] * p.Y + m[5]
                            }),
                    (fun (q: CoordinatePoint) ->
                        if abs determinant < 1e-12 then
                            Error CoordinateUnavailable.NonInvertible
                        else
                            let x, y = q.X - m[4], q.Y - m[5]

                            Ok
                                {
                                    X = (m[3] * x - m[2] * y) / determinant
                                    Y = (m[0] * y - m[1] * x) / determinant
                                }))
            | CoordinateSpace.Page ->
                Ok ((fun p -> Ok { X = p.X - view[0]; Y = p.Y - view[1] }), (fun q -> Ok { X = q.X + view[0]; Y = q.Y + view[1] }))
            | CoordinateSpace.Client -> Ok (Ok, Ok)
            | CoordinateSpace.Host ->
                Ok (
                    (fun p ->
                        Ok
                            {
                                X = p.X / view[4] + view[2]
                                Y = p.Y / view[4] + view[3]
                            }),
                    (fun q ->
                        Ok
                            {
                                X = (q.X - view[2]) * view[4]
                                Y = (q.Y - view[3]) * view[4]
                            })
                )
            | CoordinateSpace.Device ->
                let scale = view[4] * view[5]

                Ok (
                    (fun p ->
                        Ok
                            {
                                X = p.X / scale + view[2]
                                Y = p.Y / scale + view[3]
                            }),
                    (fun q ->
                        Ok
                            {
                                X = (q.X - view[2]) * scale
                                Y = (q.Y - view[3]) * scale
                            })
                )

        let convert (from: CoordinateSpace) (target: CoordinateSpace) (points: CoordinatePoint array) =
            state ()
            |> Result.bind (fun view ->
                resolve from view
                |> Result.bind (fun (toClient, _) ->
                    resolve target view
                    |> Result.bind (fun (_, fromClient) ->
                        let result = Array.zeroCreate points.Length
                        let mutable failure = None

                        for index in 0 .. points.Length - 1 do
                            if failure.IsNone then
                                match toClient points[index] |> Result.bind fromClient with
                                | Ok point -> result[index] <- point
                                | Error reason -> failure <- Some reason

                        match failure with
                        | Some reason -> Error reason
                        | None -> Ok result)))

        Coordinates.create
            {
                Convert = fun (from, target, points) -> convert from target points
                BoxQuad =
                    fun (element, cssBox, target) ->
                        map element
                        |> Result.bind (fun m ->
                            let width, height = m[6], m[7]

                            let side (offset: int) =
                                m[offset], m[offset + 1], m[offset + 2], m[offset + 3]

                            let bt, br, bb, bl = side 8
                            let pt, pr, pb, pl = side 12
                            let mt, mr, mb, ml = side 16

                            let x, y, w, h =
                                match cssBox with
                                | CoordinateBox.Border -> 0.0, 0.0, width, height
                                | CoordinateBox.Padding -> bl, bt, width - bl - br, height - bt - bb
                                | CoordinateBox.Content -> bl + pl, bt + pt, width - bl - br - pl - pr, height - bt - bb - pt - pb
                                | CoordinateBox.Margin -> -ml, -mt, width + ml + mr, height + mt + mb

                            [|
                                { X = x; Y = y }
                                { X = x + w; Y = y }
                                { X = x + w; Y = y + h }
                                { X = x; Y = y + h }
                            |]
                            |> convert (CoordinateSpace.Element element) target
                            |> Result.map (fun p ->
                                {
                                    P1 = p[0]
                                    P2 = p[1]
                                    P3 = p[2]
                                    P4 = p[3]
                                }))
            }
