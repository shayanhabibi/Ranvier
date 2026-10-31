namespace Ranvier.Dom

open System

/// <summary>The ToggleEventInit fields; <c>Source</c> is null for none.</summary>
/// <remarks>The engine fires <c>"open"</c> and <c>"closed"</c>; a constructed event carries any string.</remarks>
type ToggleFields =
    {
        OldState: string
        NewState: string
        Source: Element
    }

    /// <summary>Empty states and no source.</summary>
    static member Default =
        {
            OldState = ""
            NewState = ""
            Source = null
        }

/// <summary>The CommandEventInit fields; <c>Source</c> is null for none.</summary>
type CommandFields =
    {
        Command: string
        Source: Element
    }

    /// <summary>An empty command and no source.</summary>
    static member Default = { Command = ""; Source = null }

/// <summary>Constructs untrusted ToggleEvents and CommandEvents in one document.</summary>
/// <remarks>
/// Dispatch them with <c>EventTarget.dispatchEvent</c>. A dispatched CommandEvent runs no command, and a dispatched
/// ToggleEvent opens and closes nothing. Dialogs, popovers and invokers are members of the element interfaces.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ToggleEventService =
    inherit IDisposable
    /// <summary>An untrusted ToggleEvent of <c>eventType</c>.</summary>
    abstract CreateToggleEvent: eventType: string * options: EventOptions * fields: ToggleFields -> ToggleEvent
    /// <summary>An untrusted CommandEvent of <c>eventType</c>.</summary>
    abstract CreateCommandEvent: eventType: string * options: EventOptions * fields: CommandFields -> CommandEvent

/// <summary>Argument validation shared by toggle event providers.</summary>
module ToggleEventContract =
    /// <summary>Raises ArgumentNullException for a null event type.</summary>
    let eventType (value: string) =
        if isNull value then
            nullArg "eventType"

        value

    /// <summary>Raises ArgumentNullException for a null state or command string.</summary>
    let text (name: string) (value: string) =
        if isNull value then
            nullArg name

        value
