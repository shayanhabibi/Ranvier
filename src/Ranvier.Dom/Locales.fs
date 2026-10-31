namespace Ranvier.Dom

open System

/// <summary>A reported preference value or the provider's reason that it is unavailable.</summary>
[<RequireQualifiedAccess>]
type LocaleAvailability<'T> =
    | Available of 'T
    | Unavailable of reason: string

/// <summary>An application preference override or inheritance from the latest host snapshot.</summary>
[<RequireQualifiedAccess>]
type LocalePreference<'T> =
    | HostDefault
    | Override of 'T

/// <summary>An immutable snapshot of locale, ordered language preferences and time-zone identifiers.</summary>
type LocaleSettings =
    {
        Locale: LocaleAvailability<string>
        Languages: LocaleAvailability<string list>
        TimeZone: LocaleAvailability<string>
    }

/// <summary>Application overrides; each HostDefault field inherits the corresponding reported host value.</summary>
type LocaleOverrides =
    {
        Locale: LocalePreference<string>
        Languages: LocalePreference<string list>
        TimeZone: LocalePreference<string>
    }

/// <summary>The host preference source and its locale and time-zone normalization policies.</summary>
/// <remarks>Canonicalizers reject unsupported identifiers. ReadHost reports unavailable fields explicitly.</remarks>
type LocaleProvider =
    {
        Name: string
        LocaleCanonicalization: string
        TimeZoneCanonicalization: string
        ReadHost: unit -> LocaleSettings
        CanonicalizeLocale: string -> string
        CanonicalizeTimeZone: string -> string
    }

/// <summary>Application-owned locale overrides over a refreshable host snapshot.</summary>
/// <param name="provider">The host source and normalization policies used for every accepted identifier.</param>
/// <remarks>
/// The optional ensureAccess guard runs on construction and every operation. Updates validate atomically.
/// Host and process settings remain owned by their provider.
/// </remarks>
type LocaleService(provider: LocaleProvider, ?ensureAccess: unit -> unit) =
    let guard = defaultArg ensureAccess ignore
    let gate = obj ()

    do
        if isNull (box provider) then
            nullArg (nameof provider)

    let scalar name canonicalize value =
        if String.IsNullOrWhiteSpace value then
            invalidArg name "Expected a non-empty locale or time-zone identifier."

        let result = canonicalize value

        if String.IsNullOrWhiteSpace result then
            invalidOp "The locale provider returned an empty identifier."

        result

    let locale = scalar "locale" provider.CanonicalizeLocale
    let zone = scalar "timeZone" provider.CanonicalizeTimeZone

    let languages values =
        if isNull (box values) then
            nullArg "languages"

        values |> List.map locale |> List.distinct

    let available normalize value =
        match value with
        | LocaleAvailability.Available item -> LocaleAvailability.Available (normalize item)
        | LocaleAvailability.Unavailable reason -> LocaleAvailability.Unavailable reason

    let normalizeHost (value: LocaleSettings) : LocaleSettings =
        if isNull (box value) then
            invalidOp "The locale provider returned a null host snapshot."

        {
            Locale = available locale value.Locale
            Languages = available languages value.Languages
            TimeZone = available zone value.TimeZone
        }

    let normalizePreference normalize value =
        match value with
        | LocalePreference.HostDefault -> LocalePreference.HostDefault
        | LocalePreference.Override item -> LocalePreference.Override (normalize item)

    let inheritAll: LocaleOverrides =
        {
            Locale = LocalePreference.HostDefault
            Languages = LocalePreference.HostDefault
            TimeZone = LocalePreference.HostDefault
        }

    do guard ()
    let mutable host = normalizeHost (provider.ReadHost ())
    let mutable overrides = inheritAll

    let choose setting fallback =
        match setting with
        | LocalePreference.HostDefault -> fallback
        | LocalePreference.Override value -> LocaleAvailability.Available value

    let read action =
        guard ()
        lock gate action

    /// <summary>The effective preferences, combining canonical application overrides with the last host snapshot.</summary>
    member _.Current: LocaleSettings =
        read (fun () ->
            {
                Locale = choose overrides.Locale host.Locale
                Languages = choose overrides.Languages host.Languages
                TimeZone = choose overrides.TimeZone host.TimeZone
            })

    /// <summary>The last canonical reported host preferences, including unavailable fields.</summary>
    member _.Host = read (fun () -> host)

    /// <summary>The canonical application overrides; HostDefault fields inherit reported values.</summary>
    member _.Overrides = read (fun () -> overrides)

    /// <summary>The preference provider's identifier.</summary>
    member _.ProviderName = read (fun () -> provider.Name)

    /// <summary>The provider's locale normalization policy, including alias behavior.</summary>
    member _.LocaleCanonicalization = read (fun () -> provider.LocaleCanonicalization)

    /// <summary>The provider's time-zone normalization policy and accepted identifier family.</summary>
    member _.TimeZoneCanonicalization = read (fun () -> provider.TimeZoneCanonicalization)

    /// <summary>Normalizes a locale through the provider; unsupported or empty identifiers raise an argument error.</summary>
    /// <param name="value">The locale identifier to normalize.</param>
    member _.CanonicalizeLocale(value) =
        read (fun () -> locale value)

    /// <summary>Normalizes a time zone through the provider; unsupported or empty identifiers raise an argument error.</summary>
    /// <param name="value">The time-zone identifier to normalize.</param>
    member _.CanonicalizeTimeZone(value) =
        read (fun () -> zone value)

    /// <summary>Atomically replaces every application override after validating and normalizing all fields.</summary>
    /// <param name="value">The replacement overrides, including HostDefault for inherited fields.</param>
    /// <remarks>Languages are deduplicated after normalization in preference order; an explicit empty list remains empty.</remarks>
    member _.SetOverrides(value: LocaleOverrides) =
        read (fun () ->
            if isNull (box value) then
                nullArg "value"

            let next: LocaleOverrides =
                {
                    Locale = normalizePreference locale value.Locale
                    Languages = normalizePreference languages value.Languages
                    TimeZone = normalizePreference zone value.TimeZone
                }

            overrides <- next)

    /// <summary>Clears every application override, exposing the latest reported host preferences.</summary>
    member _.ClearOverrides() =
        read (fun () -> overrides <- inheritAll)

    /// <summary>Atomically refreshes the reported host snapshot while preserving application overrides.</summary>
    member _.RefreshHost() =
        read (fun () ->
            let next = normalizeHost (provider.ReadHost ())
            host <- next)
