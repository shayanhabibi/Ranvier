# Ranvier.Dom

DOM contracts shared by Ranvier's browser and native view backends. The source is generated from WebKit's
IDL and the curated native UI scope in `ranvier-org/Ranvier.WebCore`, rather than TypeScript's browser-wide
DOM declarations.

Reference `Ranvier.Dom` for .NET or `Fable.Ranvier.Dom` for a browser build. Both projects compile the same
generated contracts and shared utility sources. Browser-only conversion code handles typed unions at the JavaScript boundary. Components use
the same public member names on both targets.

`StyleSheetList.namedItem` uses the associated document's first matching ID and returns the sheet of
an HTML style element, or null. Reserved names such as `length`, `item`, `0` and `__proto__` remain
document IDs. Shadow-root lists return null. Obtain a document's list through the typed `styleSheets`
getter so its association is retained even when the browser list is empty. The browser facade adds
non-enumerable Symbol metadata containing a weak document reference; this metadata is visible through
`Reflect.ownKeys`. A list supplied directly by JavaScript needs that typed getter before named lookup.

`DOMStringMap.namedItem` reads dataset own keys and returns an empty string for a missing key on both
targets. Prototype member names remain ordinary dataset keys.

The shared `Ranvier.Dom.CSS.escape` function serializes complete UTF-16 text for CSS identifiers on all targets, including embedded null characters. It uses the [CSSOM 2021 identifier serialization profile](https://www.w3.org/TR/2021/WD-cssom-1-20210826/#serialize-an-identifier), matching pinned WebCore and the tested Chromium build. A null string reference becomes `"null"`.

Members outside the generated scope remain available in browser code through an explicit cast to the
corresponding Xantham interface. The generated native report records omitted members and their reasons.

`CssQueryService` provides both CSS support-query overloads through the document's engine parser.
Browser code uses `Ranvier.Dom.CssQueries.forDocument document`; the native View backend supplies its
host provider. Invalid CSS returns false, while unavailable providers and invalid host lifetimes raise
errors. Null strings convert to `"null"`, and embedded NUL remains part of the query input.

`ComputedStyleService` returns live, read-only CSS declarations for an element or pseudo-element.
Browser code uses `Ranvier.Dom.ComputedStyles.forDocument document`; the native View backend supplies
its host provider. Elements belong to the provider's document. Unresolved pseudo-element selectors
return empty declarations; later style changes update successful queries.

`Document.createNodeIterator` and `Document.createTreeWalker` take a `NodeFilter`, a function from `Node` to
`NodeFilterResult` (`Accept` = 1, `Reject` = 2, `Skip` = 3). In a browser build the function is the DOM filter
callback; the native View backend wraps it in a host filter. The `filter` getters return the same function, or
`None`. An exception raised by the function stops the traversal and propagates from the traversal method.
`whatToShow` defaults to every node type (`0xFFFFFFFF`).

`MathMLMathElement` is the interface of the MathML `math` element on the native backend. Chromium exposes the
element as `MathMLElement`, which the interface extends.

`Element.getClientRects()` and `Range.getClientRects()` return the engine's fragment rectangle snapshot.
`DOMRectList.length` counts its entries, and `item` returns a null-capable `DOMRect` for each index.
Coordinates retain double precision; an out-of-range item returns null.
