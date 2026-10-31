namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Diagnostics of a browser document: the box model, from the page's own geometry and computed style.</summary>
module BrowserDiagnostics =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0")>]
    let private current (document: Document) : bool = jsNative

    // [content, padding, border, margin] as [x, y, width, height] each, or null for an element without a box: not
    // connected, display: none or contents, or an inline box. getBoundingClientRect includes the element's zoom and
    // the computed widths do not. A transformed element or ancestor answers "transformed".
    [<Emit("""(() => { const e = $0, w = e.ownerDocument.defaultView;
        if (!e.isConnected || e.getClientRects().length === 0) return null;
        const s = w.getComputedStyle(e);
        if (s.display === 'inline' || s.display === 'contents') return null;
        for (let a = e; a; a = a.parentElement) { const t = w.getComputedStyle(a); if (t.transform !== 'none' || t.rotate !== 'none' || t.scale !== 'none' || t.translate !== 'none') return 'transformed'; }
        const z = parseFloat(s.zoom) || 1, px = n => parseFloat(s.getPropertyValue(n)) * z;
        const side = p => ['top', 'right', 'bottom', 'left'].map(d => px(p.replace('*', d)));
        const [bt, br, bb, bl] = side('border-*-width'), [pt, pr, pb, pl] = side('padding-*'), [mt, mr, mb, ml] = side('margin-*');
        const r = e.getBoundingClientRect();
        const padding = [r.x + bl, r.y + bt, r.width - bl - br, r.height - bt - bb];
        return [[padding[0] + pl, padding[1] + pt, padding[2] - pl - pr, padding[3] - pt - pb], padding, [r.x, r.y, r.width, r.height],
                [r.x - ml, r.y - mt, r.width + ml + mr, r.height + mt + mb]]; })()""")>]
    let private boxes (element: Element) : obj = jsNative

    let private rect (values: float array) =
        {
            X = values[0]
            Y = values[1]
            Width = values[2]
            Height = values[3]
        }

    let private noRenderTree = "Browsers expose no render tree to pages."

    let private noMatchedRules =
        "Browsers expose no list of the style rules matching an element to pages."

    /// <summary>A diagnostics service for <c>document</c>.</summary>
    /// <remarks>
    /// <c>BoxModel</c> derives the content, padding and margin boxes from <c>getBoundingClientRect</c> and the computed
    /// border, padding and margin widths, and answers <c>Unsupported</c> for a transformed element or ancestor, whose
    /// bounding rectangle no longer determines them. The render tree and matched rules are <c>Unsupported</c>, as is a
    /// developer-tools frontend: a page cannot observe or attach one.
    /// </remarks>
    let forDocument (document: Document) : DiagnosticsService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Diagnostics require an active document Window")

        Diagnostics.create
            {
                Features =
                    {
                        RenderTree = DiagnosticsAvailability.Unsupported noRenderTree
                        MatchedRules = DiagnosticsAvailability.Unsupported noMatchedRules
                        BoxModel = DiagnosticsAvailability.Supported
                        DeveloperToolsFrontend = Diagnostics.noFrontend
                    }
                SetEnabled = ignore
                RenderTree = fun _ -> Error (DiagnosticsUnavailable.Unsupported noRenderTree)
                MatchedRules = fun _ -> Error (DiagnosticsUnavailable.Unsupported noMatchedRules)
                BoxModel =
                    fun element ->
                        match boxes element with
                        | null -> Error DiagnosticsUnavailable.NotRendered
                        | :? string -> Error (DiagnosticsUnavailable.Unsupported "The box of a transformed element is not derivable in a browser.")
                        | value ->
                            let values: float array array = unbox value

                            Ok
                                {
                                    Content = rect values[0]
                                    Padding = rect values[1]
                                    Border = rect values[2]
                                    Margin = rect values[3]
                                }
            }
