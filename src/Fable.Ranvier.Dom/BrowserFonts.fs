namespace Ranvier.Dom

open System
open System.Collections.Generic
open Fable.Core

/// <summary>A browser font set over <c>document.fonts</c>.</summary>
[<RequireQualifiedAccess>]
module BrowserFonts =
    [<Emit("$0.fonts.status === 'loading'")>]
    let private loading (document: Document) : bool = jsNative

    [<Emit("(() => { try { return [true, $0.fonts.check($1, $2), '']; } catch (e) { return [false, false, e?.name ?? String(e)]; } })()")>]
    let private check (document: Document) (font: string) (text: string) : bool * bool * string = jsNative

    [<Emit("(() => { try { return $0.fonts.load($1, $2).then(faces => faces.length); } catch (e) { return Promise.reject(e); } })()")>]
    let private load (document: Document) (font: string) (text: string) : JS.Promise<int> = jsNative

    [<Emit("$0.fonts.ready")>]
    let private ready (document: Document) : JS.Promise<obj> = jsNative

    [<Emit("$0.then(value => $1(value), error => $2(error))")>]
    let private settled (promise: JS.Promise<'T>) (ok: 'T -> unit) (error: obj -> unit) : unit = jsNative

    [<Emit("($0?.name ?? String($0))")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("queueMicrotask($0)")>]
    let private post (action: unit -> unit) : unit = jsNative

    [<Emit("(() => { const f = $0.fonts, h = e => $1(e.type, (e.fontfaces ?? []).map(x => x.family)); for (const t of ['loading', 'loadingdone', 'loadingerror']) f.addEventListener(t, h); return { Dispose: () => { for (const t of ['loading', 'loadingdone', 'loadingerror']) f.removeEventListener(t, h); } }; })()")>]
    let private listen (document: Document) (handler: string -> string array -> unit) : IDisposable = jsNative

    let private unquote (family: string) =
        if
            family.Length >= 2
            && family.StartsWith "\""
            && family.EndsWith "\""
        then
            family.Substring (1, family.Length - 2)
        else
            family

    /// <summary>Creates a font set over the document's <c>document.fonts</c>; callbacks run as microtasks of its realm.</summary>
    let forDocument (document: Document) : FontSet =
        if isNull (box document) then
            nullArg "document"

        let live = HashSet<IDisposable>(HashIdentity.Reference)
        let subscriptions = HashSet<IDisposable>(HashIdentity.Reference)
        let mutable disposed = false

        let ensure () =
            if disposed then
                raise (ObjectDisposedException "FontSet")

        let arbiter (terminal: ResourceOutcome<'T> -> unit) =
            let mutable value = Unchecked.defaultof<ResourceArbiter<'T>>

            value <-
                new ResourceArbiter<'T> (
                    post,
                    (fun outcome ->
                        live.Remove value |> ignore
                        terminal outcome),
                    ignore,
                    ignore
                )

            live.Add value |> ignore
            value

        { new FontSet with
            member _.Status =
                ensure ()

                if loading document then
                    FontSetStatus.Loading
                else
                    FontSetStatus.Loaded

            member _.Check(font, text) =
                ensure ()
                FontSetContract.validate font text

                match check document font text with
                | true, result, _ -> Ok result
                | _, _, name -> Error name

            member _.Load(font, text, terminal) =
                ensure ()
                FontSetContract.validate font text
                let operation = arbiter terminal

                settled
                    (load document font text)
                    (fun count ->
                        operation.Settle (ResourceOutcome.Succeeded count)
                        |> ignore)
                    (fun error ->
                        operation.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed (errorName error)))
                        |> ignore)

                operation :> ResourceOperation<int>

            member _.Ready terminal =
                ensure ()
                let operation = arbiter terminal

                settled
                    (ready document)
                    (fun _ ->
                        operation.Settle (ResourceOutcome.Succeeded ())
                        |> ignore)
                    (fun error ->
                        operation.Settle (ResourceOutcome.Failed (ResourceFailure.LoadFailed (errorName error)))
                        |> ignore)

                operation :> ResourceOperation<unit>

            member _.Subscribe handler =
                ensure ()

                let listener =
                    listen document (fun kind families ->
                        let families = families |> Array.map unquote |> List.ofArray

                        match kind with
                        | "loading" -> handler FontSetEvent.Loading
                        | "loadingdone" -> handler (FontSetEvent.LoadingDone families)
                        | _ -> handler (FontSetEvent.LoadingError families))

                let mutable subscription = Unchecked.defaultof<IDisposable>

                subscription <-
                    { new IDisposable with
                        member _.Dispose() =
                            if subscriptions.Remove subscription then
                                listener.Dispose ()
                    }

                subscriptions.Add subscription |> ignore
                subscription

          interface IDisposable with
              member _.Dispose() =
                  if not disposed then
                      disposed <- true

                      for operation in List.ofSeq live do
                          operation.Dispose ()

                      for subscription in List.ofSeq subscriptions do
                          subscription.Dispose ()
        }
