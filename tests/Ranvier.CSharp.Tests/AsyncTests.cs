using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

// The headline sample of guide/csharp.md "Async values" and the Ranvier.CSharp README, as published.
public interface ISearchService
{
    Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken token);
}

public sealed class SearchViewModel
{
    public SearchViewModel(ISearchService service)
    {
        Query = Signal("");
        Results = Async(token => service.SearchAsync(Query.Value, token));
        Summary = Boundary(
            () => Results.Value.Count == 0 ? "No matches" : string.Join(", ", Results.Value),
            () => "Searching…",
            error => $"Search failed: {error.Message}");
    }

    public Signal<string> Query { get; }
    public AsyncMemo<IReadOnlyList<string>> Results { get; }
    public Boundary<string> Summary { get; }
    public bool IsSearching => Summary.IsWaiting;
    public Exception? Error => Summary.Caught;
}

// A search service with replies completed by hand, on the graph thread.
sealed class ScriptedSearch : ISearchService
{
    public List<(string Query, CancellationToken Token, TaskCompletionSource<IReadOnlyList<string>> Reply)> Calls { get; } = new();

    public Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken token)
    {
        var reply = new TaskCompletionSource<IReadOnlyList<string>>();
        Calls.Add((query, token, reply));
        return reply.Task;
    }
}

public class AsyncTests
{
    [Fact]
    public void SearchViewModelMovesThroughLoadingValueErrorAndRecovery()
    {
        var service = new ScriptedSearch();
        var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
        var shown = new List<string>();

        var search = graph.Run(() => new SearchViewModel(service));
        graph.Run(() => Effect(() => shown.Add(search.Summary.Value)));

        Assert.Equal(["Searching…"], shown);
        Assert.True(search.IsSearching);

        service.Calls[0].Reply.SetResult([]);
        Assert.Equal("No matches", shown[^1]);
        Assert.False(search.IsSearching);

        search.Query.Value = "ada";
        Assert.Equal("Searching…", shown[^1]);
        service.Calls[1].Reply.SetException(new HttpRequestException("offline"));
        Assert.Equal("Search failed: offline", shown[^1]);
        Assert.IsType<HttpRequestException>(search.Error);

        search.Query.Value = "ad";
        Assert.True(search.IsSearching);
        search.Query.Value = "adam";
        Assert.True(service.Calls[2].Token.IsCancellationRequested);
        service.Calls[3].Reply.SetResult(["Adam", "Adamant"]);

        Assert.Equal("Adam, Adamant", shown[^1]);
        Assert.Null(search.Error);
        Assert.Equal(["", "ada", "ad", "adam"], service.Calls.Select(call => call.Query));
        Assert.Equal(
            ["Searching…", "No matches", "Searching…", "Search failed: offline", "Searching…", "Adam, Adamant"],
            shown);
    }

    [Fact]
    public void FinishCurrentRunsOneTrailingSearchAfterTheFlightInProgress()
    {
        var service = new ScriptedSearch();
        var graph = new Graph(
            GraphOptions.Default
                .WithDispatcher(new ManualDispatcher())
                .WithFlightPolicy(FlightPolicy.FinishCurrent));
        var shown = new List<string>();

        var search = graph.Run(() => new SearchViewModel(service));
        graph.Run(() => Effect(() => shown.Add(search.Summary.Value)));

        search.Query.Value = "a";
        search.Query.Value = "ad";
        Assert.Single(service.Calls);
        Assert.False(service.Calls[0].Token.IsCancellationRequested);

        service.Calls[0].Reply.SetResult(["stale"]);
        Assert.True(search.IsSearching);
        Assert.Equal(["", "ad"], service.Calls.Select(call => call.Query));

        service.Calls[1].Reply.SetResult(["Ada"]);
        Assert.Equal("Ada", shown[^1]);
        Assert.DoesNotContain("stale", shown);
    }

