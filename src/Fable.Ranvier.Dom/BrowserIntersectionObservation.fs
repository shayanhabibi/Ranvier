namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Intersection observation of a browser document through its Window's <c>IntersectionObserver</c>.</summary>
module BrowserIntersectionObservation =
    [<Emit("$0.defaultView")>]
    let private windowOf (document: Document) : obj = jsNative

    [<Emit("$0.performance.now()")>]
    let private now (window: obj) : float = jsNative

    [<Emit("new $0.IntersectionObserver($1, { root: $2, rootMargin: $3, scrollMargin: $4, threshold: $5 })")>]
    let private construct
        (window: obj, callback: obj array -> unit, root: obj, rootMargin: string, scrollMargin: string, threshold: float array)
        : obj =
        jsNative

    [<Emit("$0.observe($1)")>]
    let private observe (observer: obj, target: Element) : unit = jsNative

    [<Emit("$0.unobserve($1)")>]
    let private unobserve (observer: obj, target: Element) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let private disconnect (observer: obj) : unit = jsNative

    [<Emit("$0.takeRecords()")>]
    let private takeRecords (observer: obj) : obj array = jsNative

    // [time, hasRoot, root x, y, width, height, bounds x, y, width, height, intersection x, y, width, height,
    //  isIntersecting, ratio].
    [<Emit("""(() => { const e = $0, r = e.rootBounds, b = e.boundingClientRect, i = e.intersectionRect;
        return [e.time, r ? 1 : 0, r ? r.x : 0, r ? r.y : 0, r ? r.width : 0, r ? r.height : 0, b.x, b.y, b.width, b.height,
            i.x, i.y, i.width, i.height, e.isIntersecting ? 1 : 0, e.intersectionRatio]; })()""")>]
    let private values (entry: obj) : float array = jsNative

    [<Emit("$0.target")>]
    let private targetOf (entry: obj) : Element = jsNative

    // The DOMException name, or null for another error.
    [<Emit("($0 && typeof $0.name === 'string' && ($0.name === 'SyntaxError' || $0 instanceof RangeError)) ? $0.name : null")>]
    let private optionError (error: obj) : string = jsNative

    [<Emit("$0.message")>]
    let private messageOf (error: obj) : string = jsNative

    let private rect (v: float array) start : IntersectionObserverRect =
        {
            X = v[start]
            Y = v[start + 1]
            Width = v[start + 2]
            Height = v[start + 3]
        }

    let private copy (entry: obj) : IntersectionObserverEntry =
        let v = values entry

        {
            Target = targetOf entry
            Time = v[0]
            RootBounds = if v[1] = 1.0 then Some (rect v 2) else None
            BoundingClientRect = rect v 6
            IntersectionRect = rect v 10
            IsIntersecting = v[14] = 1.0
            IntersectionRatio = v[15]
        }

    /// <summary>The provider over <c>document</c>'s Window.</summary>
    /// <remarks>An exception a callback raises reaches the window's error event; other observers keep delivering.</remarks>
    let provider (document: Document) : IntersectionObservationProvider =
        if isNull (box document) then
            nullArg "document"

        let window = windowOf document

        if isNull window then
            raise (NotSupportedException "Intersection observation requires a document with a Window")

        {
            Now = fun () -> now window
            Create =
                fun init deliver ->
                    let root: obj =
                        match init.Root with
                        | IntersectionObserverRoot.Implicit -> null
                        | IntersectionObserverRoot.Document -> box document
                        | IntersectionObserverRoot.Element element -> box element

                    let observer =
                        try
                            construct (
                                window,
                                (fun entries -> deliver (Array.map copy entries)),
                                root,
                                init.RootMargin,
                                init.ScrollMargin,
                                init.Threshold
                            )
                        with error ->
                            match optionError (box error) with
                            | null -> reraise ()
                            | name -> raise (IntersectionObserverException (name, messageOf (box error)))

                    {
                        Observe = fun target -> observe (observer, target)
                        Unobserve = fun target -> unobserve (observer, target)
                        Disconnect = fun () -> disconnect observer
                        TakeRecords = fun () -> Array.map copy (takeRecords observer)
                        Release = fun () -> disconnect observer
                    }
        }

    /// <summary>An intersection observation service of <c>document</c>.</summary>
    let forDocument (document: Document) : IntersectionObservationService =
        IntersectionObservation.create (provider document)
