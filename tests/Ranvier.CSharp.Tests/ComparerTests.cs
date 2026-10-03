using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

public class ComparerTests
{
    private sealed class ParityComparer : IEqualityComparer<int>
    {
        public bool Equals(int left, int right) => (left & 1) == (right & 1);
        public int GetHashCode(int value) => value & 1;
    }

    [Fact]
    public void SeededMemoComparerKeepsPreviousValueSemantics()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            var step = Signal(2);
            var memo = Memo(previous => previous + step.Value, 0, new ParityComparer());
            var seen = new List<int>();
            Effect(() => seen.Add(memo.Value));
            step.Value = 4;
            Assert.Equal(6, memo.Peek);
            Assert.Equal([2], seen);
            step.Value = 3;
            Assert.Equal([2, 9], seen);
        });
    }

    [Fact]
    public void SignalComparerOverridesOnlyThatSignal()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            var custom = Signal("Ada", StringComparer.OrdinalIgnoreCase);
            var normal = Signal("Ada");
            custom.Value = "ADA";
            normal.Value = "ADA";
            Assert.Equal("Ada", custom.Peek);
            Assert.Equal("ADA", normal.Peek);
        });
    }

    [Fact]
    public void ComparerOverloadsResolveAndSuppressEqualResults()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            var source = Signal("Ada");
            var equal = StringComparer.OrdinalIgnoreCase;
            var memo = Memo(() => source.Value, equal);
            var owning = OwningMemo(() => source.Value, equal);
            var suspense = Suspense(() => source.Value, () => "pending", equal);
            var errors = ErrorBoundary(() => source.Value, _ => "failed", equal);
            var boundary = Boundary(() => source.Value, () => "pending", _ => "failed", equal);
            var acted = new List<string>();
            EffectOn(() => source.Value, acted.Add, equal);
            var seen = new List<string>();
            Effect(() => seen.Add(memo.Value + owning.Value + suspense.Value + errors.Value + boundary.Value));
            source.Value = "ADA";
            Assert.Single(seen);
            Assert.Equal(["Ada"], acted);
            source.Value = "Grace";
            Assert.Equal(2, seen.Count);
            Assert.Equal(["Ada", "Grace"], acted);
        });
    }

    [Fact]
    public void NullComparersAreRejected()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            Assert.Throws<ArgumentNullException>(() => Signal(1, null!));
            Assert.Throws<ArgumentNullException>(() => Memo(() => 1, (IEqualityComparer<int>)null!));
            Assert.Throws<ArgumentNullException>(() => OwningMemo(() => 1, null!));
            Assert.Throws<ArgumentNullException>(() => EffectOn(() => 1, _ => { }, null!));
            Assert.Throws<ArgumentNullException>(() => Suspense(() => 1, () => 0, (IEqualityComparer<int>)null!));
            Assert.Throws<ArgumentNullException>(() => ErrorBoundary(() => 1, _ => 0, (IEqualityComparer<int>)null!));
            Assert.Throws<ArgumentNullException>(() => Boundary(() => 1, () => 0, _ => 0, (IEqualityComparer<int>)null!));
        });
    }
}
