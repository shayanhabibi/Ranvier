using System.Collections;
using System.ComponentModel;
using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

// A view model deriving from ReactiveObject; every getter reads the bindings.
public sealed class InvoiceViewModel : ReactiveObject
{
    readonly BoundSignal<decimal> price;
    readonly BoundSignal<int> quantity;
    readonly BoundValue<decimal> subtotal;
    readonly BoundValue<decimal> tax;
    readonly BoundValue<decimal> total;
    readonly BoundValue<bool> isLarge;

    public int SubtotalRuns;

    public InvoiceViewModel(Graph graph) : base(graph)
    {
        price = Bindings.Writable(nameof(Price), 10m);
        quantity = Bindings.Writable(nameof(Quantity), 1);
        subtotal = Bindings.Computed(nameof(Subtotal), () =>
        {
            SubtotalRuns++;
            return Price * Quantity;
        });
        tax = Bindings.Computed(nameof(Tax), () => Subtotal / 10m);
        total = Bindings.Computed(nameof(Total), () => Subtotal + Tax);
        isLarge = Bindings.Computed(nameof(IsLarge), () => Total > 100m);
    }

    public decimal Price { get => price.Value; set => price.Value = value; }
    public int Quantity { get => quantity.Value; set => quantity.Value = value; }
    public decimal Subtotal => subtotal.Value;
    public decimal Tax => tax.Value;
    public decimal Total => total.Value;
    public bool IsLarge => isLarge.Value;
}

// An existing view model with its own INotifyPropertyChanged plumbing, as a CommunityToolkit
// ObservableObject subclass has. Only two of its properties move to the bindings.
public sealed class LegacyViewModel : INotifyPropertyChanged, INotifyDataErrorInfo, IDisposable
{
    readonly ReactiveBindings bindings;
    readonly BoundSignal<string> first;
    readonly BoundValue<string> greeting;
    string title = "Untitled";

