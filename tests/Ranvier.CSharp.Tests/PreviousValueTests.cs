using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

// The previous-value members a C# caller reaches without naming ValueOption.
public class PreviousValueTests
{
    [Fact]
    public void TrySettledReportsWhetherAValueWasPublished()
    {
        using var active = new Graph().Activate();
        var step = Signal(1);
        var seen = new List<(bool, int)>();
        var total = Async<int>(async (previous, token) =>
        {
            var by = step.Value;
            var (hasValue, value) = await previous.TrySettled();
            seen.Add((hasValue, value));
            return (hasValue ? value : 100) + by;
        });

        Assert.Equal(101, total.Value);
        step.Value = 2;
        Assert.Equal(103, total.Value);
        Assert.Equal([(false, 0), (true, 101)], seen);
    }

    [Fact]
    public void TrySettledNamesItsElements()
    {
        using var active = new Graph().Activate();
        var shown = new List<string>();
        var total = Async<int>(async (previous, token) =>
        {
            var last = await previous.TrySettled();
            shown.Add($"{last.HasValue}:{last.Value}");
            return 1;
        });

        Assert.Equal(1, total.Value);
        Assert.Equal(["False:0"], shown);
    }

    [Fact]
    public async Task SettledOrWaitsForTheFlightBeforeItUnderQueue()
    {
        var graph = new Graph(GraphOptions.Default.WithFlightPolicy(FlightPolicy.Queue));
        using var active = graph.Activate();
        var trigger = Signal(0);
        var handles = new List<Previous<int>>();
        var flights = new List<TaskCompletionSource<int>>();
        var memo = Async<int>((previous, token) =>
        {
            _ = trigger.Value;
            handles.Add(previous);
            var flight = new TaskCompletionSource<int>();
            flights.Add(flight);
            return flight.Task;
        });

        _ = memo.TryValue;
        trigger.Value = 1;
        _ = memo.TryValue;

        var first = handles[0].SettledOr(-1);
        var second = handles[1].SettledOr(-1);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal(-1, first.Result);
        Assert.False(second.IsCompleted);

        flights[0].SetResult(5);
        Assert.Equal(5, await second.AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SeededMemoConstructorsTakeTheSeedBeforeCompute()
    {
        var graph = new Graph();
        var step = new Signal<int>(graph, 1);
        var total = new Memo<int>(graph, 100, previous => previous + step.Value);
        var owning = new Memo<int>(graph, 0, previous => previous + new Memo<int>(graph, _ => step.Value).Value, true);

        Assert.Equal(101, total.Value);
        Assert.Equal(1, owning.Value);
        step.Value = 5;
        Assert.Equal(106, total.Value);
        Assert.Equal(6, owning.Value);
    }

    [Fact]
    public void MemoConstructorCallFormsResolve()
    {
        var graph = new Graph();
        var flag = new Signal<bool>(graph, true);
        var ignoring = new Memo<int>(graph, _ => 1);
        var owningBool = new Memo<bool>(graph, _ => flag.Value, false);
        var seededBool = new Memo<bool>(graph, true, previous => !previous);

        Assert.Equal(1, ignoring.Value);
        Assert.True(owningBool.Value);
        Assert.False(seededBool.Value);
    }

    [Fact]
    public void TryGetCurrentReturnsTheActiveGraph()
    {
        var graph = new Graph();
        Assert.False(Graph.TryGetCurrent(out var none));
        Assert.Null(none);

        using var active = graph.Activate();
        Assert.True(Graph.TryGetCurrent(out var current));
        Assert.Same(graph, current);
    }

    [Fact]
    public void SeededBoundaryStaticsPassTheSeedThenTheLastValue()
    {
        var graph = new Graph();
        var source = new AsyncSource<string>(graph);
        var failing = new Signal<bool>(graph, true);

        var suspense = Ranvier.Boundary<string>.Suspense(graph, () => source.Value, "none", last => $"loading after {last}");
        var errors = Ranvier.Boundary<string>.Errors(graph, () => failing.Value ? throw new InvalidOperationException("x") : "ok", "none", (error, last) => $"{error.Message} after {last}");
        var catching = Ranvier.Boundary<string>.Catching(graph, () => source.Value, "none", last => $"loading after {last}", (error, last) => last);

        Assert.Equal("loading after none", suspense.Value);
        Assert.Equal("x after none", errors.Value);
        Assert.Equal("loading after none", catching.Value);

        source.Settle("ada");
        failing.Value = false;
        Assert.Equal("ada", suspense.Value);
        Assert.Equal("ok", errors.Value);
        Assert.Equal("ada", catching.Value);
    }

    [Fact]
    public void SeededFacadeBoundariesPassTheSeedThenTheLastValue()
    {
        using var active = new Graph().Activate();
        var pending = new List<AsyncSource<int>> { AsyncSource<int>() };
        var current = Signal(pending[0]);
        var failing = Signal(true);

        var suspense = Suspense(() => current.Value.Value, last => last, -1);
        var errors = ErrorBoundary(() => failing.Value ? throw new InvalidOperationException() : 3, (error, last) => last, -2);
        var both = Boundary(() => current.Value.Value, last => last, (error, last) => last, -3);

        Assert.Equal(-1, suspense.Value);
        Assert.Equal(-2, errors.Value);
        Assert.Equal(-3, both.Value);

        pending[0].Settle(7);
        failing.Value = false;
        Assert.Equal(7, suspense.Value);
        Assert.Equal(3, errors.Value);
        Assert.Equal(7, both.Value);

        pending.Add(AsyncSource<int>());
        current.Value = pending[1];
        failing.Value = true;
        Assert.Equal(7, suspense.Value);
        Assert.Equal(3, errors.Value);
        Assert.Equal(7, both.Value);
    }
}
