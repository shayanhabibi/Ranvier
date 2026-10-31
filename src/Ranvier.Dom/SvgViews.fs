namespace Ranvier.Dom

/// <summary>An actual SVG view value or the provider's reason that no typed value can be returned.</summary>
[<RequireQualifiedAccess>]
type SvgViewAvailability<'T> =
    | Available of 'T
    /// <summary>The provider does not expose the requested DOM member.</summary>
    | NotExposed
    /// <summary>The exposed member reports that its operation is unsupported.</summary>
    | Unsupported of reason: string
    /// <summary>The exposed member returned no value with the required DOM type.</summary>
    | Unknown of reason: string

/// <summary>The observed current-view properties of one SVG root.</summary>
/// <remarks>An available view remains the provider's live DOM object; requested attributes do not establish applied view state.</remarks>
type SvgViewState =
    {
        CurrentView: SvgViewAvailability<SVGViewSpec>
        UseCurrentView: SvgViewAvailability<bool>
    }