    public LegacyViewModel(Graph graph)
    {
        bindings = new ReactiveBindings(this, graph);
        bindings.PropertyChanged += (_, e) => OnPropertyChanged(e);
        bindings.ErrorsChanged += (_, e) => ErrorsChanged?.Invoke(this, e);
        first = bindings.Writable(nameof(First), "Ada");
        greeting = bindings.Computed(nameof(Greeting), () => $"{Title}: hello {First}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

    // Hand-written, and not reactive: the greeting reads it untracked.
    public string Title
    {
        get => title;
        set
        {
            title = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public string First { get => first.Value; set => first.Value = value; }
    public string Greeting => greeting.Value;

    public bool HasErrors => bindings.HasErrors;
    public IEnumerable GetErrors(string? propertyName) => bindings.GetErrors(propertyName!);
    public void Dispose() => bindings.Dispose();
}

// Records posts and runs them when asked.
sealed class RecordingContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback, object?)> posted = new();

    public int Posts { get; private set; }

    public override void Post(SendOrPostCallback d, object? state)
    {
        Posts++;
        posted.Enqueue((d, state));
    }

    public void RunPosted()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            while (posted.Count > 0)
            {
                var (d, state) = posted.Dequeue();
                d(state);
            }
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}

public class BindingTests
{
    static Graph NewGraph() => new(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));

    static List<string> Record(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
        return names;
    }

    // Runs body with no SynchronizationContext, so every handler it subscribes is invoked inline.
    static void WithoutContext(Action body)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { body(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void ADerivedPropertyRaisesWhenAnInputChanges() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var vm = new InvoiceViewModel(graph);
        var names = Record(vm);

        vm.Price = 20m;

        Assert.Equal(20m, vm.Subtotal);
        Assert.Equal(22m, vm.Total);
        Assert.Equal(["Price", "Subtotal", "Tax", "Total"], names);
    });

    [Fact]
    public void AnEqualDerivedValueRaisesNothing() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var vm = new InvoiceViewModel(graph);
        vm.Quantity = 2;
        var names = Record(vm);

        // 5 x 4 is 20, as 10 x 2 was: only the inputs change.
        graph.Batch(() =>
        {
            vm.Price = 5m;
            vm.Quantity = 4;
            return 0;
        });

        Assert.Equal(["Price", "Quantity"], names);
    });

    [Fact]
    public void AWriteOfTheSameValueRaisesNothing() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var vm = new InvoiceViewModel(graph);
        var names = Record(vm);
        var runs = vm.SubtotalRuns;

        vm.Price = 10m;

        Assert.Empty(names);
        Assert.Equal(runs, vm.SubtotalRuns);
    });

    // CommunityToolkit#857: cascading computed properties with no dependency declarations.
    [Fact]
    public void ATransitiveChainRaisesEveryAffectedPropertyOnce() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var vm = new InvoiceViewModel(graph);
        var names = Record(vm);
        var handlerSaw = new List<decimal>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Subtotal") handlerSaw.Add(vm.Total);
        };

        vm.Quantity = 10;

        Assert.Equal(["Quantity", "Subtotal", "Tax", "Total", "IsLarge"], names);
        Assert.True(vm.IsLarge);
        // Every snapshot is current before the first handler runs.
        Assert.Equal([110m], handlerSaw);
    });

    [Fact]
    public void SettingAWritablePropertyWritesItsSignal() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var seen = new List<int>();
        var count = new Signal<int>(graph, 1);
        using var bindings = new ReactiveBindings(null!, graph);
        var bound = bindings.Writable("Count", count);
        var doubled = bindings.Computed("Doubled", () => bound.Value * 2);
        var names = Record(bindings);
        graph.Run(() => Effect(() => seen.Add(count.Value)));

        bound.Value = 3;

        Assert.Equal(3, count.Peek);
        Assert.Equal(6, doubled.Value);
        Assert.Equal([1, 3], seen);
        Assert.Equal(["Count", "Doubled"], names);

        count.Value = 4;
        Assert.Equal(4, bound.Value);
        Assert.Equal(["Count", "Doubled", "Count", "Doubled"], names);
    });

    [Fact]
    public void AsyncStateMovesThroughLoadingValueErrorAndRecovery() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var active = graph.Activate();
        var price = AsyncSource<int>();
        using var bindings = new ReactiveBindings();
        var total = bindings.Computed("Total", () => price.Value * 2, "IsTotalLoading");
        var names = Record(bindings);
        var errorsChanged = new List<string>();
        bindings.ErrorsChanged += (_, e) => errorsChanged.Add(e.PropertyName!);

        Assert.True(total.IsLoading);
        Assert.True(bindings.IsLoading);
        Assert.Equal(0, total.Value);

        price.Settle(5);
        Assert.Equal(10, total.Value);
        Assert.False(bindings.IsLoading);
        Assert.Equal(["Total", "IsTotalLoading", "IsLoading"], names);

        names.Clear();
        price.Fail(new InvalidOperationException("feed offline"));
        Assert.True(bindings.HasErrors);
        Assert.Equal(["feed offline"], bindings.GetErrors("Total").Cast<string>());
        Assert.Equal(["feed offline"], bindings.GetErrors(null!).Cast<string>());
        Assert.Empty(bindings.GetErrors("Other").Cast<string>());
        Assert.Equal("feed offline", total.Error!.Message);
        Assert.Equal(10, total.Value);
        Assert.Equal(["HasErrors"], names);
        Assert.Equal(["Total"], errorsChanged);

        names.Clear();
        price.Settle(6);
        Assert.False(bindings.HasErrors);
        Assert.Empty(bindings.GetErrors("Total").Cast<string>());
        Assert.Null(total.Error);
        Assert.Equal(12, total.Value);
        Assert.Equal(["Total", "HasErrors"], names);
        Assert.Equal(["Total", "Total"], errorsChanged);
    });

    [Fact]
    public void AReloadKeepsTheLastValueWhileLoading() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var active = graph.Activate();
        var id = Signal(1);
        var requests = new List<TaskCompletionSource<string>>();
        var user = Async(token =>
        {
            _ = id.Value;
            var request = new TaskCompletionSource<string>();
            requests.Add(request);
            return request.Task;
        });
        using var bindings = new ReactiveBindings();
        var name = bindings.Computed("Name", () => user.Value);
        requests[^1].SetResult("Ada");
        Assert.Equal("Ada", name.Value);
        var names = Record(bindings);

        id.Value = 2;
        Assert.True(name.IsLoading);
        Assert.True(bindings.IsLoading);
        Assert.Equal("Ada", name.Value);
        Assert.Equal(["IsLoading"], names);

        requests[^1].SetResult("Grace");
        Assert.Equal("Grace", name.Value);
        Assert.Equal(["IsLoading", "Name", "IsLoading"], names);
    });

    [Fact]
    public void AHandlerSubscribedUnderAContextIsPostedToIt()
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            using var graph = NewGraph();
            var count = new Signal<int>(graph, 1);
            using var bindings = new ReactiveBindings(null!, graph);
            bindings.Writable("Count", count);

            var ui = new RecordingContext();
            var onUi = new List<SynchronizationContext?>();
            var inline = new List<SynchronizationContext?>();
            SynchronizationContext.SetSynchronizationContext(ui);
            bindings.PropertyChanged += (_, _) => onUi.Add(SynchronizationContext.Current);
            SynchronizationContext.SetSynchronizationContext(null);
            bindings.PropertyChanged += (_, _) => inline.Add(SynchronizationContext.Current);

            count.Value = 2;

            Assert.Equal(1, ui.Posts);
            Assert.Empty(onUi);
            Assert.Equal([null], inline);

            ui.RunPosted();
            Assert.Equal([ui], onUi);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AHandlerOnTheRaisingContextRunsInline()
    {
        var previous = SynchronizationContext.Current;
        var ui = new RecordingContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(ui);
            using var graph = NewGraph();
            var count = new Signal<int>(graph, 1);
            using var bindings = new ReactiveBindings(null!, graph);
            bindings.Writable("Count", count);
            var seen = 0;
            bindings.PropertyChanged += (_, _) => seen++;

            count.Value = 2;

            Assert.Equal(1, seen);
            Assert.Equal(0, ui.Posts);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void APostedHandlerIsSkippedAfterDispose()
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            using var graph = NewGraph();
            var count = new Signal<int>(graph, 1);
            var bindings = new ReactiveBindings(null!, graph);
            bindings.Writable("Count", count);
            var ui = new RecordingContext();
            var seen = 0;
            SynchronizationContext.SetSynchronizationContext(ui);
            bindings.PropertyChanged += (_, _) => seen++;
            SynchronizationContext.SetSynchronizationContext(null);

            count.Value = 2;
            bindings.Dispose();
            ui.RunPosted();

            Assert.Equal(1, ui.Posts);
            Assert.Equal(0, seen);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AfterDisposeInputChangesRaiseNothing() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var vm = new InvoiceViewModel(graph);
        var names = Record(vm);
        var runs = vm.SubtotalRuns;

        vm.Dispose();
        vm.Price = 30m;

        Assert.True(vm.Bindings.Owner.IsDisposed);
        Assert.Empty(names);
        Assert.Equal(runs, vm.SubtotalRuns);
        Assert.Throws<ObjectDisposedException>(() => vm.Bindings.Computed("Late", () => 1));
    });

    [Fact]
    public void DisposingTheEnclosingScopeDisposesTheBindings() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var active = graph.Activate();
        var count = Signal(1);
        var names = new List<string>();
        var owner = Root(owner =>
        {
            var bindings = new ReactiveBindings();
            bindings.Writable("Count", count);
            bindings.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
            return owner;
        });

        count.Value = 2;
        owner.Dispose();
        count.Value = 3;

        Assert.Equal(["Count"], names);
    });

    [Fact]
    public void NodesCreatedThroughRunAreDisposedWithTheBindings() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var count = new Signal<int>(graph, 1);
        var runs = 0;
        var bindings = new ReactiveBindings(null!, graph);
        bindings.Run(() => Effect(() =>
        {
            _ = count.Value;
            runs++;
        }));
        Assert.Equal(1, runs);

        bindings.Dispose();
        count.Value = 2;

        Assert.Equal(1, runs);
    });

    [Fact]
    public void AnExistingViewModelForwardsThroughItsOwnEvent() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var vm = new LegacyViewModel(graph);
        var names = Record(vm);
        var senders = new List<object?>();
        vm.PropertyChanged += (sender, _) => senders.Add(sender);

        vm.First = "Grace";
        Assert.Equal("Untitled: hello Grace", vm.Greeting);

        vm.Title = "Dr";
        Assert.Equal("Untitled: hello Grace", vm.Greeting);

        Assert.Equal(["First", "Greeting", "Title"], names);
        Assert.All(senders, s => Assert.Same(vm, s));
        Assert.False(vm.HasErrors);
    });

    [Fact]
    public void BindingsAreTheSenderOfTheirOwnViewModel() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        var vm = new InvoiceViewModel(graph);
        object? sender = null;
        vm.PropertyChanged += (s, _) => sender = s;

        vm.Price = 11m;

        Assert.Same(vm, sender);
    });

    [Fact]
    public void ADuplicateNameIsRejected() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        bindings.Writable("A", 1);

        Assert.Throws<ArgumentException>(() => bindings.Computed("A", () => 2));
    });

    [Fact]
    public void AThrowingHandlerLeavesTheOthersNotified() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var count = bindings.Writable("Count", 1);
        var doubled = bindings.Computed("Doubled", () => count.Value * 2);
        bindings.PropertyChanged += (_, _) => throw new InvalidOperationException("handler");
        var names = Record(bindings);

        count.Value = 2;
        count.Value = 3;

        Assert.Equal(["Count", "Doubled", "Count", "Doubled"], names);
        Assert.Equal(6, doubled.Value);
    });

    [Fact]
    public void AnOffThreadSetIsDispatchedToTheGraph() => WithoutContext(() =>
    {
        using var graph = NewGraph();
        using var bindings = new ReactiveBindings(null!, graph);
        var count = bindings.Writable("Count", 1);
        var names = Record(bindings);

        var writer = new Thread(() => count.Value = 5);
        writer.Start();
        writer.Join();

        Assert.Equal(1, count.Value);
        Assert.Empty(names);
        graph.Pump();
        Assert.Equal(5, count.Value);
        Assert.Equal(["Count"], names);
    });

    [Fact]
    public void ASerialisedSetOutsideTheGraphAppliesAtTheNextDrain() => WithoutContext(() =>
    {
        using var graph = new Graph(GraphOptions.Default
            .WithThreadAffinity(ThreadAffinity.Serialised)
            .WithDispatcher(new ManualDispatcher()));
        using var bindings = new ReactiveBindings(null!, graph);
        var count = bindings.Writable("Count", 1);
        var names = Record(bindings);

        count.Value = 5;

        Assert.Equal(1, count.Value);
        Assert.Equal(1, graph.PendingWork);
        graph.Pump();
        Assert.Equal(5, count.Value);
        Assert.Equal(["Count"], names);
    });
}
