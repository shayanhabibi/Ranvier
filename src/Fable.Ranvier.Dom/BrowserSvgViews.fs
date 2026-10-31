namespace Ranvier.Dom

open Fable.Core
open Fable.Core.JsInterop

/// <summary>Observes SVG view properties exposed by the actual browser root.</summary>
module SvgViews =
    [<Emit("$1 in $0")>]
    let private exposed (root: SVGSVGElement) (name: string) : bool = jsNative

    [<Emit("$0[$1]")>]
    let private property (root: SVGSVGElement) (name: string) : obj = jsNative

    [<Emit("$0 != null && typeof $0 === $1")>]
    let private matches (value: obj) (kind: string) : bool = jsNative

    [<Emit("$0 != null && $0.name === 'NotSupportedError'")>]
    let private unsupported (error: obj) : bool = jsNative

    [<Emit("""(() => {
        try { const realm=$0.defaultView; return realm!=null && realm.document===$0; }
        catch { return false; }
    })()""")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.location.hash = $1")>]
    let private request (document: Document) (fragment: string) : unit = jsNative

    /// <summary>Requests a fragment view in an active SVG document; rendering effects occur on a later layout/frame.</summary>
    /// <param name="document">The active SVG document of its browsing context.</param>
    /// <param name="fragment">SVG fragment text, not a full URL; an empty value requests the default view.</param>
    /// <remarks>
    /// The browser updates URL, history and <c>:target</c>; native host selection changes only renderer view state.
    /// This synchronous request returns no selected flag and starts no rendering wait. Observe effects separately.
    /// </remarks>
    /// <exception cref="T:System.ArgumentNullException">The document or fragment is null.</exception>
    /// <exception cref="T:System.InvalidOperationException">The document is no longer active in its browsing context.</exception>
    /// <exception cref="T:System.NotSupportedException">The document is not an SVG XML document.</exception>
    let requestView (document: Document) (fragment: string) : unit =
        if isNull (box document) then
            nullArg "document"

        if isNull fragment then
            nullArg "fragment"

        if not (current document) then
            invalidOp "The SVG document is no longer active in its browsing context."

        if document.contentType <> "image/svg+xml" then
            raise (System.NotSupportedException "SVG view requests require an SVG XML document.")

        request document fragment

    /// <summary>Reads actual view properties and reports absent members without manufacturing view objects.</summary>
    /// <param name="root">The nonnull SVG root whose current properties are observed.</param>
    /// <exception cref="T:System.ArgumentNullException">The root is null.</exception>
    let inspect (root: SVGSVGElement) : SvgViewState =
        if isNull (box root) then
            nullArg "root"

        let read name kind =
            if not (exposed root name) then
                SvgViewAvailability.NotExposed
            else
                try
                    let value = property root name

                    if matches value kind then
                        SvgViewAvailability.Available value
                    else
                        SvgViewAvailability.Unknown $"The {name} getter returned no {kind} value."
                with error when unsupported (box error) ->
                    SvgViewAvailability.Unsupported error.Message

        let map convert =
            function
            | SvgViewAvailability.Available value -> SvgViewAvailability.Available (convert value)
            | SvgViewAvailability.NotExposed -> SvgViewAvailability.NotExposed
            | SvgViewAvailability.Unsupported reason -> SvgViewAvailability.Unsupported reason
            | SvgViewAvailability.Unknown reason -> SvgViewAvailability.Unknown reason

        {
            CurrentView =
                read "currentView" "object"
                |> map unbox<SVGViewSpec>
            UseCurrentView = read "useCurrentView" "boolean" |> map unbox<bool>
        }
