module Ranvier.View.Foundation.Binding

open Ranvier
open Ranvier.View.Foundation.Scheduling

/// <summary>Creates an owned text effect whose updates are committed through the supplied queue.</summary>
/// <remarks>The first settled value is written immediately. A suspended or failed read cancels any older queued write.
/// Pending and error presentation belongs to the reader's boundary. The enclosing owner disposes the effect.</remarks>
let internal startQueued registerCleanup isLive (queue: Queue) equals read write =
    let slot = queue.Reserve ()
    let mutable first = true
    let mutable version = 0
    let mutable hasCommitted = false
    let mutable committed = Unchecked.defaultof<_>
    registerCleanup (fun () -> queue.Cancel slot)

    Api.createEffect (fun () ->
        version <- version + 1
        let current = version
        queue.Cancel slot
        Api.onCleanup (fun () -> queue.Cancel slot)
        let value = read ()
        let owner = Api.getOwner ()

        let commit () =
            if
                isLive ()
                && not owner.IsDisposed
                && version = current
            then
                Api.runWithOwner owner (fun () ->
                    Api.untrack (fun () ->
                        let changed = not hasCommitted || not (equals committed value)

                        if
                            changed
                            && isLive ()
                            && not owner.IsDisposed
                            && version = current
                        then
                            write value

                            if version = current then
                                committed <- value
                                hasCommitted <- true))

        if first then
            first <- false
            commit ()
        else
            queue.Enqueue (slot, commit))
