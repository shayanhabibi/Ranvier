using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ranvier;
using ToolkitVm = ToolkitExample.OrderViewModel;
using RanvierVm = RanvierExample.OrderViewModel;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var ui = new UiContext();
SynchronizationContext.SetSynchronizationContext(ui);
var checks = Verify(ui);
Console.WriteLine($"Correctness: {checks} checks passed.");
var scenarios = new[]
{
    new Scenario("open-load-close", 1000, "Create a screen, load its quote, display the total, close it; graph lifetime included."),
    new Scenario("quantity-edit", 30000, "Edit quantity on an already loaded cart; one edit per operation."),
    new Scenario("quantity-no-op", 30000, "Assign the already current quantity; one assignment per operation."),
    new Scenario("refresh", 3000, "Reload, edit quantity twice while pending, attempt an overlapping reload, then settle."),
    new Scenario("error-retry", 500, "Reload and fail, edit quantity while failed, retry and succeed."),
    new Scenario("mixed-journey", 3000, "Two loaded edits, refresh, two pending edits, overlapping reload; every tenth journey fails and retries."),
    new Scenario("100-cart-edit", 30000, "Edit one of 100 independently bound carts; Ranvier carts share one graph."),
    new Scenario("close-in-flight", 1000, "Create, start loading, detach the view and dispose, then deliver a late success."),
    new Scenario("delayed-service", 30, "Reload with a 10 ms Task.Delay on the thread pool, then pump the simulated UI context.")
};
var trials = new List<Trial>();
foreach (var scenario in scenarios)
{
    for (var warmup = 0; warmup < 2; warmup++)
        foreach (var implementation in new[] { "Toolkit", "Ranvier" })
            Measure(scenario, implementation, -1, ui);
    var repeats = scenario.Name == "delayed-service" ? 6 : 12;
    for (var trial = 0; trial < repeats; trial++)
    {
        var order = trial % 2 == 0 ? new[] { "Toolkit", "Ranvier" } : new[] { "Ranvier", "Toolkit" };
        foreach (var implementation in order)
            trials.Add(Measure(scenario, implementation, trial, ui));
    }
    Console.WriteLine($"Measured {scenario.Name}.");
}
var summaries = trials.GroupBy(t => new { t.Scenario, t.Implementation }).Select(g => new
{
    g.Key.Scenario,
    g.Key.Implementation,
    Trials = g.Count(),
    MedianMicrosecondsPerOperation = Median(g.Select(t => t.MicrosecondsPerOperation)),
    MinMicrosecondsPerOperation = g.Min(t => t.MicrosecondsPerOperation),
    MaxMicrosecondsPerOperation = g.Max(t => t.MicrosecondsPerOperation),
    MedianUiThreadBytesPerOperation = Median(g.Select(t => t.UiThreadBytesPerOperation)),
    MedianNotificationsPerOperation = Median(g.Select(t => t.NotificationsPerOperation)),
    MedianBindingReadsPerOperation = Median(g.Select(t => t.BindingReadsPerOperation))
}).ToArray();
var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
while (!File.Exists(Path.Combine(directory, "Bench.csproj"))) directory = Directory.GetParent(directory)!.FullName;
var results = new
{
    MeasuredUtc = DateTimeOffset.UtcNow,
    Runtime = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription,
    LogicalProcessors = Environment.ProcessorCount,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    ToolkitVersion = typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject).Assembly.GetName().Version!.ToString(),
    RanvierVersion = typeof(Graph).Assembly.GetName().Version!.ToString(),
    CorrectnessChecks = checks,
    TieredCompilation = false,
    RanvierTracing = false,
    SourceHashes = new[] { "Program.cs", "Toolkit.cs", "Ranvier.cs", "Bench.csproj" }.ToDictionary(
        f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, f))))),
    Scenarios = scenarios,
    Summaries = summaries,
    Trials = trials
};
File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
foreach (var scenario in scenarios)
{
    var toolkit = summaries.Single(s => s.Scenario == scenario.Name && s.Implementation == "Toolkit");
    var ranvier = summaries.Single(s => s.Scenario == scenario.Name && s.Implementation == "Ranvier");
    Console.WriteLine($"{scenario.Name}: Toolkit {toolkit.MedianMicrosecondsPerOperation:F3} us, Ranvier {ranvier.MedianMicrosecondsPerOperation:F3} us ({ranvier.MedianMicrosecondsPerOperation / toolkit.MedianMicrosecondsPerOperation:F2}x); UI bytes {toolkit.MedianUiThreadBytesPerOperation:F0}/{ranvier.MedianUiThreadBytesPerOperation:F0}; notifications {toolkit.MedianNotificationsPerOperation:F2}/{ranvier.MedianNotificationsPerOperation:F2}");
}

