namespace Ranvier.Dom

open System

/// <summary>A highlight painted at a viewport point, with its ranges that contain the point.</summary>
type HighlightHit =
    {
        Highlight: Highlight
        Ranges: AbstractRange array
    }

/// <summary>The result of <c>CSS.highlights.highlightsFromPoint</c>.</summary>
[<RequireQualifiedAccess>]
type HighlightHitTest =
    /// <summary>The highlights at the point, in the order the provider reports them.</summary>
    | Hits of hits: HighlightHit array
    /// <summary>The provider's engine has no <c>highlightsFromPoint</c>.</summary>
    | Unsupported

/// <summary>
/// Custom highlights of one document: <c>new Highlight(...)</c>, the setlike operations of a Highlight, and the
/// maplike operations of the document's <c>CSS.highlights</c> registry. A registered highlight styles its ranges with
/// <c>::highlight(name)</c> and leaves the text unchanged.
/// </summary>
/// <remarks>
/// A Highlight is an ordered set of ranges and the registry an ordered map from names to highlights: adding a range
/// already in the set, or setting a registered name, keeps its position, and a deleted entry added again goes last.
/// <c>Ranges</c> and <c>Entries</c> return snapshot copies in that order, unchanged by later mutation.
/// <c>Highlight.priority</c> and <c>Highlight.type</c> belong to the shared DOM interface. Created highlights stay
/// usable after the service is disposed.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type HighlightService =
    inherit IDisposable
    /// <summary>The document's <c>CSS.highlights</c>.</summary>
    abstract Registry: HighlightRegistry
    /// <summary><c>new Highlight(...initialRanges)</c>; a range given more than once is added once.</summary>
    abstract Create: initialRanges: AbstractRange array -> Highlight
    /// <summary><c>highlight.size</c>.</summary>
    abstract Size: highlight: Highlight -> int
    /// <summary><c>highlight.has(range)</c>.</summary>
    abstract Has: highlight: Highlight * range: AbstractRange -> bool
    /// <summary><c>highlight.add(range)</c>.</summary>
    abstract Add: highlight: Highlight * range: AbstractRange -> unit
    /// <summary><c>highlight.delete(range)</c>: whether the range was in the set.</summary>
    abstract Delete: highlight: Highlight * range: AbstractRange -> bool
    /// <summary><c>highlight.clear()</c>.</summary>
    abstract Clear: highlight: Highlight -> unit
    /// <summary>A snapshot of <c>highlight.values()</c>.</summary>
    abstract Ranges: highlight: Highlight -> AbstractRange array
    /// <summary><c>registry.size</c>.</summary>
    abstract Count: registry: HighlightRegistry -> int
    /// <summary><c>registry.has(name)</c>.</summary>
    abstract Contains: registry: HighlightRegistry * name: string -> bool
    /// <summary><c>registry.get(name)</c>, or <c>None</c> for an unregistered name.</summary>
    abstract TryGet: registry: HighlightRegistry * name: string -> Highlight option
    /// <summary><c>registry.set(name, highlight)</c>.</summary>
    abstract Set: registry: HighlightRegistry * name: string * highlight: Highlight -> unit
    /// <summary><c>registry.delete(name)</c>: whether the name was registered.</summary>
    abstract Remove: registry: HighlightRegistry * name: string -> bool
    /// <summary><c>registry.clear()</c>: unregisters every name; each highlight keeps its ranges.</summary>
    abstract ClearRegistry: registry: HighlightRegistry -> unit
    /// <summary>A snapshot of <c>registry.entries()</c>.</summary>
    abstract Entries: registry: HighlightRegistry -> (string * Highlight) array
    /// <summary><c>CSS.highlights.highlightsFromPoint(x, y)</c> in viewport CSS pixels.</summary>
    abstract HighlightsFromPoint: x: float * y: float -> HighlightHitTest

/// <summary>Argument validation shared by highlight providers.</summary>
module HighlightContract =
    /// <summary>Raises ArgumentNullException for a null object argument named <c>name</c>.</summary>
    let instance<'T when 'T: null> (name: string) (value: 'T) =
        if isNull value then
            nullArg name

        value

    /// <summary>Raises ArgumentNullException for a null name.</summary>
    let name (value: string) =
        if isNull value then
            nullArg "name"

        value

    /// <summary>Raises ArgumentNullException for a null array or a null range in it.</summary>
    let ranges (value: AbstractRange array) =
        if isNull value then
            nullArg "initialRanges"

        for range in value do
            instance "initialRanges" range |> ignore

        value
