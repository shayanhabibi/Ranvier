namespace Ranvier.Dom

open System
open System.Collections.Generic
open Fable.Core

/// <summary>Idle callbacks of a browser document's Window.</summary>
module BrowserIdle =
    [<Emit("$0.defaultView")>]
    let private windowOf (document: Document) : obj = jsNative

    [<Emit("typeof $0?.requestIdleCallback === 'function' && typeof $0?.cancelIdleCallback === 'function'")>]
    let private supported (window: obj) : bool = jsNative

    [<Emit("$0.requestIdleCallback($1, $2 == null ? undefined : { timeout: $2 })")>]
    let private requestIdle (window: obj) (callback: obj -> unit) (timeout: float option) : int = jsNative

    [<Emit("$0.cancelIdleCallback($1)")>]
    let private cancelIdle (window: obj) (id: int) : unit = jsNative

    [<Emit("$0.timeRemaining()")>]
    let private timeRemaining (deadline: obj) : float = jsNative

    [<Emit("$0.didTimeout")>]
    let private didTimeout (deadline: obj) : bool = jsNative

    let private window (document: Document) =
        if isNull (box document) then
            nullArg "document"

        windowOf document

    /// <summary>A scheduler over <c>document</c>'s Window <c>requestIdleCallback</c>, or None when the Window lacks
    /// it.</summary>
    let forDocument (document: Document) : IdleScheduler option =
        let window = window document

        if not (supported window) then
            None
        else
            let mutable disposed = false
            let mutable next = 0
            let pending = Dictionary<int, int>()

            Some
                { new IdleScheduler with
                    member _.Idle(timeout, callback) =
                        if disposed then
                            raise (ObjectDisposedException (nameof IdleScheduler))

                        next <- next + 1
                        let id = next

                        let invoke (deadline: obj) =
                            if pending.Remove id then
                                callback
                                    { new IdleDeadline with
                                        member _.TimeRemaining() =
                                            timeRemaining deadline

                                        member _.DidTimeout = didTimeout deadline
                                    }

                        pending[id] <- requestIdle window invoke (timeout |> Option.map (max 0.0))

                        { new IDisposable with
                            member _.Dispose() =
                                match pending.TryGetValue id with
                                | true, handle ->
                                    pending.Remove id |> ignore
                                    cancelIdle window handle
                                | _ -> ()
                        }

                    member _.Dispose() =
                        if not disposed then
                            disposed <- true

                            for handle in Seq.toArray pending.Values do
                                cancelIdle window handle

                            pending.Clear ()
                }

    /// <summary>The <c>idle.callbacks</c> capability of <c>document</c>'s Window.</summary>
    let capabilities (document: Document) : CapabilityProvider =
        if supported (window document) then
            IdleCapabilities.provider "browser-idle" "Window function presence" true "The Window provides requestIdleCallback and cancelIdleCallback."
        else
            IdleCapabilities.provider "browser-idle" "Window function presence" false "The Window lacks requestIdleCallback or cancelIdleCallback."
