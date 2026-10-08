using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Ranvier;
using ToolkitVm = ToolkitExample.OrderViewModel;
using RanvierVm = RanvierExample.OrderViewModel;

internal static class WpfProgram
{
    [STAThread]
    private static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var checks = Verify();
        Console.WriteLine($"WPF correctness: {checks} checks passed.");
        var workloads = new[] { ("wpf-quantity-input", 10000), ("wpf-refresh", 1000), ("wpf-error-retry", 300), ("wpf-100-row-input", 10000), ("wpf-open-close", 300) };
        var trials = new List<WpfTrial>();
        foreach (var (workload, iterations) in workloads)
        {
            for (var warmup = 0; warmup < 2; warmup++)
                foreach (var kind in new[] { "Toolkit", "Ranvier" }) Measure(kind, workload, iterations, -1);
            for (var trial = 0; trial < 12; trial++)
                foreach (var kind in trial % 2 == 0 ? new[] { "Toolkit", "Ranvier" } : new[] { "Ranvier", "Toolkit" })
                    trials.Add(Measure(kind, workload, iterations, trial));
            Console.WriteLine($"Measured {workload}.");
        }
        var summaries = trials.GroupBy(t => new { t.Workload, t.Implementation }).Select(g => new
        {
            g.Key.Workload, g.Key.Implementation, Trials = g.Count(),
            MedianMicrosecondsPerOperation = Median(g.Select(t => t.MicrosecondsPerOperation)),
            MinMicrosecondsPerOperation = g.Min(t => t.MicrosecondsPerOperation),
            MaxMicrosecondsPerOperation = g.Max(t => t.MicrosecondsPerOperation),
            MedianUiThreadBytesPerOperation = Median(g.Select(t => t.UiThreadBytesPerOperation)),
            MedianNotificationsPerOperation = Median(g.Select(t => t.NotificationsPerOperation))
        }).ToArray();
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        while (!File.Exists(Path.Combine(directory, "Bench.csproj"))) directory = Directory.GetParent(directory)!.FullName;
        File.WriteAllText(Path.Combine(directory, "wpf-results.json"), JsonSerializer.Serialize(new
        {
            MeasuredUtc = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, CorrectnessChecks = checks,
            TieredCompilation = false, RanvierTracing = false,
            SourceHashes = new[] { "WpfProgram.cs", "Toolkit.cs", "Ranvier.cs", "wpf/WpfBench.csproj" }.ToDictionary(
                f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, f))))),
            Summaries = summaries, Trials = trials
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var (workload, _) in workloads)
        {
            var a = summaries.Single(s => s.Workload == workload && s.Implementation == "Toolkit");
            var b = summaries.Single(s => s.Workload == workload && s.Implementation == "Ranvier");
            Console.WriteLine($"{workload}: Toolkit {a.MedianMicrosecondsPerOperation:F3} us, Ranvier {b.MedianMicrosecondsPerOperation:F3} us ({b.MedianMicrosecondsPerOperation / a.MedianMicrosecondsPerOperation:F2}x); UI bytes {a.MedianUiThreadBytesPerOperation:F0}/{b.MedianUiThreadBytesPerOperation:F0}; notifications {a.MedianNotificationsPerOperation:F2}/{b.MedianNotificationsPerOperation:F2}");
        }
    }
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
    }
    internal static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static int Verify()
    {
        var checks = 0;
        void Assert(bool value) { if (!value) throw new Exception("WPF binding correctness"); checks++; }
        foreach (var kind in new[] { "Toolkit", "Ranvier" })
        {
            using var row = new BoundRow(kind, null);
            Drain();
            Assert(row.Display.Text == "No quote" && row.Quantity.Text == "1");
            row.Begin(); Drain();
            Assert(row.Display.Text == "Loading…" && row.Loading.Text == "True" && row.Total.Text == "");
            row.Quantity.Text = "3";
            row.Begin();
            Assert(row.Prices.Calls == 1);
            row.Prices.Complete(); Drain();
            Assert(row.Display.Text == "Total: ¤12.00" && row.Total.Text == "12" && row.Loading.Text == "False");
            row.Quantity.Text = "5"; Drain();
            Assert(row.Display.Text == "Total: ¤20.00" && row.Total.Text == "20");
            row.Begin(); row.Quantity.Text = "6"; Drain();
            Assert(row.Display.Text == "Loading…" && row.Total.Text == "");
            row.Prices.Complete(true); Drain();
            Assert(row.Display.Text == "Unavailable: offline" && row.Error.Text == "offline" && row.HasErrors.Text == "True");
            row.Begin(); Drain();
            Assert(row.Error.Text == "" && row.HasErrors.Text == "False");
            Task.Run(() => row.Prices.Complete()).GetAwaiter().GetResult(); Drain();
            Assert(row.Display.Text == "Total: ¤24.00" && row.Total.Text == "24");
            row.Begin();
            row.Dispose();
            Assert(row.Prices.Token.IsCancellationRequested);
            row.Prices.Complete(); Drain();
        }
        return checks;
    }
    private static WpfTrial Measure(string kind, string workload, int iterations, int trial)
    {
        using var graph = kind == "Ranvier" ? new Graph() : null;
        var rows = new List<BoundRow>();
        if (workload != "wpf-open-close")
            for (var i = 0; i < (workload == "wpf-100-row-input" ? 100 : 1); i++)
            {
                var row = new BoundRow(kind, graph);
                rows.Add(row); row.Begin(); row.Prices.Complete();
            }
        Drain();
        foreach (var row in rows) row.Notifications = 0;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long closedNotifications = 0;
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            if (workload == "wpf-open-close")
            {
                using var opened = new BoundRow(kind, null);
                opened.Begin(); opened.Prices.Complete(); Drain();
                closedNotifications += opened.Notifications;
                continue;
            }
            var row = rows[workload == "wpf-100-row-input" ? i * 37 % rows.Count : 0];
            if (workload is "wpf-quantity-input" or "wpf-100-row-input") row.Quantity.Text = (i % 9 + 1).ToString(CultureInfo.InvariantCulture);
            else
            {
                row.Begin();
                row.Quantity.Text = ((i + 2) % 9 + 1).ToString(CultureInfo.InvariantCulture);
                row.Begin();
                row.Prices.Complete(workload == "wpf-error-retry"); Drain();
                if (workload == "wpf-error-retry") { row.Begin(); row.Prices.Complete(); }
            }
            Drain();
        }
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        var notifications = closedNotifications + rows.Sum(r => r.Notifications);
        foreach (var row in rows)
        {
            var expected = decimal.Parse(row.Quantity.Text, CultureInfo.InvariantCulture) * 4m;
            if (row.Total.Text != expected.ToString(CultureInfo.InvariantCulture) || row.Display.Text != $"Total: {expected:C}") throw new Exception("WPF final state");
            row.Dispose();
        }
        return new WpfTrial(workload, kind, trial, iterations, elapsed * 1_000_000.0 / Stopwatch.Frequency / iterations,
            allocated / (double)iterations, notifications / (double)iterations);
    }
}