static double Median(IEnumerable<double> values)
{
    var sorted = values.Order().ToArray();
    return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
}

static Trial Measure(Scenario scenario, string implementation, int trial, UiContext ui)
{
    using var fixture = new Fixture(implementation, ui);
    var cold = scenario.Name is "open-load-close" or "close-in-flight";
    if (!cold) fixture.Prepare(scenario.Name == "100-cart-edit" ? 100 : 1);
    fixture.ResetCounters();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var g0 = GC.CollectionCount(0);
    var g1 = GC.CollectionCount(1);
    var g2 = GC.CollectionCount(2);
    var bytes = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    fixture.Run(scenario.Name, scenario.Iterations);
    var elapsed = Stopwatch.GetTimestamp() - start;
    var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
    return new Trial(scenario.Name, implementation, trial, scenario.Iterations,
        elapsed * 1_000_000.0 / Stopwatch.Frequency / scenario.Iterations,
        allocated / (double)scenario.Iterations,
        fixture.Notifications / (double)scenario.Iterations,
        fixture.BindingReads / (double)scenario.Iterations,
        GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2);
}

static int Verify(UiContext ui)
{
    var checks = 0;
    void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    foreach (var implementation in new[] { "Toolkit", "Ranvier" })
    {
        using var cart = new Cart(implementation, null);
        Assert(cart.Display == "No quote" && cart.Prices.Calls == 0, "Initial idle state");
        cart.Begin();
        Assert(cart.IsLoading && cart.Total is null && cart.Display == "Loading…", "Loading state");
        cart.Quantity = 3;
        cart.Begin();
        Assert(cart.Prices.Calls == 1 && cart.Total is null, "Ignore overlapping requests");
        cart.Prices.Complete();
        ui.Pump();
        Assert(!cart.IsLoading && cart.Total == 12m && cart.Display == "Total: ¤12.00", "Success state");
        Assert(cart.RenderedDisplay == cart.Display && cart.RenderedTotal == cart.Total, "Bound view sees success");
        cart.Quantity = 5;
        Assert(cart.Total == 20m && cart.RenderedDisplay == cart.Display && cart.RenderedTotal == cart.Total, "Loaded edit notifications");
        cart.Begin();
        Assert(cart.Total is null && cart.RenderedTotal is null && cart.RenderedDisplay == "Loading…", "Reload notifications");
        cart.Quantity = 6;
        Assert(cart.Total is null && cart.Display == "Loading…", "Quantity edit during refresh");
        cart.Prices.Complete(true);
        ui.Pump();
        Assert(!cart.IsLoading && cart.HasErrors && cart.Error == "offline" && cart.Display == "Unavailable: offline", "Failure state");
        Assert(cart.RenderedError == cart.Error && cart.RenderedDisplay == cart.Display && cart.RenderedHasErrors, "Bound view sees failure");
        cart.Quantity = 7;
        Assert(cart.Total is null && cart.Display == "Unavailable: offline", "Edit during failure");
        cart.Begin();
        Assert(cart.IsLoading && !cart.HasErrors && cart.Error is null && cart.RenderedError is null, "Retry clears errors");
        Task.Run(() => cart.Prices.Complete()).GetAwaiter().GetResult();
        ui.Pump();
        Assert(cart.Total == 28m && cart.RenderedTotal == 28m && cart.RenderedDisplay == cart.Display, "Thread-pool completion and recovery");
        cart.ResetCounters();
        cart.Quantity = 7;
        Assert(cart.Notifications == 0 && cart.BindingReads == 0, "No-op assignment");
        cart.Begin();
        cart.Dispose();
        Assert(cart.Prices.Token.IsCancellationRequested, "Disposal cancels token");
        cart.Prices.Complete();
        ui.Pump();
        Assert(cart.RenderedDisplay == "Loading…", "Detached view receives no late update");
    }
    foreach (var implementation in new[] { "Toolkit", "Ranvier" })
    {
        using var fixture = new Fixture(implementation, ui);
        fixture.Prepare(100);
        fixture.ResetCounters();
        fixture.Carts[37].Quantity = 4;
        Assert(fixture.Carts.Where((_, i) => i != 37).All(c => c.Notifications == 0), "Only edited row notified");
        Assert(fixture.Carts[37].RenderedTotal == 16m, "Edited row updated");
    }
    return checks;
}

