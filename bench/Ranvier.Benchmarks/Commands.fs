module Ranvier.Benchmarks.Commands

open System
open System.Threading
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open Ranvier
open Ranvier.CSharp

/// <summary>Where a command under measurement lives.</summary>
type CommandHost =
    /// <summary><c>Reactive.Command</c>, with an effect of its own.</summary>
    | Standalone = 0
    /// <summary><c>ReactiveBindings.Command</c>, notified by the bindings' effect.</summary>
    | Hosted = 1

let private completedBody =
    Func<obj, CancellationToken, Task>(fun _ _ -> Task.CompletedTask)

/// <summary>Construction and disposal of a <c>ReactiveCommand</c> with a predicate over one signal.</summary>
[<MemoryDiagnoser; BenchmarkCategory "Commands">]
type CommandLifecycleBenchmarks() =
    let graph = new Graph ()
    let mutable activation: IDisposable = null
    let mutable bindings: ReactiveBindings = Unchecked.defaultof<_>
    let allowed = Signal (graph, true)
    let canExecute = Func<bool>(fun () -> allowed.Value)

    [<Params(CommandHost.Standalone, CommandHost.Hosted)>]
    member val Host = CommandHost.Standalone with get, set

    [<GlobalSetup>]
    member _.Setup() =
        activation <- graph.Activate ()
        bindings <- new ReactiveBindings (null, graph)

    [<GlobalCleanup>]
    member _.Cleanup() =
        bindings.Dispose ()
        activation.Dispose ()

    [<Benchmark>]
    member this.CreateAndDispose() =
        let command =
            if this.Host = CommandHost.Standalone then
                Reactive.Command (completedBody, canExecute)
            else
                bindings.Command (completedBody, canExecute)

        command.Dispose ()

/// <summary>One execution of an armed command whose body returns <c>Task.CompletedTask</c>.</summary>
[<MemoryDiagnoser; BenchmarkCategory "Commands">]
type CommandExecuteBenchmarks() =
    let graph = new Graph ()
    let mutable activation: IDisposable = null
    let mutable bindings: ReactiveBindings = Unchecked.defaultof<_>
    let mutable command: ReactiveCommand = Unchecked.defaultof<_>

    [<Params(CommandHost.Standalone, CommandHost.Hosted)>]
    member val Host = CommandHost.Standalone with get, set

    [<GlobalSetup>]
    member this.Setup() =
        activation <- graph.Activate ()
        bindings <- new ReactiveBindings (null, graph)

        command <-
            if this.Host = CommandHost.Standalone then
                Reactive.Command completedBody
            else
                bindings.Command completedBody

        command.CanExecute null |> ignore

    [<GlobalCleanup>]
    member _.Cleanup() =
        command.Dispose ()
        bindings.Dispose ()
        activation.Dispose ()

    [<Benchmark>]
    member _.ExecuteCompleted() =
        command.ExecuteAsync ()

/// <summary>
/// One execution of a hosted command in bindings holding <c>Slots</c> computed properties. Each notify pass refreshes
/// every slot.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Commands">]
type CommandSlotsBenchmarks() =
    let graph = new Graph ()
    let mutable bindings: ReactiveBindings = Unchecked.defaultof<_>
    let mutable command: ReactiveCommand = Unchecked.defaultof<_>

    [<Params(1, 10, 100)>]
    member val Slots = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        bindings <- new ReactiveBindings (null, graph)
        let source = Signal (graph, 1)

        for i in 1 .. this.Slots do
            bindings.Computed ($"P%d{i}", Func<int>(fun () -> source.Value + i))
            |> ignore

        command <- bindings.Command completedBody
        command.CanExecute null |> ignore

    [<GlobalCleanup>]
    member _.Cleanup() =
        bindings.Dispose ()

    [<Benchmark>]
    member _.ExecuteWithSlots() =
        command.ExecuteAsync ()