public interface IPriceService
{
    Task<decimal> GetPriceAsync(string product, CancellationToken token);
}

internal sealed class WpfPrices : IPriceService
{
    private TaskCompletionSource<decimal>? reply;
    public CancellationToken Token;
    public int Calls;
    public Task<decimal> GetPriceAsync(string product, CancellationToken token)
    {
        if (reply is { Task.IsCompleted: false }) throw new Exception("Overlapping request");
        Token = token; Calls++;
        reply = new TaskCompletionSource<decimal>();
        return reply.Task;
    }
    public void Complete(bool fail = false)
    {
        if (fail) reply!.SetException(new InvalidOperationException("offline"));
        else reply!.SetResult(4m);
    }
}

internal sealed class BoundRow : IDisposable
{
    private readonly ToolkitVm? toolkit;
    private readonly RanvierVm? ranvier;
    private readonly Graph? ownedGraph;
    private readonly INotifyPropertyChanged source;
    private readonly StackPanel panel = new();
    private bool disposed;
    public readonly WpfPrices Prices = new();
    public readonly TextBox Quantity = new();
    public readonly TextBlock Display = new(), Total = new(), Loading = new(), Error = new(), HasErrors = new();
    public long Notifications;
    public BoundRow(string kind, Graph? graph)
    {
        if (kind == "Toolkit") source = toolkit = new ToolkitVm(Prices);
        else
        {
            if (graph is null) graph = ownedGraph = new Graph();
            source = ranvier = new RanvierVm(graph, Prices);
        }
        source.PropertyChanged += Count;
        panel.DataContext = source;
        panel.Children.Add(Quantity);
        Quantity.SetBinding(TextBox.TextProperty, new Binding("Quantity") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        foreach (var (control, name) in new[] { (Display, "Display"), (Total, "Total"), (Loading, "IsLoading"), (Error, "Error"), (HasErrors, "HasErrors") })
        {
            panel.Children.Add(control);
            control.SetBinding(TextBlock.TextProperty, new Binding(name));
        }
    }
    private void Count(object? sender, PropertyChangedEventArgs e) => Notifications++;
    public void Begin() { if (toolkit is not null) _ = toolkit.LoadAsync(); else ranvier!.Load(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        source.PropertyChanged -= Count;
        BindingOperations.ClearAllBindings(Quantity);
        foreach (var control in new[] { Display, Total, Loading, Error, HasErrors }) BindingOperations.ClearAllBindings(control);
        panel.DataContext = null;
        toolkit?.Dispose(); ranvier?.Dispose(); ownedGraph?.Dispose();
    }
}

internal sealed record WpfTrial(string Workload, string Implementation, int Index, int Operations,
    double MicrosecondsPerOperation, double UiThreadBytesPerOperation, double NotificationsPerOperation);
