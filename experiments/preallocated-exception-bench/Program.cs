using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    private const int Iterations = 200_000;
    private const int Warmup = 20_000;
    private const int Rounds = 7;
    private static readonly string[] Names = ["baseline", "fresh", "cached", "override", "cached-oom", "native-oom"];
    private static readonly int[] Depths = [0, 16];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly MethodInfo ImmutableCheck = typeof(Exception).GetMethod("IsImmutableAgileException", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("CoreCLR Exception.IsImmutableAgileException");
    private static readonly Dictionary<string, string> ChildEnvironment = new()
    {
        ["DOTNET_TieredCompilation"] = "0",
        ["DOTNET_TieredPGO"] = "0",
        ["DOTNET_ReadyToRun"] = "0",
        ["DOTNET_gcServer"] = "0"
    };
    private static volatile int sink;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--child", var name, var depth])
            {
                Console.WriteLine(JsonSerializer.Serialize(Measure(name, int.Parse(depth))));
                return 0;
            }
            if (args is ["--probe-native"] or ["--verify"])
            {
                Console.WriteLine(JsonSerializer.Serialize(Verify(), JsonOptions));
                return 0;
            }
            if (args.Length != 0 && args is not ["--out", _])
                throw new ArgumentException("Usage: [--verify | --probe-native | --out report.json]");

            var verification = Verify();
            var samples = new List<Sample>();
            var random = new Random(1751);
            for (var round = 0; round < Rounds; round++)
            {
                var cases = Depths.SelectMany(depth => Names.Select(name => (name, depth))).ToArray();
                random.Shuffle(cases);
                foreach (var item in cases)
                {
                    Console.Error.WriteLine($"Round {round + 1}/{Rounds}: {item.name}, depth {item.depth}");
                    samples.Add(await RunChild(item.name, item.depth));
                }
            }

            var results = samples.GroupBy(s => (s.Name, s.Depth)).OrderBy(g => g.Key.Depth).ThenBy(g => Array.IndexOf(Names, g.Key.Name))
                .Select(g => new
                {
                    scenario = g.Key.Name, depth = g.Key.Depth, rounds = g.Count(),
                    medianNsOp = Median(g.Select(s => s.NsOp)),
                    minNsOp = g.Min(s => s.NsOp), maxNsOp = g.Max(s => s.NsOp),
                    medianBytesOp = Median(g.Select(s => s.BytesOp)),
                    rawFrames = g.Select(s => s.Diagnostic?.RawFrames).Distinct().ToArray(),
                    immutable = g.Select(s => s.Diagnostic?.Immutable).Distinct().ToArray(),
                    samples = g.ToArray()
                }).ToArray();
            var json = JsonSerializer.Serialize(new
            {
                runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                logicalProcessors = Environment.ProcessorCount, utc = DateTimeOffset.UtcNow,
                iterations = Iterations, warmup = Warmup, environment = ChildEnvironment, verification, results
            }, JsonOptions);
            if (args is ["--out", var output])
                await File.WriteAllTextAsync(output, json + Environment.NewLine);
            else
                Console.WriteLine(json);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task<Sample> RunChild(string name, int depth)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--child");
        info.ArgumentList.Add(name);
        info.ArgumentList.Add(depth.ToString());
        foreach (var setting in ChildEnvironment)
            info.Environment[setting.Key] = setting.Value;
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Could not start benchmark child.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException($"Child {name}, depth {depth} exceeded three minutes.");
        }
        var text = await stdout;
        var error = await stderr;
        if (child.ExitCode != 0)
            throw new InvalidOperationException($"Child {name}, depth {depth}, exit {child.ExitCode}: {error}{text}");
        return JsonSerializer.Deserialize<Sample>(text) ?? throw new InvalidOperationException("Missing child result.");
    }

    private static Sample Measure(string name, int depth)
    {
        foreach (var setting in ChildEnvironment)
            if (Environment.GetEnvironmentVariable(setting.Key) != setting.Value)
                throw new InvalidOperationException($"Child requires {setting.Key}={setting.Value}; use the parent driver.");
        var scenario = CreateScenario(name, depth);
        for (var i = 0; i < Warmup; i++)
            scenario.Run();
        if (scenario.Exception != null)
            AssertCapture(scenario.Exception, name == "native-oom", depth);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = Stopwatch.GetTimestamp();
        _ = GC.GetAllocatedBytesForCurrentThread();
        var initialSink = sink;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
            scenario.Run();
        var end = Stopwatch.GetTimestamp();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (sink - initialSink != Iterations)
            throw new InvalidOperationException("The measured operations did not all complete.");
        if (scenario.Exception != null)
            AssertCapture(scenario.Exception, name == "native-oom", depth);
        if (name == "native-oom" && bytes != 0)
            throw new InvalidOperationException($"Native preallocated throws unexpectedly allocated {bytes} bytes.");
        return new Sample(name, depth, (end - start) * (1_000_000_000.0 / Stopwatch.Frequency) / Iterations,
            (double)bytes / Iterations, scenario.Exception is null ? null : Describe(scenario.Exception), scenario.Acquisition);
    }

    private static Scenario CreateScenario(string name, int depth)
    {
        var scenario = new Scenario();
        if (name == "baseline")
        {
            scenario.Run = () => sink++;
            return scenario;
        }
        if (name == "fresh")
        {
            scenario.Run = () =>
            {
                var exception = new Probe();
                CatchThrow(exception, depth);
                scenario.Exception = exception;
            };
            return scenario;
        }
        if (name == "native-oom")
        {
            var acquired = NativePreallocated.Acquire();
            scenario.Exception = acquired.Exception;
            scenario.Acquisition = new AcquisitionInfo(acquired.Symbol, acquired.PdbIdentity);
            if (!IsImmutable(scenario.Exception) || scenario.Exception.GetType() != typeof(OutOfMemoryException))
                throw new InvalidOperationException("Native acquisition did not return CoreCLR's preallocated OOM object.");
        }
        else
        {
            scenario.Exception = name switch
            {
                "cached" => new Probe(),
                "override" => new NoTrace(),
                "cached-oom" => new OutOfMemoryException(),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }
        var cached = scenario.Exception;
        scenario.Run = () => CatchThrow(cached, depth);
        return scenario;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CatchThrow(Exception exception, int depth)
    {
        try
        {
            ThrowAtDepth(exception, depth);
            throw new InvalidOperationException("ThrowAtDepth returned.");
        }
        catch (Exception caught) when (ReferenceEquals(caught, exception))
        {
            sink++;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAtDepth(Exception exception, int depth)
    {
        if (depth == 0)
            throw exception;
        ThrowAtDepth(exception, depth - 1);
        sink += depth;
    }

    private static object Verify()
    {
        var acquired = NativePreallocated.Acquire();
        if (acquired.Exception.GetType() != typeof(OutOfMemoryException) || !IsImmutable(acquired.Exception))
            throw new InvalidOperationException("The acquired object is not the runtime preallocated OOM.");
        var rows = new List<object>();
        foreach (var (name, exception) in new (string, Exception)[]
        {
            ("cached", new Probe()), ("override", new NoTrace()), ("cached-oom", new OutOfMemoryException()),
            ("marshal-so", Marshal.GetExceptionForHR(unchecked((int)0x800703E9), new IntPtr(-1))!),
            ("marshal-oom", Marshal.GetExceptionForHR(unchecked((int)0x8007000E), new IntPtr(-1))!),
            ("native-oom", acquired.Exception)
        })
        {
            var before = Describe(exception);
            var after = new List<object>();
            foreach (var depth in Depths)
            {
                for (var i = 0; i < 3; i++)
                    CatchThrow(exception, depth);
                AssertCapture(exception, name == "native-oom", depth);
                after.Add(new { depth, diagnostic = Describe(exception) });
            }
            rows.Add(new { name, before, after });
        }
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        CatchThrow(acquired.Exception, 16);
        AssertCapture(acquired.Exception, true, 16);
        var second = NativePreallocated.Acquire();
        if (!ReferenceEquals(acquired.Exception, second.Exception))
            throw new InvalidOperationException("Native acquisition did not preserve singleton identity across collection.");
        return new { verified = true, acquired.Symbol, acquired.PdbIdentity, sameInstanceAfterCollection = true, rows };
    }

    private static void AssertCapture(Exception exception, bool preallocated, int depth)
    {
        var frames = new StackTrace(exception).FrameCount;
        if (IsImmutable(exception) != preallocated || (preallocated ? frames != 0 : frames != depth + 2))
            throw new InvalidOperationException($"Capture assertion failed: {exception.GetType()}, immutable={IsImmutable(exception)}, frames={frames}, depth={depth}.");
        if (exception is NoTrace && exception.StackTrace != string.Empty)
            throw new InvalidOperationException("The StackTrace override did not hide its rendered trace.");
    }

    private static bool IsImmutable(Exception exception) => (bool)ImmutableCheck.Invoke(null, [exception])!;
    private static Diagnostic Describe(Exception exception) => new(exception.GetType().FullName!, IsImmutable(exception),
        new StackTrace(exception).FrameCount, exception.StackTrace?.Length);
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private sealed class Scenario
    {
        internal Action Run = null!;
        internal Exception? Exception;
        internal AcquisitionInfo? Acquisition;
    }

    private sealed class Probe : Exception;
    private sealed class NoTrace : Exception { public override string? StackTrace => string.Empty; }
    private sealed record Diagnostic(string Type, bool Immutable, int RawFrames, int? RenderedTraceLength);
    private sealed record AcquisitionInfo(string Symbol, string PdbIdentity);
    private sealed record Sample(string Name, int Depth, double NsOp, double BytesOp, Diagnostic? Diagnostic, AcquisitionInfo? Acquisition);
}
