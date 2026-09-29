using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

public class TracingTests
{
    [Fact]
    public void NamedReturnsTheBodysValue()
    {
        using var active = new Graph().Activate();

        Assert.Equal(42, Tracing.Named("answer", () => 42));
    }

    [Fact]
    public void NamedRunsAnActionBody()
    {
        using var active = new Graph().Activate();
        var ran = false;

        Tracing.Named("body", () => { ran = true; });

        Assert.True(ran);
    }

#if RANVIER_TRACE
    [Fact]
    public void OriginNamesTheLabelAndTheCallersFile()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));

        var origin = Tracing.Origin(graph, count);

        Assert.Contains("count", origin);
        Assert.Contains("TracingTests.cs:", origin);
    }

    [Fact]
    public void OriginOfAFacadeNodeNamesTheCallersFile()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Signal(1);
        var doubled = Memo(() => count.Value * 2);

        Assert.Contains("TracingTests.cs:", Tracing.Origin(graph, doubled));
    }

    [Fact]
    public void LabelReplacesTheNodesPathSegment()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));

        Tracing.Label(graph, count, "renamed");

        Assert.Contains("renamed", Tracing.Origin(graph, count));
    }

    [Fact]
    public void WhyTracesARunBackToTheWrite()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        var doubled = Tracing.Named("doubled", () => Memo(() => count.Value * 2));
        _ = doubled.Value;

        count.Value = 2;
        _ = doubled.Value;

        var why = Tracing.Why(graph, doubled);
        Assert.Contains("count", why);
        Assert.Equal(why, Tracing.Why(graph, doubled, 2));
    }

    [Fact]
    public void WhyTracesAnEffectsRunBackToTheWrite()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        var log = Effect(() => _ = count.Value);
        Flush();

        count.Value = 2;
        Flush();

        Assert.Contains("count", Tracing.Why(graph, log));
        Assert.Contains("TracingTests.cs:", Tracing.Origin(graph, log));
    }

    [Fact]
    public void AnEffectOnIsQueriedByItsPath()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        Tracing.Named("log", () => EffectOn(() => count.Value, _ => { }));
        Flush();

        count.Value = 2;
        Flush();

        Assert.Contains("count", Tracing.Why(graph, "/log"));
        Assert.Equal(Tracing.Why(graph, "/log"), Tracing.Why(graph, "/log", 2));
        Assert.Contains("TracingTests.cs:", Tracing.Origin(graph, "/log"));
        Assert.Throws<ArgumentException>(() => Tracing.Why(graph, "/missing"));
    }

    [Fact]
    public void ResolveFindsANodeByItsPath()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));

        Assert.Equal(((INode)count).Id, Tracing.Resolve(graph, "/count"));
        Assert.Null(Tracing.Resolve(graph, "/missing"));
    }

    [Fact]
    public void DumpTextHoldsOneJsonObjectPerLine()
    {
        var graph = new Graph();
        using var active = graph.Activate();
        var count = Tracing.Named("count", () => Signal(1));
        count.Value = 2;

        var lines = Tracing.DumpText(graph).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.StartsWith("{", line));
        Assert.Empty(Tracing.Reconcile(graph));
    }
#endif
}
