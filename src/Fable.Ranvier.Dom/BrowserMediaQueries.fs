namespace Ranvier.Dom

/// <summary>Media query evaluation of a browser document; its preferences come from the platform.</summary>
module BrowserMediaQueries =
    /// <summary>The provider over <c>document</c>'s Window, with no preference supply.</summary>
    let provider (document: Document) : MediaQueryProvider =
        {
            MatchMedia = (BrowserViewport.provider document).MatchMedia
            Preferences = None
        }

    /// <summary>A media query service over <c>document</c>'s Window.</summary>
    let forDocument (document: Document) : MediaQueryService =
        MediaQueries.create (provider document)
