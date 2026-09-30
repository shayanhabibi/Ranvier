using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

public class ReactiveTests
{
    [Fact]
    public void MemoAndEffectFollowASignal()
    {
        var seen = new List<int>();

        new Graph().Run(() =>
        {
            var count = Signal(1);
            var doubled = Memo(() => count.Value * 2);
            Effect(() => seen.Add(doubled.Value));
            Flush();

            count.Value = 2;
            Flush();
        });

        Assert.Equal([2, 4], seen);
    }

    [Fact]
    public void SeededMemoReceivesItsPreviousValue()
    {
        using var active = new Graph().Activate();
        var step = Signal(1);
        var total = Memo(previous => previous + step.Value, 100);

        Assert.Equal(101, total.Value);
        step.Value = 5;
        Assert.Equal(106, total.Value);
    }

    [Fact]
    public void BatchRunsAnEffectOnceForSeveralWrites()
    {
        using var active = new Graph().Activate();
        var a = Signal(1);
        var b = Signal(1);
        var runs = 0;
        Effect(() =>
        {
            _ = a.Value + b.Value;
            runs++;
        });
        Flush();

        Batch(() =>
        {
            a.Value = 2;
            b.Value = 2;
        });
        Flush();

        Assert.Equal(2, runs);
    }

    [Fact]
    public void ADisposedEffectStopsRunning()
    {
        using var active = new Graph().Activate();
        var count = Signal(1);
        var runs = 0;
        var effect = Effect(() =>
        {
            _ = count.Value;
            runs++;
        });
        Flush();

        effect.Dispose();
        count.Value = 2;
        Flush();

        Assert.Equal(1, runs);
    }

    [Fact]
    public void UntrackedReadsDoNotWakeAMemo()
    {
        using var active = new Graph().Activate();
        var tracked = Signal(1);
        var hidden = Signal(10);
        var runs = 0;
        var sum = Memo(() =>
        {
            runs++;
            return tracked.Value + Untrack(() => hidden.Value);
        });

        Assert.Equal(11, sum.Value);
        hidden.Value = 20;
        Assert.Equal(11, sum.Value);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void DisposingARootStopsItsEffectsAndRunsItsCleanups()
    {
        using var active = new Graph().Activate();
        var count = Signal(0);
        var runs = 0;
        var cleaned = 0;

        var root = Root(owner =>
        {
            Effect(() =>
            {
                _ = count.Value;
                runs++;
                OnCleanup(() => cleaned++);
            });
            return owner;
        });
        Flush();

        root.Dispose();
        count.Value = 1;
        Flush();

        Assert.Equal(1, runs);
        Assert.Equal(1, cleaned);
    }

    [Fact]
    public void SignalUpdateAppliesAFunction()
    {
        using var active = new Graph().Activate();
        var count = Signal(3);
        count.Update(n => n + 1);
        Assert.Equal(4, count.Value);
    }

    [Fact]
    public void EffectOnActsOnlyOnAChangedValue()
    {
        using var active = new Graph().Activate();
        var count = Signal(1);
        var acted = new List<bool>();
        EffectOn(() => count.Value > 5, acted.Add);
        Flush();

        count.Value = 2;
        Flush();
        count.Value = 9;
        Flush();

        Assert.Equal([false, true], acted);
    }

    [Fact]
    public void ErrorOriginNamesTheNodeAFailureCameFrom()
    {
        using var active = new Graph().Activate();
        var price = AsyncSource<decimal>();
        var total = Memo(() => price.Value * 2);
        var shown = ErrorBoundary(() => total.Value.ToString(), _ => "unavailable");

        Assert.Null(total.ErrorOrigin);
        price.Fail(new InvalidOperationException("offline"));
        Flush();

        Assert.Equal("unavailable", shown.Value);
        Assert.Same(price, shown.CaughtFrom);
        Assert.Same(price, total.ErrorOrigin);
        Assert.Same(price.ErrorOrigin, total.ErrorOrigin);
    }
}
