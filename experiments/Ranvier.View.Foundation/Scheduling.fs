module Ranvier.View.Foundation.Scheduling

open System
open Ranvier

#if FABLE_COMPILER
open Fable.Core
#endif

type CommitFailure =
    | CollectAndContinue
    | FailFastCancel

type Slot internal (owner: obj) =
    member internal _.Owner = owner
    member val internal Version = 0 with get, set
    member val internal Listed = false with get, set
    member val internal Action: (unit -> unit) option = None with get, set

#if FABLE_COMPILER
[<Fable.Core.Emit("$0.map($1)")>]
let private snapshot (pending: ResizeArray<Slot>) (project: Slot -> Slot * int * (unit -> unit) option) : (Slot * int * (unit -> unit) option) array =
    jsNative
#endif

type Queue(graph: Graph, failure: CommitFailure, post: (unit -> unit) -> unit) as this =
    let pending = ResizeArray<Slot>()
    let mutable disposed = false
    let mutable flushing = false
    let mutable scheduled = false
    let mutable generation = 0
    let mutable errors: exn array = [||]

    let owned (slot: Slot) =
        if not (obj.ReferenceEquals (slot.Owner, this)) then
            invalidArg "slot" "Commit slot belongs to another queue"

    member private _.Request() =
        if
            not disposed
            && not flushing
            && not scheduled
            && pending.Count > 0
        then
            scheduled <- true
            let ticket = generation

            try
                post (fun () ->
                    if not disposed && ticket = generation then
                        this.Flush ())
            with _ ->
                scheduled <- false
                reraise ()

    member _.Reserve() =
        if disposed then
            raise (InvalidOperationException "Commit queue is disposed")

        Slot (this)

    member _.Cancel(slot: Slot) =
        owned slot
        slot.Version <- slot.Version + 1
        slot.Action <- None

    member _.Enqueue(slot: Slot, action: unit -> unit) =
        owned slot

        if not disposed then
            slot.Version <- slot.Version + 1
            slot.Action <- Some action

            if not slot.Listed then
                slot.Listed <- true
                pending.Add slot

            this.Request ()

    member _.Flush() =
        if flushing then
            raise (InvalidOperationException "Recursive commit flushing is prohibited")

        if not disposed then
            generation <- generation + 1
            scheduled <- false
            flushing <- true
            let failures = ResizeArray<exn>()

            try
                graph.Run (fun () ->
                    Api.flush ()

                    let work =
#if FABLE_COMPILER
                        snapshot pending (fun slot -> slot, slot.Version, slot.Action)
#else
                        pending
                        |> Seq.map (fun slot -> slot, slot.Version, slot.Action)
                        |> Seq.toArray
#endif

                    pending.Clear ()

                    for slot, _, _ in work do
                        slot.Listed <- false
                        slot.Action <- None

                    let mutable stopped = false

                    for slot, version, action in work do
                        if stopped then
                            this.Cancel slot
                        elif not disposed && slot.Version = version then
                            match action with
                            | None -> ()
                            | Some commit ->
                                try
                                    Api.untrack commit
                                with error ->
                                    failures.Add error
                                    stopped <- failure = FailFastCancel)
            finally
                errors <- failures.ToArray ()
                flushing <- false
                this.Request ()

    member _.Errors = Array.copy errors

    member _.Dispose() =
        if not disposed then
            disposed <- true
            generation <- generation + 1
            scheduled <- false

            for slot in pending do
                slot.Listed <- false
                this.Cancel slot

            pending.Clear ()

type Scheduling =
    | Synchronous
    | Queued of Queue

type CommitMode =
    | SynchronousCommits
    | QueuedCommits of CommitFailure * ((unit -> unit) -> unit)
