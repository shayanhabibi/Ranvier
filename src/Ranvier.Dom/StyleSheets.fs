namespace Ranvier.Dom

/// <summary>The media text or existing media list copied into a constructed stylesheet.</summary>
type StyleSheetMedia =
    | MediaText of string
    | MediaList of MediaList

/// <summary>Options for a sheet constructed in the service's document.</summary>
type StyleSheetOptions =
    {
        /// <summary>The base URL for relative resources; None or Some null uses the document's base URL.</summary>
        BaseUrl: string option
        /// <summary>The initial media query list.</summary>
        Media: StyleSheetMedia
        /// <summary>Whether the sheet initially stops contributing styles.</summary>
        Disabled: bool
    }

    /// <summary>Uses the document's base URL, an empty media query list and enabled styles.</summary>
    static member Default =
        {
            BaseUrl = None
            Media = MediaText ""
            Disabled = false
        }

/// <summary>A stylesheet engine error with its DOM exception name and message.</summary>
exception StyleSheetException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>Constructs and adopts sheets in one document; callers dispose the service with their owning view.</summary>
type StyleSheetService =
    inherit System.IDisposable

    /// <summary>Whether construction honours an explicit base URL; unsupported options are rejected.</summary>
    abstract SupportsBaseUrl: bool

    /// <summary>Constructs an unadopted sheet using the service's document and the supplied options.</summary>
    abstract Create: options: StyleSheetOptions -> CSSStyleSheet

    /// <summary>Replaces rules when started on the owning loop and completes asynchronously with the same sheet.</summary>
    /// <remarks>Cancellation after parsing does not undo the rules. Service or native host disposal cancels pending completion.</remarks>
    abstract ReplaceAsync: sheet: CSSStyleSheet * text: string -> Async<CSSStyleSheet>

    /// <summary>Copies the ordered adoption list of a Document or ShadowRoot; array edits do not change adoption.</summary>
    abstract GetAdopted: scope: Node -> CSSStyleSheet array

    /// <summary>Atomically replaces a Document or ShadowRoot's adoption list, preserving order and duplicates.</summary>
    /// <remarks>Every sheet must have been constructed in the scope's document.</remarks>
    abstract SetAdopted: scope: Node * sheets: CSSStyleSheet array -> unit
