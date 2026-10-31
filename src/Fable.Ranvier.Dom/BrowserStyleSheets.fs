namespace Ranvier.Dom

open System
open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Browser providers for stylesheet construction and adoption in a document's Window.</summary>
module StyleSheets =
    [<Emit("$0.defaultView != null")>]
    let private hasWindow (document: Document) : bool = jsNative

    [<Emit("new $0.defaultView.CSSStyleSheet($1)")>]
    let private construct (document: Document) (options: obj) : CSSStyleSheet = jsNative

    [<Emit("Array.from($0.adoptedStyleSheets)")>]
    let private snapshot (scope: Node) : CSSStyleSheet array = jsNative

    [<Emit("$0.adoptedStyleSheets = $1")>]
    let private adopt (scope: Node) (sheets: CSSStyleSheet array) : unit = jsNative

    [<Emit("$0.nodeType === 9 || ($0.nodeType === 11 && $0.host != null)")>]
    let private isScope (scope: Node) : bool = jsNative

    [<Emit("$0.replace($1)")>]
    let private replace (sheet: CSSStyleSheet) (text: string) : JS.Promise<CSSStyleSheet> = jsNative

    [<Emit("$0.then($1, $2)")>]
    let private observe (promise: JS.Promise<CSSStyleSheet>) (success: CSSStyleSheet -> unit) (error: obj -> unit) : unit = jsNative

    [<Emit("queueMicrotask($0)")>]
    let private post (action: unit -> unit) : unit = jsNative

    [<Emit("$0.name || 'Error'")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("$0.message || String($0)")>]
    let private errorMessage (error: obj) : string = jsNative

    let private failure error =
        StyleSheetException (errorName error, errorMessage error)

    /// <summary>Creates an owned provider bound to the document's Window.</summary>
    /// <param name="document">The owning page document; documents without a Window are unsupported.</param>
    let forDocument (document: Document) : StyleSheetService =
        if isNull (box document) then
            nullArg (nameof document)

        if not (hasWindow document) then
            raise (NotSupportedException "The stylesheet service requires a document with a Window")

        let supportsBaseUrl =
            try
                construct document (createObj [ "baseURL" ==> "http://[" ])
                |> ignore

                false
            with error ->
                let name = errorName (box error)
                name = "NotAllowedError" || name = "SyntaxError"

        let pending = Dictionary<int, unit -> unit>()
        let mutable nextId = 0
        let mutable disposed = false

        let active () =
            if disposed then
                raise (ObjectDisposedException "StyleSheetService")

        let scope (value: Node) =
            active ()

            if isNull (box value) then
                nullArg (nameof value)

            if not (isScope value) then
                invalidArg (nameof value) "A stylesheet scope must be a Document or ShadowRoot"

            let owner =
                if value.nodeType = 9us then
                    unbox<Document> value
                else
                    value.ownerDocument

            if not (obj.ReferenceEquals (owner, document)) then
                invalidArg (nameof value) "The scope belongs to another document"

        let run action =
            try
                action ()
            with error ->
                raise (failure (box error))

        { new StyleSheetService with
            member _.SupportsBaseUrl = supportsBaseUrl

            member _.Create options =
                active ()

                if isNull (box options) then
                    nullArg (nameof options)

                let baseUrl =
                    match options.BaseUrl with
                    | Some url when not (isNull url) -> Some url
                    | _ -> None

                if baseUrl.IsSome && not supportsBaseUrl then
                    raise (NotSupportedException "This browser does not support explicit stylesheet base URLs")

                let media =
                    match options.Media with
                    | MediaText text ->
                        if isNull text then
                            nullArg "options.Media"

                        box text
                    | MediaList list ->
                        if isNull (box list) then
                            nullArg "options.Media"

                        box list

                let properties = ResizeArray<string * obj>()
                properties.Add ("disabled", box options.Disabled)
                properties.Add ("media", media)

                match baseUrl with
                | Some url -> properties.Add ("baseURL", box url)
                | None -> ()

                run (fun () -> construct document (createObj properties))

            member _.GetAdopted target =
                scope target
                run (fun () -> snapshot target)

            member _.SetAdopted(target, sheets) =
                scope target

                if isNull sheets then
                    nullArg (nameof sheets)

                if sheets |> Array.exists (box >> isNull) then
                    nullArg (nameof sheets)

                run (fun () ->
                    let previous = snapshot target

                    try
                        adopt target sheets
                    with _ ->
                        adopt target previous
                        reraise ())

            member _.ReplaceAsync(sheet, text) =
                async {
                    let! token = Async.CancellationToken
                    active ()

                    if isNull (box sheet) then
                        nullArg (nameof sheet)

                    if isNull text then
                        nullArg (nameof text)

                    return!
                        Async.FromContinuations (fun (success, error, cancel) ->
                            nextId <- nextId + 1
                            let id = nextId
                            let mutable finished = false
                            let mutable unregister = ignore

                            let finish action =
                                if not finished then
                                    finished <- true
                                    pending.Remove id |> ignore
                                    unregister ()
                                    action ()

                            let cancelled () =
                                finish (fun () -> cancel (OperationCanceledException token))

                            pending[id] <- cancelled
                            let registration = token.Register (Action (fun () -> post cancelled))
                            unregister <- fun () -> (registration :> IDisposable).Dispose()

                            if token.IsCancellationRequested then
                                cancelled ()
                            else
                                try
                                    observe
                                        (replace sheet text)
                                        (fun value ->
                                            if token.IsCancellationRequested then
                                                cancelled ()
                                            else
                                                finish (fun () -> success value))
                                        (fun reason ->
                                            if token.IsCancellationRequested then
                                                cancelled ()
                                            else
                                                finish (fun () -> error (failure reason)))
                                with reason ->
                                    post (fun () -> finish (fun () -> error (failure (box reason)))))
                }

            member _.Dispose() =
                if not disposed then
                    disposed <- true

                    for cancel in pending.Values |> Seq.toArray do
                        cancel ()
        }
