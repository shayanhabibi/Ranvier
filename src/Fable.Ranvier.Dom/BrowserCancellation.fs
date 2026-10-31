namespace Ranvier.Dom

open System
open System.Threading
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Cancellation backed by the active document realm's AbortController and AbortSignal.</summary>
module BrowserCancellation =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.AbortController === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("new $0.AbortController()")>]
    let private newController (realm: obj) : obj = jsNative

    [<Emit("typeof $0.AbortSignal[$1] === 'function'")>]
    let private hasStatic (realm: obj) (name: string) : bool = jsNative

    [<Emit("$0.AbortSignal.abort($1)")>]
    let private abortedSignal (realm: obj) (reason: obj) : obj = jsNative

    [<Emit("$0.AbortSignal.abort()")>]
    let private defaultAbortedSignal (realm: obj) : obj = jsNative

    [<Emit("$0.AbortSignal.timeout($1)")>]
    let private timeoutSignal (realm: obj) (milliseconds: float) : obj = jsNative

    [<Emit("$0.AbortSignal.any($1)")>]
    let private anySignal (realm: obj) (signals: obj array) : obj = jsNative

    [<Emit("$0 instanceof $1.AbortSignal")>]
    let private isSignal (value: obj) (realm: obj) : bool = jsNative

    [<Emit("new $0.DOMException($1, $2)")>]
    let private domException (realm: obj) (message: string) (name: string) : obj = jsNative

    [<Emit("$0 != null && typeof $0 === 'object' && typeof $0.name === 'string' && Object.prototype.toString.call($0) === '[object DOMException]' ? $0.name : null")>]
    let private exceptionName (value: obj) : string = jsNative

    [<Emit("$0[$1]")>]
    let private field (target: obj) (name: string) : obj = jsNative

    [<Emit("$0.abort()")>]
    let private abortDefault (controller: obj) : unit = jsNative

    [<Emit("$0.abort($1)")>]
    let private abortWith (controller: obj) (reason: obj) : unit = jsNative

    [<Emit("$0.addEventListener('abort', $1, { once: true })")>]
    let private addAbort (signal: obj) (handler: obj -> unit) : unit = jsNative

    [<Emit("$0.removeEventListener('abort', $1)")>]
    let private removeAbort (signal: obj) (handler: obj -> unit) : unit = jsNative

    [<Emit("$0.addEventListener($1, $2, $3)")>]
    let private addListener (target: obj) (eventType: string) (listener: obj) (options: obj) : unit = jsNative

    [<Emit("$0.queueMicrotask($1)")>]
    let private queueMicrotask (realm: obj) (action: unit -> unit) : unit = jsNative

    [<Emit("new WeakMap()")>]
    let private weakMap () : obj = jsNative

    [<Emit("$0.get($1)")>]
    let private mapGet (map: obj) (key: obj) : obj = jsNative

    [<Emit("$0.set($1, $2)")>]
    let private mapSet (map: obj) (key: obj) (value: obj) : unit = jsNative

    [<Emit("$0 !== null && (typeof $0 === 'object' || typeof $0 === 'function')")>]
    let private isObject (value: obj) : bool = jsNative

    [<Emit("$0 === undefined")>]
    let private isUndefined (value: obj) : bool = jsNative

    /// <summary>The realm's real signal wrapped as a shared signal; <c>raw</c> exposes it for interoperability tests.</summary>
    type internal BrowserSignal(raw: obj, owner: obj, check: unit -> unit, reasonOf: obj -> AbortReason) =
        let mutable token: CancellationTokenSource = null

        member _.Raw = raw
        member _.Owner = owner

        member _.Token =
            if isNull token then
                let source = new CancellationTokenSource ()
                token <- source

                if unbox<bool>(field raw "aborted") then
                    source.Cancel ()
                else
                    addAbort raw (fun _ -> source.Cancel ())

            token.Token

        interface AbortSignal with
            member _.Aborted =
                check ()
                unbox<bool>(field raw "aborted")

            member _.Reason =
                check ()

                if unbox<bool>(field raw "aborted") then
                    Some (reasonOf (field raw "reason"))
                else
                    None

            member this.ThrowIfAborted() =
                match (this :> AbortSignal).Reason with
                | Some value -> raise (AbortedException value)
                | None -> ()

            member _.OnAbort callback =
                check ()

                if isNull (box callback) then
                    nullArg "callback"

                let handler = fun (_: obj) -> callback (reasonOf (field raw "reason"))
                addAbort raw handler

                { new IDisposable with
                    member _.Dispose() =
                        removeAbort raw handler
                }

    /// <summary>Reads the raw reason of an aborting signal from <c>signal</c>; null for a shared signal of another kind.</summary>
    let rawSignal (signal: AbortSignal) : obj =
        match box signal with
        | :? BrowserSignal as browser -> browser.Raw
        | _ -> null

    /// <summary>A cancellation domain over the document realm's AbortController and AbortSignal statics.</summary>
    /// <remarks>Statics the realm lacks raise NotSupportedException; the provider never emulates them.</remarks>
    let forDocument (document: Document) : CancellationService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Cancellation requires an active document Window with AbortController")

        let window = realm document
        let identity = obj ()
        let disposed = ref false
        let wrappers = weakMap ()
        let reasons = weakMap ()

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof CancellationService))

            if not (current document) then
                raise (InvalidOperationException "The cancellation document is no longer active")

        let reasonOf (raw: obj) : AbortReason =
            if isObject raw then
                let known = mapGet reasons raw

                if isUndefined known then
                    let value =
                        match exceptionName raw with
                        | "AbortError" -> AbortReason.Abort
                        | "TimeoutError" -> AbortReason.Timeout
                        | _ -> AbortReason.Custom (Timing.fromRaw raw)

                    mapSet reasons raw (box value)
                    value
                else
                    unbox known
            else
                AbortReason.Custom (Timing.fromRaw raw)

        let wrap (raw: obj) : AbortSignal =
            let known = mapGet wrappers raw

            if isUndefined known then
                let value = BrowserSignal (raw, identity, check, reasonOf)
                mapSet wrappers raw (box value)
                value :> AbortSignal
            else
                unbox known

        let own (value: AbortSignal) =
            match box value with
            | :? BrowserSignal as browser when obj.ReferenceEquals (browser.Owner, identity) -> browser
            | _ -> raise (ArgumentException "The signal belongs to another cancellation service")

        let toRaw (reason: AbortReason) =
            match CancellationContract.reason reason with
            | AbortReason.Abort -> None
            | AbortReason.Timeout as value ->
                let raw = domException window "signal timed out" "TimeoutError"
                mapSet reasons raw (box value)
                Some raw
            | AbortReason.Custom detail as value ->
                let raw = Timing.toRaw detail

                if isObject raw then
                    mapSet reasons raw (box value)

                Some raw

        let requireStatic name =
            if not (hasStatic window name) then
                raise (NotSupportedException $"The document realm has no AbortSignal.{name}")

        { new CancellationService with
            member _.CreateController() =
                check ()
                let controller = newController window
                let target = wrap (field controller "signal")

                { new AbortController with
                    member _.Signal = target

                    member _.Abort() =
                        check ()
                        abortDefault controller

                    member _.Abort(reason: AbortReason) =
                        check ()

                        match toRaw reason with
                        | None -> abortDefault controller
                        | Some raw -> abortWith controller raw
                }

            member _.Abort reason =
                check ()
                requireStatic "abort"

                match toRaw reason with
                | None -> wrap (defaultAbortedSignal window)
                | Some raw -> wrap (abortedSignal window raw)

            member _.Timeout milliseconds =
                check ()
                let duration = CancellationContract.timeout milliseconds
                requireStatic "timeout"
                wrap (timeoutSignal window duration)

            member _.Any signals =
                check ()

                let sources =
                    CancellationContract.signals signals
                    |> Array.map own

                requireStatic "any"
                wrap (anySignal window (sources |> Array.map _.Raw))

            member _.Listen(target, eventType, listener, options, abortSignal) =
                check ()

                if isNull (box target) then
                    nullArg "target"

                if isNull eventType then
                    nullArg "eventType"

                let source = own abortSignal

                let options =
                    if isNull (box options) then
                        AddEventListenerOptions ()
                    else
                        options

                let raw = createEmpty<obj>
                raw?capture <- options.capture
                raw?once <- options.once

                match options.passive with
                | Some passive -> raw?passive <- passive
                | None -> ()

                raw?signal <- source.Raw
                addListener target eventType listener raw

            member _.ToCancellationToken abortSignal =
                check ()
                (own abortSignal).Token

            member _.FromCancellationToken token =
                check ()

                if token.IsCancellationRequested then
                    wrap (defaultAbortedSignal window)
                else
                    let controller = newController window

                    token.Register (fun () ->
                        queueMicrotask window (fun () ->
                            if not disposed.Value then
                                abortDefault controller))
                    |> ignore

                    wrap (field controller "signal")

            member _.Dispose() =
                disposed.Value <- true
        }

    /// <summary>Capability facts for the realm's AbortSignal statics: Exported when present, Unknown when absent.</summary>
    let capabilityProvider (document: Document) : CapabilityProvider =
        if isNull (box document) then
            nullArg "document"

        let names = [| "abort"; "timeout"; "any" |]

        let keys =
            names
            |> Array.map (fun name -> Capabilities.key CapabilityCategory.PlatformFacility ("abort-signal-" + name))

        {
            Id = "browser-cancellation"
            KnownKeys = keys
            Observe =
                fun key _ ->
                    match
                        keys
                        |> Array.tryFindIndex (fun candidate -> candidate = key)
                    with
                    | None -> System.Threading.Tasks.Task.FromResult None
                    | Some index ->
                        let observation =
                            Capabilities.unknown key "browser-cancellation" "AbortSignal static lookup" "document realm" "Static not present"

                        let observation =
                            if
                                current document
                                && hasStatic (realm document) names[index]
                            then
                                { observation with
                                    Exported =
                                        {
                                            State = CapabilityState.Yes
                                            Reason = $"The document realm defines AbortSignal.{names[index]}"
                                        }
                                }
                            else
                                observation

                        System.Threading.Tasks.Task.FromResult (Some observation)
        }