public interface IPriceService
{
    Task<decimal> GetPriceAsync(string product, CancellationToken token);
}

public sealed class Prices : IPriceService
{
    private TaskCompletionSource<decimal>? reply;
    public CancellationToken Token { get; private set; }
    public int Calls { get; private set; }
    public Task<decimal> GetPriceAsync(string product, CancellationToken token)
    {
        if (reply is { Task.IsCompleted: false }) throw new InvalidOperationException("Overlapping service call");
        reply = new TaskCompletionSource<decimal>();
        Token = token;
        Calls++;
        return reply.Task;
    }
    public void Complete(bool fail = false)
    {
        if (fail) reply!.SetException(new InvalidOperationException("offline"));
        else reply!.SetResult(4m);
    }
    public void CompleteDelayed()
    {
        var pending = reply!;
        _ = Task.Run(async () =>
        {
            await Task.Delay(10).ConfigureAwait(false);
            pending.SetResult(4m);
        });
    }
}

public sealed class UiContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
    public override void Post(SendOrPostCallback callback, object? state) => queue.Enqueue((callback, state));
    public void Pump()
    {
        while (queue.TryDequeue(out var work)) work.Callback(work.State);
    }
    public void WaitUntil(Func<bool> done)
    {
        var start = Stopwatch.GetTimestamp();
        while (!done())
        {
            Pump();
            if (Stopwatch.GetElapsedTime(start).TotalSeconds > 5) throw new TimeoutException("UI completion");
            if (!done()) Thread.Sleep(1);
        }
        Pump();
    }
}

public sealed class Cart : IDisposable
{
    private readonly ToolkitVm? toolkit;
    private readonly RanvierVm? ranvier;
    private readonly Graph? ownedGraph;
    private readonly INotifyPropertyChanged notifications;
    private bool disposed;
    public Prices Prices { get; } = new();
    public long Notifications { get; private set; }
    public long BindingReads { get; private set; }
    public string? RenderedDisplay { get; private set; }
    public decimal? RenderedTotal { get; private set; }
    public string? RenderedError { get; private set; }
    public bool RenderedHasErrors { get; private set; }
    public long Checksum { get; private set; }
    public Cart(string implementation, Graph? graph)
    {
        if (implementation == "Toolkit") notifications = toolkit = new ToolkitVm(Prices);
        else
        {
            if (graph is null) graph = ownedGraph = new Graph();
            notifications = ranvier = new RanvierVm(graph, Prices);
        }
        notifications.PropertyChanged += Observe;
        RenderedDisplay = Display;
        RenderedTotal = Total;
        RenderedError = Error;
        RenderedHasErrors = HasErrors;
    }
    public int Quantity
    {
        get => toolkit?.Quantity ?? ranvier!.Quantity;
        set { if (toolkit is not null) toolkit.Quantity = value; else ranvier!.Quantity = value; }
    }
    public string Display => toolkit?.Display ?? ranvier!.Display;
    public decimal? Total => toolkit is not null ? toolkit.Total : ranvier!.Total;
    public string? Error => toolkit is not null ? toolkit.Error : ranvier!.Error;
    public bool IsLoading => toolkit is not null ? toolkit.IsLoading : ranvier!.IsLoading;
    public bool HasErrors => toolkit is not null ? toolkit.HasErrors : ranvier!.HasErrors;
    public void Begin()
    {
        if (toolkit is not null) _ = toolkit.LoadAsync();
        else ranvier!.Load();
    }
    private void Observe(object? sender, PropertyChangedEventArgs e)
    {
        Notifications++;
        switch (e.PropertyName)
        {
            case "Quantity": BindingReads++; Checksum += Quantity; break;
            case "Total": BindingReads++; RenderedTotal = Total; Checksum += (long)(RenderedTotal ?? 0); break;
            case "Display": BindingReads++; RenderedDisplay = Display; Checksum += RenderedDisplay.Length; break;
            case "IsLoading": BindingReads++; Checksum += IsLoading ? 1 : 0; break;
            case "HasErrors": BindingReads++; RenderedHasErrors = HasErrors; Checksum += HasErrors ? 1 : 0; break;
            case "Error": BindingReads++; RenderedError = Error; Checksum += RenderedError?.Length ?? 0; break;
        }
    }
    public void ResetCounters() { Notifications = 0; BindingReads = 0; Checksum = 0; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        notifications.PropertyChanged -= Observe;
        toolkit?.Dispose();
        ranvier?.Dispose();
        ownedGraph?.Dispose();
    }
}

