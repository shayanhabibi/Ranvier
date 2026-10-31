namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Viewport scrolling of a browser document through its Window.</summary>
module BrowserViewportScroll =
    [<Emit("$0.defaultView")>]
    let private windowOf (document: Document) : obj = jsNative

    [<Emit("$0.defaultView ? $0.defaultView[$1] : 0")>]
    let private offset (document: Document, name: string) : float = jsNative

    [<Emit("$0.defaultView && $0.defaultView[$1]($2)")>]
    let private call (document: Document, name: string, options: obj) : unit = jsNative

    let private options (request: ScrollRequest) =
        let behavior =
            match request.Behavior with
            | ScrollBehavior.Auto -> "auto"
            | ScrollBehavior.Instant -> "instant"
            | ScrollBehavior.Smooth -> "smooth"

        let result = createObj [ "behavior" ==> behavior ]

        match request.Left with
        | Some left -> result?left <- left
        | None -> ()

        match request.Top with
        | Some top -> result?top <- top
        | None -> ()

        result

    /// <summary>A scroll service over <c>document</c>'s Window.</summary>
    /// <exception cref="T:System.NotSupportedException">The document has no Window.</exception>
    let forDocument (document: Document) : ViewportScrollService =
        if isNull (box document) then
            nullArg "document"

        if isNull (windowOf document) then
            raise (NotSupportedException "Viewport scrolling requires a document with a Window")

        { new ViewportScrollService with
            member _.Position() =
                {
                    X = offset (document, "scrollX")
                    Y = offset (document, "scrollY")
                }

            member _.ScrollTo request =
                call (document, "scrollTo", options request)

            member _.ScrollBy request =
                call (document, "scrollBy", options request)
        }