    [Fact]
    public void BoundariesShowFallbackAndRecovery()
    {
        using var active = new Graph().Activate();
        var price = AsyncSource<int>();
        var view = Boundary(() => $"Total {price.Value * 3}", () => "Loading", ex => $"Unavailable: {ex.Message}");

        Assert.Equal("Loading", view.Value);
        price.Settle(4);
        Assert.Equal("Total 12", view.Value);
        price.Fail(new InvalidOperationException("feed offline"));
        Assert.Equal("Unavailable: feed offline", view.Value);
    }

    // The C# code and frames of the home page's hero.
    [Fact]
    public void HeroBoundaryMovesThroughItsStates()
    {
        using var graph = new Graph();
        using var _ = graph.Activate();

        var price = AsyncSource<int>();
        var total = Memo(() => price.Value * 3);
        var view = Boundary(
            () => $"Total {total.Value}",
            () => "Loading…",
            ex => $"Unavailable: {ex.Message}");

        Assert.Equal("Ready \"Loading…\"", view.TryValue.ToString());
        Assert.True(view.IsWaiting);
        price.Settle(4);
        Assert.Equal("Ready \"Total 12\"", view.TryValue.ToString());
        Assert.False(view.IsWaiting);
        price.Fail(new Exception("feed offline"));
        Assert.Equal("Ready \"Unavailable: feed offline\"", view.TryValue.ToString());
        Assert.NotNull(view.Caught);
        price.Settle(5);
        Assert.Equal("Ready \"Total 15\"", view.TryValue.ToString());
        Assert.Null(view.Caught);
    }

    [Fact]
    public void ReadingExposesValueAndError()
    {
        using var active = new Graph().Activate();
        var price = AsyncSource<int>();

        Assert.True(price.TryValue.IsPending);
        Assert.False(price.TryValue.TryGetValue(out _));

        price.Settle(5);
        Assert.True(price.TryValue.TryGetValue(out var value));
        Assert.Equal(5, value);

        price.Fail(new InvalidOperationException("down"));
        Assert.True(price.TryValue.TryGetError(out var error));
        Assert.Equal("down", error.Message);
    }

    [Fact]
    public void SuspenseAndErrorBoundaryEachCatchOneChannel()
    {
        using var active = new Graph().Activate();
        var price = AsyncSource<int>();
        var suspense = Suspense(() => price.Value, () => -1);
        var errors = ErrorBoundary(() => price.Value, _ => -2);

        Assert.Equal(-1, suspense.Value);
        Assert.True(errors.TryValue.IsPending);

        price.Fail(new InvalidOperationException("down"));
        Assert.True(suspense.TryValue.IsFailed);
        Assert.Equal(-2, errors.Value);
    }

    [Fact]
    public void AsyncMemoSettlesFromATask()
    {
        using var active = new Graph().Activate();
        var id = Signal(1);
        var pending = new List<TaskCompletionSource<string>>();
        var name = Async(token =>
        {
            _ = id.Value;
            var request = new TaskCompletionSource<string>();
            pending.Add(request);
            return request.Task;
        });
        var shown = Suspense(() => name.Value, () => "Loading");

        Assert.Equal("Loading", shown.Value);
        pending[^1].SetResult("Ada");
        Assert.Equal("Ada", shown.Value);

        id.Value = 2;
        Assert.Equal("Loading", shown.Value);
    }

    [Fact]
    public void AsyncMemoReceivesThePreviousValue()
    {
        using var active = new Graph().Activate();
        var step = Signal(1);
        var total = Async<int>(async (previous, token) =>
        {
            var by = step.Value;
            var last = await previous.Settled;
            return (last.IsSome ? last.Value : 0) + by;
        });

        Assert.Equal(1, total.Value);
        step.Value = 10;
        Assert.Equal(11, total.Value);
    }

    [Fact]
    public void AManualDispatcherQueuesWorkUntilPumped()
    {
        var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
        using var active = graph.Activate();
        var count = Signal(0);

        var writer = new Thread(() => graph.Dispatch(() => count.Value = 1));
        writer.Start();
        writer.Join();

        Assert.Equal(1, graph.PendingWork);
        Assert.Equal(0, count.Value);
        graph.Pump();
        Assert.Equal(1, count.Value);
    }
}
