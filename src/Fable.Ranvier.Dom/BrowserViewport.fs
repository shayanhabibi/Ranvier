namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Viewport dimensions and change delivery of a browser document, observed through its Window.</summary>
module BrowserViewport =
    [<Emit("$0.defaultView")>]
    let private windowOf (document: Document) : EventTarget = jsNative

    [<Emit("$0[$1]")>]
    let private number (window: EventTarget, name: string) : float = jsNative

    let private metricsOf (window: EventTarget) =
        {
            InnerWidth = int (number (window, "innerWidth"))
            InnerHeight = int (number (window, "innerHeight"))
            OuterWidth = int (number (window, "outerWidth"))
            OuterHeight = int (number (window, "outerHeight"))
            DevicePixelRatio = number (window, "devicePixelRatio")
        }

    [<Emit("$0.matchMedia($1)")>]
    let private matchMedia (window: EventTarget, query: string) : EventTarget = jsNative

    [<Emit("$0.media")>]
    let private mediaOf (list: EventTarget) : string = jsNative

    [<Emit("$0.matches")>]
    let private matchesOf (list: EventTarget) : bool = jsNative

    /// <summary>The provider over <c>document</c>'s Window.</summary>
    let provider (document: Document) : ViewportProvider =
        if isNull (box document) then
            nullArg "document"

        let window = windowOf document

        if isNull (box window) then
            raise (NotSupportedException "A viewport requires a document with a Window")

        {
            Window = window
            Document = document
            Metrics = fun () -> metricsOf window
            MatchMedia =
                fun query ->
                    let list = matchMedia (window, query)

                    { new ViewportMediaQuery with
                        member _.Media = mediaOf list
                        member _.Matches = matchesOf list
                        member _.Target = list
                        member _.Dispose() = ()
                    }
        }

    /// <summary>A viewport service over <c>document</c>'s Window.</summary>
    let forDocument (document: Document) : ViewportService =
        Viewport.create (provider document)
