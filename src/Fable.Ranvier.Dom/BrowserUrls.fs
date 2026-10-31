namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>URL, URLSearchParams and URLPattern construction backed by the active document realm.</summary>
module BrowserUrls =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.URLPattern === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("new $0.URL($1, $2)")>]
    let private create (realm: obj) (url: string) (baseUrl: string option) : URL = jsNative

    [<Emit("$0.URL.parse($1, $2)")>]
    let private parse (realm: obj) (url: string) (baseUrl: string option) : URL = jsNative

    [<Emit("$0.URL.canParse($1, $2)")>]
    let private canParse (realm: obj) (url: string) (baseUrl: string option) : bool = jsNative

    [<Emit("new $0.URLSearchParams($1)")>]
    let private searchParams (realm: obj) (init: obj) : URLSearchParams = jsNative

    [<Emit("Array.from($0.entries())")>]
    let private entries (searchParams: URLSearchParams) : (string * string) array = jsNative

    [<Emit("new $0.URLPattern($1, $2)")>]
    let private pattern (realm: obj) (input: obj) (options: obj) : URLPattern = jsNative

    [<Emit("new $0.URLPattern($1, $2, $3)")>]
    let private patternWithBase (realm: obj) (input: obj) (baseUrl: string) (options: obj) : URLPattern = jsNative

    [<Emit("$0.exec($1, $2)")>]
    let private exec (pattern: URLPattern) (input: obj) (baseUrl: string option) : obj = jsNative

    [<Emit("Object.entries($0)")>]
    let private groups (value: obj) : (string * string option) array = jsNative

    [<Emit("typeof $0 === 'string'")>]
    let private isString (value: obj) : bool = jsNative

    let private boxInput (value: USVStringOrURLPatternInit) : obj =
        match UrlContract.patternInput value with
        | USVStringOrURLPatternInit.USVString text -> box text
        | USVStringOrURLPatternInit.URLPatternInit init -> box init

    let private readInit (raw: obj) =
        let init = URLPatternInit ()

        let field (name: string) : string option =
            raw?(name)

        init.protocol <- field "protocol"
        init.username <- field "username"
        init.password <- field "password"
        init.hostname <- field "hostname"
        init.port <- field "port"
        init.pathname <- field "pathname"
        init.search <- field "search"
        init.hash <- field "hash"
        init.baseURL <- field "baseURL"
        init

    let private componentMatch (raw: obj) (name: string) =
        let value = raw?(name)

        {
            Input = value?input
            Groups = groups value?groups
        }

    let private matchResult (raw: obj) =
        {
            Inputs =
                (raw?inputs: obj array)
                |> Array.map (fun item ->
                    if isString item then
                        USVStringOrURLPatternInit.USVString (unbox<string> item)
                    else
                        USVStringOrURLPatternInit.URLPatternInit (readInit item))
            Protocol = componentMatch raw "protocol"
            Username = componentMatch raw "username"
            Password = componentMatch raw "password"
            Hostname = componentMatch raw "hostname"
            Port = componentMatch raw "port"
            Pathname = componentMatch raw "pathname"
            Search = componentMatch raw "search"
            Hash = componentMatch raw "hash"
        }

    /// <summary>A URL service for <c>document</c>'s realm.</summary>
    let forDocument (document: Document) : UrlService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "URLPattern requires an active document Window")

        let window = realm document
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof UrlService))

            if not (current document) then
                raise (InvalidOperationException "The URL document is no longer active")

        { new UrlService with
            member _.Create(url, baseUrl) =
                check ()
                create window (UrlContract.text "url" url) (UrlContract.baseUrl baseUrl)

            member _.Parse(url, baseUrl) =
                check ()

                match parse window (UrlContract.text "url" url) (UrlContract.baseUrl baseUrl) with
                | null -> None
                | value -> Some value

            member _.CanParse(url, baseUrl) =
                check ()
                canParse window (UrlContract.text "url" url) (UrlContract.baseUrl baseUrl)

            member _.CreateSearchParams init =
                check ()

                match UrlContract.searchParamsInit init with
                | UrlSearchParamsInit.Query query -> searchParams window (box query)
                | UrlSearchParamsInit.Pairs pairs -> searchParams window (box pairs)

            member _.Entries value =
                check ()
                entries (UrlContract.instance "searchParams" value)

            member _.CreatePattern(input, baseUrl, ignoreCase) =
                check ()
                let options = createObj [ "ignoreCase" ==> ignoreCase ]

                match UrlContract.baseUrl baseUrl with
                | Some baseUrl -> patternWithBase window (boxInput input) baseUrl options
                | None -> pattern window (boxInput input) options

            member _.Exec(value, input, baseUrl) =
                check ()

                let input =
                    input
                    |> Option.map boxInput
                    |> Option.defaultWith (fun () -> createEmpty<obj>)

                match exec (UrlContract.instance "pattern" value) input (UrlContract.baseUrl baseUrl) with
                | null -> None
                | raw -> Some (matchResult raw)

            member _.Dispose() =
                disposed.Value <- true
        }
