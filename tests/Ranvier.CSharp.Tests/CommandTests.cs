using System.ComponentModel;
using System.Windows.Input;
using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers.Commands;

public interface IRepository
{
    Task SaveAsync(string draft, CancellationToken token);
    Task LoadAsync(CancellationToken token);
}

// Each call waits on a completion source the test settles.
sealed class ScriptedRepository : IRepository
{
    public readonly List<(string Kind, CancellationToken Token, TaskCompletionSource Reply)> Calls = new();

    Task Record(string kind, CancellationToken token)
    {
        var reply = new TaskCompletionSource();
        Calls.Add((kind, token, reply));
        return reply.Task;
    }

    public Task SaveAsync(string draft, CancellationToken token) => Record($"save {draft}", token);
    public Task LoadAsync(CancellationToken token) => Record("load", token);
}

// The sample of docs/content/guide/csharp.md "Commands": each command's predicate reads the other, which is
// created after it (CommunityToolkit#826).
public sealed class EditorViewModel : ReactiveObject
{
    readonly BoundSignal<string> draft;
    readonly BoundValue<bool> isValid;
    readonly BoundValue<bool> isBusy;

    public EditorViewModel(Graph graph, IRepository repo) : base(graph)
    {
        draft = Bindings.Writable(nameof(Draft), "");
        isValid = Bindings.Computed(nameof(IsValid), () => Draft.Length > 0);
        Save = Bindings.Command((_, token) => repo.SaveAsync(draft.Value, token), () => IsValid && Load is { IsRunning: false });
        Load = Bindings.Command((_, token) => repo.LoadAsync(token), () => !Save.IsRunning);
        isBusy = Bindings.Computed(nameof(IsBusy), () => Save.IsRunning || Load.IsRunning);
    }

    public string Draft { get => draft.Value; set => draft.Value = value; }
    public bool IsValid => isValid.Value;
    public bool IsBusy => isBusy.Value;
    public ReactiveCommand Save { get; }
    public ReactiveCommand Load { get; }
}

