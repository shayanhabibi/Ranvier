namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Screen and ScreenOrientation of the active document realm.</summary>
module BrowserScreen =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && $0.defaultView.screen != null")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("(() => { const s = $0.screen, o = s.orientation; return [s.width, s.height, s.availLeft ?? 0, s.availTop ?? 0, s.availWidth, s.availHeight, s.colorDepth, s.pixelDepth, o ? o.type : ($0.innerHeight > $0.innerWidth ? 'portrait-primary' : 'landscape-primary'), o ? o.angle : 0]; })()")>]
    let private snapshot (realm: obj) : obj array = jsNative

    [<Emit("(() => { const o = $0.screen.orientation, f = () => $1(); $0.addEventListener('resize', f); o?.addEventListener('change', f); $0.screen.addEventListener?.('change', f); return () => { $0.removeEventListener('resize', f); o?.removeEventListener('change', f); $0.screen.removeEventListener?.('change', f); }; })()")>]
    let private listen (realm: obj) (callback: unit -> unit) : (unit -> unit) = jsNative

    [<Emit("typeof $0.screen.orientation?.lock === 'function'")>]
    let private hasLock (realm: obj) : bool = jsNative

    [<Emit("$0.screen.orientation.lock($1).then(() => null, e => [String(e?.name ?? 'Error'), String(e?.message ?? e)])")>]
    let private lock (realm: obj) (orientation: string) : JS.Promise<string array> = jsNative

    [<Emit("$0.screen.orientation?.unlock?.()")>]
    let private unlock (realm: obj) : unit = jsNative

    let private info (values: obj array) =
        {
            Width = unbox values[0]
            Height = unbox values[1]
            AvailLeft = unbox values[2]
            AvailTop = unbox values[3]
            AvailWidth = unbox values[4]
            AvailHeight = unbox values[5]
            ColorDepth = unbox values[6]
            PixelDepth = unbox values[7]
            Orientation = ScreenContract.orientationOf (unbox values[8])
            Angle = unbox values[9]
        }

    /// <summary>A screen service for <c>document</c>'s realm.</summary>
    /// <remarks>
    /// Changes are observed through the Window's resize event and ScreenOrientation's change event, and delivered when
    /// the screen differs from the last one delivered. Lock reports the browser's own result.
    /// </remarks>
    let forDocument (document: Document) : ScreenService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Screen requires an active document Window")

        let window = realm document
        let disposed = ref false
        let stops = ResizeArray<unit -> unit>()

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof ScreenService))

            if not (current document) then
                raise (InvalidOperationException "The screen document is no longer active")

        { new ScreenService with
            member _.Read() =
                check ()
                info (snapshot window)

            member _.Subscribe handler =
                check ()
                let handler = ScreenContract.handler handler
                let last = ref (info (snapshot window))
                let active = ref true

                let stop =
                    listen window (fun () ->
                        if active.Value then
                            let next = info (snapshot window)

                            if next <> last.Value then
                                last.Value <- next
                                handler next)

                stops.Add stop

                { new IDisposable with
                    member _.Dispose() =
                        if active.Value then
                            active.Value <- false
                            stop ()
                            stops.Remove stop |> ignore
                }

            member _.Lock orientation =
                check ()
                let orientation = ScreenContract.lockType orientation

                if not (hasLock window) then
                    async { return ScreenContract.unsupportedLock }
                else
                    let pending = lock window orientation

                    async {
                        let! failure = Async.AwaitPromise pending

                        return
                            match failure with
                            | null -> ScreenLockOutcome.Locked
                            | [| "NotSupportedError"; _ |] -> ScreenContract.unsupportedLock
                            | values -> ScreenLockOutcome.Failed (values[0], values[1])
                    }

            member _.Unlock() =
                check ()
                unlock window

            member _.Dispose() =
                disposed.Value <- true

                for stop in List.ofSeq stops do
                    stop ()

                stops.Clear ()
        }
