namespace Ranvier.Dom

open System

/// <summary>The initial entries of a URLSearchParams.</summary>
[<RequireQualifiedAccess>]
type UrlSearchParamsInit =
    /// <summary>An <c>application/x-www-form-urlencoded</c> query; a leading <c>?</c> is skipped.</summary>
    | Query of query: string
    /// <summary>Name and value pairs, appended in order; names can repeat.</summary>
    | Pairs of pairs: (string * string) array

/// <summary>The part of a URLPattern match for one URL component.</summary>
type URLPatternComponentMatch =
    {
        /// <summary>The component of the matched URL.</summary>
        Input: string
        /// <summary>
        /// The pattern's groups in the order the provider reports them, each with its matched text; <c>None</c> for
        /// an optional group that matched nothing.
        /// </summary>
        Groups: (string * string option) array
    }

/// <summary>The result of <c>URLPattern.exec</c>.</summary>
type URLPatternMatch =
    {
        /// <summary>The input and base URL that were matched.</summary>
        Inputs: USVStringOrURLPatternInit array
        Protocol: URLPatternComponentMatch
        Username: URLPatternComponentMatch
        Password: URLPatternComponentMatch
        Hostname: URLPatternComponentMatch
        Port: URLPatternComponentMatch
        Pathname: URLPatternComponentMatch
        Search: URLPatternComponentMatch
        Hash: URLPatternComponentMatch
    }

/// <summary>
/// Constructs URL, URLSearchParams and URLPattern objects with the WHATWG URL parser of one document's engine, and
/// returns URLSearchParams entries and URLPattern matches.
/// </summary>
/// <remarks>
/// Members of the objects themselves, such as <c>URL.href</c>, <c>URLSearchParams.append</c> and
/// <c>URLPattern.test</c>, belong to the shared DOM interfaces. A URL that fails to parse, and an invalid pattern,
/// raise the engine's TypeError. Created objects stay usable after the service is disposed.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type UrlService =
    inherit IDisposable
    /// <summary><c>new URL(url, baseUrl)</c>; <c>None</c> or <c>Some null</c> parses <c>url</c> as an absolute URL.</summary>
    abstract Create: url: string * baseUrl: string option -> URL
    /// <summary><c>URL.parse(url, baseUrl)</c>: the URL, or <c>None</c> when it fails to parse.</summary>
    abstract Parse: url: string * baseUrl: string option -> URL option
    /// <summary><c>URL.canParse(url, baseUrl)</c>.</summary>
    abstract CanParse: url: string * baseUrl: string option -> bool
    /// <summary><c>new URLSearchParams(init)</c>.</summary>
    abstract CreateSearchParams: init: UrlSearchParamsInit -> URLSearchParams
    /// <summary>A snapshot of the name and value pairs, in list order.</summary>
    abstract Entries: searchParams: URLSearchParams -> (string * string) array
    /// <summary><c>new URLPattern(input, baseUrl, { ignoreCase })</c>.</summary>
    abstract CreatePattern: input: USVStringOrURLPatternInit * baseUrl: string option * ignoreCase: bool -> URLPattern
    /// <summary>
    /// <c>pattern.exec(input, baseUrl)</c>: the match, or <c>None</c>. <c>None</c> as the input matches an empty
    /// <c>URLPatternInit</c>.
    /// </summary>
    abstract Exec: pattern: URLPattern * input: USVStringOrURLPatternInit option * baseUrl: string option -> URLPatternMatch option

/// <summary>Argument validation shared by URL providers.</summary>
module UrlContract =
    /// <summary>Raises ArgumentNullException for a null string argument named <c>name</c>.</summary>
    let text (name: string) (value: string) =
        if isNull value then
            nullArg name

        value

    /// <summary>The base URL, with <c>Some null</c> read as <c>None</c>.</summary>
    let baseUrl (value: string option) =
        match value with
        | Some text when not (isNull text) -> value
        | _ -> None

    /// <summary>Raises ArgumentNullException for a null init, query, pair array or pair member.</summary>
    let searchParamsInit (value: UrlSearchParamsInit) =
        if isNull (box value) then
            nullArg "init"

        match value with
        | UrlSearchParamsInit.Query query -> text "query" query |> ignore
        | UrlSearchParamsInit.Pairs pairs ->
            if isNull pairs then
                nullArg "pairs"

            for name, entry in pairs do
                text "pairs" name |> ignore
                text "pairs" entry |> ignore

        value

    /// <summary>Raises ArgumentNullException for a null pattern input, string or init.</summary>
    let patternInput (value: USVStringOrURLPatternInit) =
        if isNull (box value) then
            nullArg "input"

        match value with
        | USVStringOrURLPatternInit.USVString text when isNull text -> nullArg "input"
        | USVStringOrURLPatternInit.URLPatternInit init when isNull init -> nullArg "input"
        | _ -> value

    /// <summary>Raises ArgumentNullException for a null object argument named <c>name</c>.</summary>
    let instance<'T when 'T: null> (name: string) (value: 'T) =
        if isNull value then
            nullArg name

        value
