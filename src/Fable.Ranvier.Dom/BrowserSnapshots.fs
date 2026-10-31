namespace Ranvier.Dom

open System

/// <summary>Snapshots in an ordinary browser context, which exposes no DOM capture.</summary>
[<RequireQualifiedAccess>]
module BrowserSnapshots =
    /// <summary>A snapshot service for <c>document</c> that reports capture unavailable.</summary>
    let forDocument (document: Document) : SnapshotService =
        if isNull (box document) then
            nullArg "document"

        let disposed = ref false

        { new SnapshotService with
            member _.Available = false
            member _.SupportsColorSpace _ = false
            member _.SupportsEncoding _ = false

            member _.Capture(subject, options, _) =
                if disposed.Value then
                    raise (ObjectDisposedException (nameof SnapshotService))

                SnapshotContract.options subject options |> ignore
                raise (NotSupportedException "An ordinary browser context has no DOM capture")

            member _.Dispose() =
                disposed.Value <- true
        }
