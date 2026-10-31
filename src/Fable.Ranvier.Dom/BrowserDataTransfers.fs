namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>DataTransfer construction backed by the active document realm.</summary>
[<RequireQualifiedAccess>]
module BrowserDataTransfers =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.DataTransfer === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("new $0.defaultView.DataTransfer()")>]
    let private create (document: Document) : DataTransfer = jsNative

    /// <summary>The DataTransfer service of <c>document</c>, which must have an active Window.</summary>
    let forDocument (document: Document) : DataTransferService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "DataTransfer requires an active document Window")

        let disposed = ref false

        { new DataTransferService with
            member _.Create() =
                if disposed.Value then
                    raise (ObjectDisposedException (nameof DataTransferService))

                if not (current document) then
                    raise (InvalidOperationException "The DataTransfer document is no longer active")

                create document

            member _.Dispose() =
                disposed.Value <- true
        }
