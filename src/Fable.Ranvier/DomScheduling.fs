namespace Fable.Ranvier

open System
open System.Collections.Generic
open Fable.Core
open global.Ranvier

type DomScheduling =
    | Synchronous
    | Microtask

type DomOptions =
    {
        Scheduling: DomScheduling
        BatchEvents: bool
    }

    static member Default =
        {
            Scheduling = Synchronous
            BatchEvents = true
        }

type internal DomQueue(graph: Graph) as this =
    let pending = Dictionary<int, unit -> unit>()
    let errors = ResizeArray<exn>()
    let mutable nextId = 0
    let mutable generation = 0
    let mutable scheduled = false
    let mutable flushing = false
    let mutable disposed = false

    [<Emit("queueMicrotask($0)")>]
    static member private Schedule(callback: unit -> unit) : unit = jsNative

    member private _.Request() =
        if
            not disposed
            && not scheduled
            && not flushing
            && pending.Count > 0
        then
            scheduled <- true
            let ticket = generation

            DomQueue.Schedule (fun () ->
                if not disposed && ticket = generation then
                    this.Flush ())

    member _.Reserve() =
        nextId <- nextId + 1
        nextId

    member _.Cancel(id) =
        pending.Remove id |> ignore

    member _.Enqueue(id, commit) =
        if not disposed then
            pending[id] <- commit
            this.Request ()

    member _.Flush() =
        if not disposed && not flushing then
            flushing <- true
            generation <- generation + 1
            scheduled <- false

            try
                graph.Run (fun () ->
                    flush ()
                    let work = pending.Values |> Seq.toArray
                    pending.Clear ()

                    if work.Length > 0 then
                        errors.Clear ()

                    for commit in work do
                        if not disposed then
                            try
                                commit ()
                            with error ->
                                errors.Add error)
            finally
                flushing <- false
                this.Request ()

    member _.Errors = errors.ToArray ()

    member _.Dispose() =
        if not disposed then
            disposed <- true
            generation <- generation + 1
            scheduled <- false
            pending.Clear ()

type internal DomContext =
    {
        Graph: Graph
        Options: DomOptions
        Queue: DomQueue
    }

module internal DomContext =
    let mutable current: DomContext option = None

    let run context body =
        let previous = current
        current <- context

        try
            body ()
        finally
            current <- previous

    let bind (read: unit -> 'T) (write: 'T -> unit) =
        match current with
        | Some context when context.Options.Scheduling = Microtask ->
            let bindingOwner = getOwner ()
            let id = context.Queue.Reserve ()
            onCleanup (fun () -> context.Queue.Cancel id)
            let value = createMemo (fun _ -> read ())
            let mutable firstRun = true

            createEffectOn (fun () -> value.TryValue) (fun reading ->
                let initial = firstRun
                firstRun <- false

                match reading with
                | Ready result ->
                    if initial then
                        write result
                    else
                        let actionOwner = getOwner ()
                        onCleanup (fun () -> context.Queue.Cancel id)

                        context.Queue.Enqueue (
                            id,
                            fun () ->
                                if
                                    not bindingOwner.IsDisposed
                                    && not actionOwner.IsDisposed
                                then
                                    run (Some context) (fun () ->
                                        runWithOwner actionOwner (fun () ->
                                            untrack (fun () ->
                                                match value.TryValue with
                                                | Ready latest when not actionOwner.IsDisposed -> write latest
                                                | _ -> ())))
                        )
                | Pending
                | Failed _ -> context.Queue.Cancel id)
        | _ -> createEffectOn read write
