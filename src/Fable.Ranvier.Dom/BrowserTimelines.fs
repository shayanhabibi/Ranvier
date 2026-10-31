namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Browser providers for animation timeline constructors in a document's active realm.</summary>
module BrowserTimelines =
    [<Emit("""(() => {
        try { const realm=$0.defaultView; return realm!=null && realm.document===$0; }
        catch { return false; }
    })()""")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("typeof $0.defaultView?.ScrollTimeline === 'function'")>]
    let private available (document: Document) : bool = jsNative

    [<Emit("new $0.defaultView.ScrollTimeline($1 == null ? {axis:$2} : {source:$1,axis:$2})")>]
    let private scroll (document: Document) (source: Element) (axis: ScrollAxis) : ScrollTimeline = jsNative

    [<Emit("new $0.defaultView.ScrollTimeline({source:null,axis:$1})")>]
    let private inactive (document: Document) (axis: ScrollAxis) : ScrollTimeline = jsNative

    [<Emit("$0.name || 'Error'")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("$0.message || String($0)")>]
    let private errorMessage (error: obj) : string = jsNative

    /// <summary>The animation timeline constructors of the document's realm.</summary>
    let forDocument (document: Document) : TimelineService =
        if isNull (box document) then
            nullArg (nameof document)

        if not (current document) || not (available document) then
            raise (NotSupportedException "Scroll timelines require an active document with the ScrollTimeline constructor")

        let mutable disposed = false

        let active () =
            if disposed then
                raise (ObjectDisposedException "TimelineService")

            if not (current document) then
                raise (InvalidOperationException "The timeline document is no longer active")

        let engine action =
            try
                action ()
            with error ->
                raise (AnimationException (errorName (box error), errorMessage (box error)))

        { new TimelineService with
            member _.CreateScrollTimeline(?source, ?axis) =
                active ()
                engine (fun () -> scroll document (defaultArg source null) (defaultArg axis ScrollAxis.Block))

            member _.CreateScrollTimelineWithoutSource?axis =
                active ()
                engine (fun () -> inactive document (defaultArg axis ScrollAxis.Block))

          interface IDisposable with
              member _.Dispose() =
                  disposed <- true
        }
