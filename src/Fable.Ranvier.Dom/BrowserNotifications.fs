namespace Ranvier.Dom

open System
open System.Threading.Tasks
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Notifications and the application badge of the active document realm.</summary>
module BrowserNotifications =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.Notification === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.navigator.setAppBadge === 'function'")>]
    let private hasBadge (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.Notification.permission")>]
    let private permission (document: Document) : string = jsNative

    [<Emit("$0.defaultView.Notification.requestPermission()")>]
    let private requestPermission (document: Document) : JS.Promise<string> = jsNative

    [<Emit("new $0.defaultView.Notification($1, $2)")>]
    let private create (document: Document) (title: string) (options: obj) : obj = jsNative

    [<Emit("$0[$1]")>]
    let private field (value: obj) (name: string) : obj = jsNative

    [<Emit("$0.close()")>]
    let private close (value: obj) : unit = jsNative

    [<Emit("(() => { const f = () => $2(); $0.addEventListener($1, f); return () => $0.removeEventListener($1, f); })()")>]
    let private listen (value: obj) (kind: string) (callback: unit -> unit) : (unit -> unit) = jsNative

    [<Emit("($1 == null ? $0.defaultView.navigator.setAppBadge() : $0.defaultView.navigator.setAppBadge($1)).then(() => undefined)")>]
    let private setBadge (document: Document) (count: obj) : JS.Promise<unit> = jsNative

    [<Emit("$0.defaultView.navigator.clearAppBadge().then(() => undefined)")>]
    let private clearBadge (document: Document) : JS.Promise<unit> = jsNative

    [<Emit("$0.then($1)")>]
    let private map (value: JS.Promise<'T>) (next: 'T -> 'U) : Task<'U> = jsNative

    let private permissionOf (name: string) =
        match name with
        | "granted" -> NotificationPermission.Granted
        | "denied" -> NotificationPermission.Denied
        | _ -> NotificationPermission.Default

    let private dirOf (name: string) =
        match name with
        | "ltr" -> NotificationDirection.Ltr
        | "rtl" -> NotificationDirection.Rtl
        | _ -> NotificationDirection.Auto

    let private handle (value: obj) =
        let subscribe kind (callback: unit -> unit) =
            if isNull (box callback) then
                nullArg "callback"

            let stop = listen value kind callback
            let active = ref true

            { new IDisposable with
                member _.Dispose() =
                    if active.Value then
                        active.Value <- false
                        stop ()
            }

        { new NotificationHandle with
            member _.Title = unbox (field value "title")
            member _.Body = unbox (field value "body")
            member _.Tag = unbox (field value "tag")
            member _.Lang = unbox (field value "lang")
            member _.Icon = unbox (field value "icon")
            member _.Dir = dirOf (unbox (field value "dir"))

            member _.Silent =
                match field value "silent" with
                | null -> None
                | flag -> Some (unbox<bool> flag)

            member _.Close() =
                close value

            member _.OnShow callback =
                subscribe "show" callback

            member _.OnClick callback =
                subscribe "click" callback

            member _.OnClose callback =
                subscribe "close" callback

            member _.OnError callback =
                subscribe "error" callback
        }

    /// <summary>A notification service for <c>document</c>'s realm; the browser's permission model decides each show.</summary>
    /// <exception cref="T:System.NotSupportedException">The realm has no Notification constructor.</exception>
    let forDocument (document: Document) : NotificationService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Notifications require an active document Window with Notification")

        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof NotificationService))

            if not (current document) then
                raise (InvalidOperationException "The notification document is no longer active")

        { new NotificationService with
            member _.Permission =
                check ()
                permissionOf (permission document)

            member _.RequestPermission() =
                check ()
                map (requestPermission document) permissionOf

            member _.Show(title, options, signal) =
                check ()
                NotificationContract.check title options

                let init =
                    createObj
                        [
                            "body" ==> options.Body
                            "tag" ==> options.Tag
                            "lang" ==> options.Lang
                            "icon" ==> options.Icon
                            "dir" ==> NotificationContract.dirName options.Dir
                        ]

                match options.Silent with
                | Some silent -> init?silent <- silent
                | None -> ()

                let shown = handle (create document title init)
                NotificationContract.bind (NotificationContract.signal signal) shown
                shown

            member _.Dispose() =
                disposed.Value <- true
        }

    /// <summary>An application badge service for <c>document</c>'s realm.</summary>
    /// <exception cref="T:System.NotSupportedException">The realm has no navigator.setAppBadge.</exception>
    let badgeForDocument (document: Document) : AppBadgeService =
        if isNull (box document) then
            nullArg "document"

        if not (hasBadge document) then
            raise (NotSupportedException "App badges require an active document Window with navigator.setAppBadge")

        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof AppBadgeService))

        { new AppBadgeService with
            member _.Set count =
                check ()

                let value =
                    match count with
                    | Some count -> box (float count)
                    | None -> null

                unbox (setBadge document value)

            member _.Clear() =
                check ()
                unbox (clearBadge document)

            member _.Dispose() =
                disposed.Value <- true
        }