public sealed class Fixture : IDisposable
{
    private readonly string implementation;
    private readonly UiContext ui;
    private readonly Graph? graph;
    private long closedNotifications;
    private long closedReads;
    public List<Cart> Carts { get; } = new();
    public long Notifications => closedNotifications + Carts.Sum(c => c.Notifications);
    public long BindingReads => closedReads + Carts.Sum(c => c.BindingReads);
    public Fixture(string implementation, UiContext ui)
    {
        this.implementation = implementation;
        this.ui = ui;
        if (implementation == "Ranvier") graph = new Graph();
    }
    public void Prepare(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var cart = new Cart(implementation, graph);
            Carts.Add(cart);
            cart.Begin();
            cart.Prices.Complete();
            ui.Pump();
        }
    }
    public void ResetCounters()
    {
        closedNotifications = closedReads = 0;
        foreach (var cart in Carts) cart.ResetCounters();
    }
    public void Run(string scenario, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (scenario is "open-load-close" or "close-in-flight")
            {
                using var opened = new Cart(implementation, null);
                opened.Begin();
                if (scenario == "close-in-flight") opened.Dispose();
                opened.Prices.Complete();
                ui.Pump();
                closedNotifications += opened.Notifications;
                closedReads += opened.BindingReads;
                continue;
            }
            var cart = Carts[scenario == "100-cart-edit" ? i * 37 % Carts.Count : 0];
            switch (scenario)
            {
                case "quantity-edit":
                case "100-cart-edit": cart.Quantity = i % 9 + 1; break;
                case "quantity-no-op": cart.Quantity = 1; break;
                case "refresh": Refresh(cart, i); break;
                case "error-retry":
                    cart.Begin();
                    cart.Prices.Complete(true);
                    ui.Pump();
                    cart.Quantity = i % 9 + 1;
                    cart.Begin();
                    cart.Prices.Complete();
                    ui.Pump();
                    break;
                case "mixed-journey":
                    cart.Quantity = i % 9 + 1;
                    cart.Quantity = (i + 1) % 9 + 1;
                    Refresh(cart, i, i % 10 == 0);
                    break;
                case "delayed-service":
                    cart.Begin();
                    cart.Prices.CompleteDelayed();
                    ui.WaitUntil(() => !cart.IsLoading);
                    break;
            }
        }
        foreach (var cart in Carts)
            if (cart.IsLoading || cart.HasErrors || cart.Total != cart.Quantity * 4m || cart.Display != cart.RenderedDisplay || cart.Total != cart.RenderedTotal)
                throw new Exception($"Invalid final binding state in {scenario}/{implementation}");
    }
    private void Refresh(Cart cart, int i, bool fail = false)
    {
        cart.Begin();
        cart.Quantity = (i + 2) % 9 + 1;
        cart.Quantity = (i + 3) % 9 + 1;
        cart.Begin();
        cart.Prices.Complete(fail);
        ui.Pump();
        if (fail)
        {
            cart.Begin();
            cart.Prices.Complete();
            ui.Pump();
        }
    }
    public void Dispose()
    {
        foreach (var cart in Carts) cart.Dispose();
        graph?.Dispose();
    }
}

public sealed record Scenario(string Name, int Iterations, string Description);
public sealed record Trial(string Scenario, string Implementation, int Index, int Operations,
    double MicrosecondsPerOperation, double UiThreadBytesPerOperation,
    double NotificationsPerOperation, double BindingReadsPerOperation, int Gen0, int Gen1, int Gen2);
