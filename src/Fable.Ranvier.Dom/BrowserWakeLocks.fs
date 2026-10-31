namespace Ranvier.Dom

open System
open System.Threading.Tasks
open Fable.Core

/// <summary>Screen wake locks of the active document realm's navigator.wakeLock.</summary>
module BrowserWakeLocks =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && $0.defaultView.navigator.wakeLock != null")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.navigator.wakeLock.request($1).then(s => [s, null], e => [null, [String(e?.name ?? 'Error'), String(e?.message ?? e)]])")>]
    let private request (document: Document) (kind: string) : JS.Promise<obj array> = jsNative

    [<Emit("$0.released")>]
    let private released (sentinel: obj) : bool = jsNative

    [<Emit("$0.release().then(() => undefined)")>]
    let private release (sentinel: obj) : JS.Promise<unit> = jsNative

    [<Emit("(() => { const f = () => $1(); $0.addEventListener('release', f); return () => $0.removeEventListener('release', f); })()")>]
    let private listen (sentinel: obj) (callback: unit -> unit) : (unit -> unit) = jsNative

    [<Emit("Promise.reject($0)")>]
    let private reject (error: exn) : Task<'T> = jsNative

    [<Emit("$0.then($1)")>]
    let private map (value: JS.Promise<'T>) (next: 'T -> 'U) : Task<'U> = jsNative

    let private sentinel (value: obj) =
        { new WakeLockSentinel with
            member _.Released = released value
            member _.Type = WakeLockType.Screen

            member _.Release() =
                unbox (release value)

            member _.OnRelease callback =
                if isNull (box callback) then
                    nullArg "callback"

                let stop = listen value callback
                let active = ref true

                { new IDisposable with
                    member _.Dispose() =
                        if active.Value then
                            active.Value <- false
                            stop ()
                }
        }

    /// <summary>A wake lock service for <c>document</c>'s realm; the browser's permission model decides each request.</summary>
    /// <exception cref="T:System.NotSupportedException">The realm has no navigator.wakeLock.</exception>
    let forDocument (document: Document) : WakeLockService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Wake locks require an active document Window with navigator.wakeLock")

        let disposed = ref false

        { new WakeLockService with
            member _.Request(kind, signal) =
                if disposed.Value then
                    raise (ObjectDisposedException (nameof WakeLockService))

                if not (current document) then
                    raise (InvalidOperationException "The wake lock document is no longer active")

                let signal = WakeLockContract.signal signal

                match signal with
                | Some aborted when aborted.Aborted -> reject (AbortedException aborted.Reason.Value)
                | _ ->
                    map (request document (WakeLockContract.typeName kind)) (fun result ->
                        match result[1] with
                        | null ->
                            let held = sentinel result[0]
                            WakeLockContract.bind signal held |> ignore
                            held
                        | failure ->
                            let values: string array = unbox failure
                            raise (WakeLockException (values[0], values[1])))

            member _.Dispose() =
                disposed.Value <- true
        }
