namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Custom highlights backed by the document realm's Highlight constructor and CSS.highlights.</summary>
module BrowserHighlights =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.Highlight === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("$0.CSS.highlights")>]
    let private registry (realm: obj) : HighlightRegistry = jsNative

    [<Emit("new $0.Highlight(...$1)")>]
    let private create (realm: obj) (ranges: AbstractRange array) : Highlight = jsNative

    [<Emit("$0.size")>]
    let private size (target: obj) : int = jsNative

    [<Emit("$0.has($1)")>]
    let private has (target: obj) (key: obj) : bool = jsNative

    [<Emit("$0.add($1)")>]
    let private add (target: Highlight) (range: AbstractRange) : unit = jsNative

    [<Emit("$0.delete($1)")>]
    let private delete (target: obj) (key: obj) : bool = jsNative

    [<Emit("$0.clear()")>]
    let private clear (target: obj) : unit = jsNative

    [<Emit("Array.from($0.values())")>]
    let private values (target: Highlight) : AbstractRange array = jsNative

    [<Emit("$0.get($1)")>]
    let private get (target: HighlightRegistry) (name: string) : Highlight = jsNative

    [<Emit("$0.set($1, $2)")>]
    let private set (target: HighlightRegistry) (name: string) (highlight: Highlight) : unit = jsNative

    [<Emit("Array.from($0.entries())")>]
    let private entries (target: HighlightRegistry) : (string * Highlight) array = jsNative

    [<Emit("typeof $0.highlightsFromPoint === 'function'")>]
    let private hasHitTest (target: HighlightRegistry) : bool = jsNative

    [<Emit("Array.from($0.highlightsFromPoint($1, $2), hit => ({ Highlight: hit.highlight, Ranges: Array.from(hit.ranges) }))")>]
    let private hitTest (target: HighlightRegistry) (x: float) (y: float) : HighlightHit array = jsNative

    /// <summary>A highlight service for <c>document</c>'s realm.</summary>
    let forDocument (document: Document) : HighlightService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Custom highlights require an active document Window")

        let window = realm document
        let highlights = registry window
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof HighlightService))

            if not (current document) then
                raise (InvalidOperationException "The highlight document is no longer active")

        let highlight (value: Highlight) =
            HighlightContract.instance "highlight" value

        let range (value: AbstractRange) =
            HighlightContract.instance "range" value

        let target (value: HighlightRegistry) =
            HighlightContract.instance "registry" value

        { new HighlightService with
            member _.Registry =
                check ()
                highlights

            member _.Create initialRanges =
                check ()
                create window (HighlightContract.ranges initialRanges)

            member _.Size value =
                check ()
                size (highlight value)

            member _.Has(value, item) =
                check ()
                has (highlight value) (range item)

            member _.Add(value, item) =
                check ()
                add (highlight value) (range item)

            member _.Delete(value, item) =
                check ()
                delete (highlight value) (range item)

            member _.Clear value =
                check ()
                clear (highlight value)

            member _.Ranges value =
                check ()
                values (highlight value)

            member _.Count value =
                check ()
                size (target value)

            member _.Contains(value, name) =
                check ()
                has (target value) (HighlightContract.name name)

            member _.TryGet(value, name) =
                check ()

                match get (target value) (HighlightContract.name name) with
                | null -> None
                | found -> Some found

            member _.Set(value, name, item) =
                check ()
                set (target value) (HighlightContract.name name) (highlight item)

            member _.Remove(value, name) =
                check ()
                delete (target value) (HighlightContract.name name)

            member _.ClearRegistry value =
                check ()
                clear (target value)

            member _.Entries value =
                check ()
                entries (target value)

            member _.HighlightsFromPoint(x, y) =
                check ()

                if hasHitTest highlights then
                    HighlightHitTest.Hits (hitTest highlights x y)
                else
                    HighlightHitTest.Unsupported

            member _.Dispose() =
                disposed.Value <- true
        }
