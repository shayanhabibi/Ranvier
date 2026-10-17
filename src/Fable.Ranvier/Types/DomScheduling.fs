namespace Fable.Ranvier

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
        | Some context when context.Options.Scheduling = Microtask -> Microtask.bind context.Queue (fun body -> run (Some context) body) read write
        | _ -> createEffectOn read write
