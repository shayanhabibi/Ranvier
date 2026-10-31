namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>Browser locale preferences and application overrides within a document's Window.</summary>
module Locales =
    [<Emit("$0.defaultView != null && typeof $0.defaultView.Intl?.getCanonicalLocales === 'function' && typeof $0.defaultView.Intl?.DateTimeFormat === 'function'")>]
    let private available (document: Document) : bool = jsNative

    [<Emit("$0.defaultView != null && $0.defaultView.document === $0")>]
    let private active (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.Intl.getCanonicalLocales($1)[0]")>]
    let private canonicalLocale (document: Document) (value: string) : string = jsNative

    [<Emit("new $0.defaultView.Intl.DateTimeFormat('en', {timeZone:$1}).resolvedOptions().timeZone")>]
    let private canonicalZone (document: Document) (value: string) : string = jsNative

    [<Emit("new $0.defaultView.Intl.DateTimeFormat().resolvedOptions().locale || ''")>]
    let private hostLocale (document: Document) : string = jsNative

    [<Emit("Array.from($0.defaultView.navigator?.languages || [])")>]
    let private hostLanguages (document: Document) : string array = jsNative

    [<Emit("$0.defaultView.navigator != null && $0.defaultView.navigator.languages != null")>]
    let private hasLanguages (document: Document) : bool = jsNative

    [<Emit("new $0.defaultView.Intl.DateTimeFormat().resolvedOptions().timeZone || ''")>]
    let private hostZone (document: Document) : string = jsNative

    /// <summary>Creates per-instance application preferences; host defaults come from the document's Intl and navigator.</summary>
    /// <param name="document">The live document whose Window supplies the preference provider.</param>
    /// <remarks>Overrides change this service. RefreshHost explicitly resamples reported browser preferences.</remarks>
    let forDocument (document: Document) =
        if isNull (box document) then
            nullArg (nameof document)

        if not (available document) then
            raise (NotSupportedException "The locale service requires a live document with Intl locale and time-zone support.")

        let guard () =
            if not (active document) then
                raise (ObjectDisposedException "document")

        let reported name value =
            if String.IsNullOrWhiteSpace value then
                LocaleAvailability.Unavailable (name + " is unavailable from the browser.")
            else
                LocaleAvailability.Available value

        let provider =
            {
                Name = "Browser Intl; host Intl defaults and navigator.languages"
                LocaleCanonicalization = "Intl.getCanonicalLocales: Unicode aliases and extension ordering follow the browser's ICU/CLDR version."
                TimeZoneCanonicalization = "Intl.DateTimeFormat: identifiers and aliases follow the browser's time-zone database."
                ReadHost =
                    fun () ->
                        {
                            Locale = reported "Locale" (hostLocale document)
                            Languages =
                                if hasLanguages document then
                                    LocaleAvailability.Available (hostLanguages document |> Array.toList)
                                else
                                    LocaleAvailability.Unavailable "navigator.languages is unavailable from the browser."
                            TimeZone = reported "Time zone" (hostZone document)
                        }
                CanonicalizeLocale =
                    fun value ->
                        try
                            canonicalLocale document value
                        with error ->
                            invalidArg "locale" error.Message
                CanonicalizeTimeZone =
                    fun value ->
                        try
                            canonicalZone document value
                        with error ->
                            invalidArg "timeZone" error.Message
            }

        LocaleService (provider, ensureAccess = guard)
