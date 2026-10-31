namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>The visual viewport of a browser document's Window.</summary>
/// <remarks>
/// A page cannot set its own page scale or obscured insets: those inputs report <c>Unsupported</c>. Once the
/// document has no Window, as in a removed frame, reads report <c>Detached</c>.
/// </remarks>
module BrowserVisualViewport =
    [<Emit("$0.defaultView")>]
    let private windowOf (document: Document) : obj = jsNative

    [<Emit("$0.defaultView ? $0.defaultView.visualViewport : null")>]
    let private viewportOf (document: Document) : VisualViewport = jsNative

    let private unsupported (input: string) =
        Error (VisualViewportUnavailable.Unsupported (input + " is set by the browser, not the page"))

    /// <summary>The provider over <c>document</c>'s <c>window.visualViewport</c>.</summary>
    /// <exception cref="T:System.NotSupportedException">The document has no Window with a visualViewport.</exception>
    let provider (document: Document) : VisualViewportProvider =
        if isNull (box document) then
            nullArg "document"

        let viewport = viewportOf document

        if isNull (box viewport) then
            raise (NotSupportedException "A visual viewport requires a document with a Window")

        {
            Viewport = viewport
            Read =
                fun () ->
                    if isNull (windowOf document) then
                        Error VisualViewportUnavailable.Detached
                    else
                        Ok
                            {
                                OffsetLeft = viewport.offsetLeft
                                OffsetTop = viewport.offsetTop
                                PageLeft = viewport.pageLeft
                                PageTop = viewport.pageTop
                                Width = viewport.width
                                Height = viewport.height
                                Scale = viewport.scale
                            }
            SetPageScale = fun _ -> unsupported "The page scale"
            SetObscuredInsets = fun _ -> unsupported "The obscured insets"
        }

    /// <summary>A visual viewport service over <c>document</c>'s Window.</summary>
    let forDocument (document: Document) : VisualViewportService =
        VisualViewports.create (provider document)