public class CommandTests
{
    static Graph NewGraph() => new(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));

    // Runs body with no SynchronizationContext, so every handler it subscribes is invoked inline.
    static void WithoutContext(Action body)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { body(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static List<string> Record(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
        return names;
    }

    static List<bool> RecordCanExecute(ICommand command)
    {
        var seen = new List<bool>();
        command.CanExecuteChanged += (_, _) => seen.Add(command.CanExecute(null));
        return seen;
    }

    [Fact]
    public void CanExecuteFollowsThePredicate() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var allowed = bindings.Writable("Allowed", false);
        var command = bindings.Command((_, _) => Task.CompletedTask, () => allowed.Value);
        var seen = RecordCanExecute(command);

        Assert.False(command.CanExecute(null));

        allowed.Value = true;
        Assert.True(command.CanExecute(null));
        Assert.True(command.CanRun);

        allowed.Value = false;
        Assert.Equal([true, false], seen);
    });

    [Fact]
    public void ADisableCommandIsDisabledBeforeItsBodyStarts() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var reply = new TaskCompletionSource();
        var canExecuteInBody = true;
        var changesBeforeBody = 0;
        var changes = 0;
        ReactiveCommand command = null!;
        command = bindings.Command((_, _) =>
        {
            canExecuteInBody = command.CanExecute(null);
            changesBeforeBody = changes;
            return reply.Task;
        });
        command.CanExecuteChanged += (_, _) => changes++;
        Assert.True(command.CanExecute(null));
        changes = 0;

        var execution = command.ExecuteAsync();

        Assert.False(canExecuteInBody);
        Assert.Equal(1, changesBeforeBody);
        Assert.True(command.IsRunning);
        Assert.False(execution.IsCompleted);

        // A second request while the first runs does nothing.
        var second = command.ExecuteAsync();
        Assert.True(second.IsCompletedSuccessfully);

        reply.SetResult();
        Assert.True(execution.IsCompletedSuccessfully);
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
    });

    [Fact]
    public void IsRunningAndErrorRaisePropertyChanged() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var replies = new List<TaskCompletionSource>();
        var command = bindings.Command((_, _) =>
        {
            var reply = new TaskCompletionSource();
            replies.Add(reply);
            return reply.Task;
        });
        var names = Record(command);
        names.Clear();

        var failed = command.ExecuteAsync();
        replies[0].SetException(new InvalidOperationException("offline"));

        Assert.Equal(["CanRun", "IsRunning", "CanRun", "IsRunning", "Error"], names);
        Assert.Equal("offline", command.Error?.Message);
        Assert.IsType<InvalidOperationException>(failed.Exception?.InnerException);

        names.Clear();
        _ = command.ExecuteAsync();
        replies[1].SetResult();

        Assert.Null(command.Error);
        Assert.Contains("Error", names);
    });

    [Fact]
    public void ICommandExecuteStoresAFailureWithoutRaisingIt() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        ICommand command = bindings.Command(_ => throw new InvalidOperationException("boom"));

        command.Execute(null);

        Assert.Equal("boom", ((ReactiveCommand)command).Error?.Message);
        Assert.True(command.CanExecute(null));
    });

    [Fact]
    public void ASynchronousCommandRaisesOnlyItsOutcome() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var count = bindings.Writable("Count", 0);
        var command = bindings.Command(p => count.Value += (int)p!);
        var seen = RecordCanExecute(command);
        var names = Record(bindings);

        command.Execute(2);
        command.Execute(3);

        // The subscription evaluated the predicate once; the executions left CanExecute unchanged.
        Assert.Equal(5, count.Value);
        Assert.Equal([true], seen);
        Assert.Equal(["Count", "Count"], names);
    });

    [Fact]
    public void CancelPreviousCancelsTheExecutionInFlight() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var calls = new List<(CancellationToken Token, TaskCompletionSource Reply)>();
        var command = bindings.Command((_, token) =>
        {
            var reply = new TaskCompletionSource();
            calls.Add((token, reply));
            return reply.Task;
        }, policy: CommandPolicy.CancelPrevious);

        var first = command.ExecuteAsync();
        Assert.True(command.CanExecute(null));
        var second = command.ExecuteAsync();

        Assert.True(calls[0].Token.IsCancellationRequested);
        Assert.False(calls[1].Token.IsCancellationRequested);

        // The superseded execution fails late; the command reports the latest one only.
        calls[1].Reply.SetException(new InvalidOperationException("latest"));
        Assert.True(command.IsRunning);
        calls[0].Reply.SetCanceled(calls[0].Token);

        Assert.True(first.IsCanceled);
        Assert.True(second.IsFaulted);
        Assert.False(command.IsRunning);
        Assert.Equal("latest", command.Error?.Message);
    });

    [Fact]
    public void CancelledExecutionsRecordNoError() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var command = bindings.Command(async (_, token) => await Task.Delay(Timeout.Infinite, token));

        var execution = command.ExecuteAsync();
        Assert.True(command.IsRunning);

        command.Cancel();
        SpinWait.SpinUntil(() => graph.PendingWork > 0 || execution.IsCompleted, TimeSpan.FromSeconds(5));
        graph.Pump();

        Assert.True(execution.IsCanceled);
        Assert.False(command.IsRunning);
        Assert.Null(command.Error);
    });

    // CommunityToolkit#826: each command is disabled while the other runs, and IsBusy covers both.
    [Fact]
    public void CommandsReadEachOtherWhateverTheirCreationOrder() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var repo = new ScriptedRepository();
        using var vm = new EditorViewModel(graph, repo);
        var names = Record(vm);
        var save = RecordCanExecute(vm.Save);
        var load = RecordCanExecute(vm.Load);

        Assert.False(vm.Save.CanExecute(null));
        Assert.True(vm.Load.CanExecute(null));

        vm.Draft = "hello";
        Assert.True(vm.Save.CanExecute(null));

        vm.Load.Execute(null);
        Assert.False(vm.Save.CanExecute(null));
        Assert.False(vm.Load.CanExecute(null));
        Assert.True(vm.IsBusy);

        repo.Calls[0].Reply.SetResult();
        Assert.True(vm.Save.CanExecute(null));
        Assert.True(vm.Load.CanExecute(null));
        Assert.False(vm.IsBusy);

        vm.Save.Execute(null);
        Assert.Equal("save hello", repo.Calls[1].Kind);
        Assert.False(vm.Load.CanExecute(null));

        Assert.Equal([true, false, true, false], save);
        Assert.Equal([true, false, true, false], load);
        Assert.Equal(["Draft", "IsValid", "IsBusy", "IsBusy", "IsBusy"], names);
    });

    [Fact]
    public void CanExecuteChangedIsPostedToTheSubscribingContext()
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            using var graph = NewGraph();
            using var bindings = new ReactiveBindings(null!, graph);
            var allowed = bindings.Writable("Allowed", false);
            var command = bindings.Command((_, _) => Task.CompletedTask, () => allowed.Value);

            var ui = new RecordingContext();
            var seen = new List<SynchronizationContext?>();
            SynchronizationContext.SetSynchronizationContext(ui);
            command.CanExecuteChanged += (_, _) => seen.Add(SynchronizationContext.Current);
            SynchronizationContext.SetSynchronizationContext(null);

            allowed.Value = true;
            Assert.Equal(1, ui.Posts);
            Assert.Empty(seen);

            ui.RunPosted();
            Assert.Equal([ui], seen);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AnExecutionRequestedOffTheGraphThreadStartsOnThePump() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var starts = new List<int>();
        var command = bindings.Command((_, _) =>
        {
            starts.Add(Environment.CurrentManagedThreadId);
            return Task.CompletedTask;
        });

        Task execution = null!;
        var caller = new Thread(() => execution = command.ExecuteAsync());
        caller.Start();
        caller.Join();
        Assert.Empty(starts);
        Assert.False(execution.IsCompleted);

        graph.Pump();
        Assert.Equal([Environment.CurrentManagedThreadId], starts);
        Assert.True(execution.IsCompletedSuccessfully);
    });

    [Fact]
    public void AStandaloneCommandNotifiesThroughItsOwnEffect() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        graph.Run(() =>
        {
            var allowed = Signal(true);
            var reply = new TaskCompletionSource();
            using var command = Command((_, _) => reply.Task, () => allowed.Value);
            var seen = RecordCanExecute(command);
            var names = Record(command);
            var busy = Memo(() => command.IsRunning);

            _ = command.ExecuteAsync();
            Assert.True(busy.Value);

            reply.SetResult();
            Assert.False(busy.Value);

            allowed.Value = false;
            Assert.Equal([true, false, true, false], seen);
            Assert.Equal(["CanRun", "IsRunning", "CanRun", "IsRunning", "CanRun"], names);
        });
    });

    [Fact]
    public void DisposeCancelsTheExecutionAndDropsHandlers() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var allowed = bindings.Writable("Allowed", true);
        var calls = new List<(CancellationToken Token, TaskCompletionSource Reply)>();
        var command = bindings.Command((_, token) =>
        {
            var reply = new TaskCompletionSource();
            calls.Add((token, reply));
            return reply.Task;
        }, () => allowed.Value);
        var seen = RecordCanExecute(command);

        _ = command.ExecuteAsync();
        seen.Clear();
        command.Dispose();

        Assert.True(calls[0].Token.IsCancellationRequested);
        allowed.Value = false;
        calls[0].Reply.SetResult();
        Assert.Empty(seen);
        Assert.True(command.ExecuteAsync().IsCompletedSuccessfully);
        Assert.Single(calls);
    });

    [Fact]
    public void AnyPendingCoversSourcesOfDifferentTypes() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        graph.Run(() =>
        {
            var quote = AsyncSource<decimal>();
            var stock = AsyncSource<int>();
            var shipping = AsyncSource<string>();
            var quoting = AnyPending(quote, stock, shipping);
            var seen = new List<bool>();
            Effect(() => seen.Add(quoting.Value));

            quote.Settle(9.5m);
            stock.Fail(new InvalidOperationException("out"));
            shipping.Settle("post");

            Assert.Equal([true, false], seen);
        });
    });
}
