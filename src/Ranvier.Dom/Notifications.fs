namespace Ranvier.Dom

open System
open System.Threading.Tasks

/// <summary>A NotificationPermission, numbered as the native <c>RV_NOTIFICATION_*</c> permissions.</summary>
type NotificationPermission =
    | Default = 0
    | Denied = 1
    | Granted = 2

/// <summary>A NotificationDirection, numbered as the native <c>RV_NOTIFICATION_DIR_*</c> values.</summary>
type NotificationDirection =
    | Auto = 0
    | Ltr = 1
    | Rtl = 2

/// <summary>The NotificationOptions members every provider supports; actions, image, badge and requireInteraction are unsupported.</summary>
type NotificationOptions =
    {
        Body: string
        Tag: string
        Lang: string
        /// <summary>A URL resolved against the document.</summary>
        Icon: string
        Dir: NotificationDirection
        /// <summary>None leaves silent unset.</summary>
        Silent: bool option
    }

    static member Default =
        {
            Body = ""
            Tag = ""
            Lang = ""
            Icon = ""
            Dir = NotificationDirection.Auto
            Silent = None
        }

/// <summary>A shown notification: its fields, its events and Close.</summary>
/// <remarks>
/// Events arrive on later turns. Without permission a notification fires error; a presenter reports show, click and
/// close. The unset silent flag reads None on native hosts and false in Chromium.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type NotificationHandle =
    abstract Title: string
    abstract Body: string
    abstract Tag: string
    abstract Lang: string
    /// <summary>The resolved icon URL, or empty without one.</summary>
    abstract Icon: string
    abstract Dir: NotificationDirection
    abstract Silent: bool option
    /// <summary>Notification.close(); a closed notification stays closed.</summary>
    abstract Close: unit -> unit
    /// <summary>Runs <c>callback</c> at each show event until the result is disposed.</summary>
    abstract OnShow: callback: (unit -> unit) -> IDisposable
    /// <summary>Runs <c>callback</c> at each click event until the result is disposed.</summary>
    abstract OnClick: callback: (unit -> unit) -> IDisposable
    /// <summary>Runs <c>callback</c> at each close event until the result is disposed.</summary>
    abstract OnClose: callback: (unit -> unit) -> IDisposable
    /// <summary>Runs <c>callback</c> at each error event until the result is disposed.</summary>
    abstract OnError: callback: (unit -> unit) -> IDisposable

/// <summary>Notifications of one document.</summary>
/// <remarks>
/// A browser provider follows the browser's permission model; a native provider follows the application's permission
/// and presenter. Persistent notifications and actions are unsupported on every provider.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type NotificationService =
    inherit IDisposable
    /// <summary>The current permission.</summary>
    abstract Permission: NotificationPermission
    /// <summary>Notification.requestPermission(); the task completes after the call returns.</summary>
    abstract RequestPermission: unit -> Task<NotificationPermission>
    /// <summary>new Notification(title, options); when <c>signal</c> aborts, the notification closes.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>title</c> or a string option is null.</exception>
    abstract Show: title: string * options: NotificationOptions * signal: AbortSignal option -> NotificationHandle

/// <summary>The application badge of a document's application.</summary>
/// <remarks>A native provider hands the badge to the application's handler; without one it is unavailable.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type AppBadgeService =
    inherit IDisposable
    /// <summary>navigator.setAppBadge: a count, or None for a flag badge; 0 clears the badge.</summary>
    abstract Set: count: uint64 option -> Task<unit>
    /// <summary>navigator.clearAppBadge.</summary>
    abstract Clear: unit -> Task<unit>

/// <summary>Argument checks shared by notification providers.</summary>
module NotificationContract =
    /// <summary>Raises ArgumentNullException for a null title or string option.</summary>
    let check (title: string) (options: NotificationOptions) =
        if isNull title then
            nullArg "title"

        if isNull (box options) then
            nullArg "options"

        for name, value in
            [|
                "Body", options.Body
                "Tag", options.Tag
                "Lang", options.Lang
                "Icon", options.Icon
            |] do
            if isNull value then
                nullArg name

    /// <summary>The NotificationDirection name of <c>dir</c>.</summary>
    let dirName (dir: NotificationDirection) =
        match dir with
        | NotificationDirection.Ltr -> "ltr"
        | NotificationDirection.Rtl -> "rtl"
        | _ -> "auto"

    /// <summary>The signal of a show: <c>Some null</c> reads as None.</summary>
    let signal (value: AbortSignal option) =
        match value with
        | Some signal when not (isNull (box signal)) -> Some signal
        | _ -> None

    /// <summary>Closes <c>handle</c> when <c>signal</c> aborts, or at once when it has aborted.</summary>
    let bind (signal: AbortSignal option) (handle: NotificationHandle) =
        match signal with
        | Some signal when signal.Aborted -> handle.Close ()
        | Some signal ->
            signal.OnAbort (fun _ -> handle.Close ())
            |> ignore
        | None -> ()
