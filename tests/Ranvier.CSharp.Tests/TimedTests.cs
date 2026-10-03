using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

public class TimedTests
{
    private sealed class Clock : TimedClock
    {
        private double now;
        private readonly List<Timer> timers = [];
        public override double NowMilliseconds => now;
        public override TimedTimer CreateTimer(Action callback)
        {
            var timer = new Timer(this, callback);
            timers.Add(timer);
            return timer;
        }
        public void Advance(double time)
        {
            now = time;
            foreach (var timer in timers.ToArray()) timer.Fire();
        }
        private sealed class Timer(Clock clock, Action callback) : TimedTimer
        {
            private double due = double.PositiveInfinity;
            public override void Arm(TimeSpan delay) => due = clock.now + delay.TotalMilliseconds;
            public override void Disarm() => due = double.PositiveInfinity;
            public override void Dispose() => Disarm();
            public void Fire()
            {
                if (clock.now < due) return;
                Disarm();
                callback();
            }
        }
    }

    [Fact]
    public void DebounceHoldsComputedInputUntilQuiet()
    {
        using var graph = new Graph();
        using var active = graph.Activate();
        var clock = new Clock();
        var input = Signal(0);
        using var output = Debounce(TimeSpan.FromMilliseconds(100), () => input.Value * 2, clock);
        input.Value = 1;
        clock.Advance(50);
        input.Value = 2;
        clock.Advance(100);
        Assert.Equal(0, output.Value);
        clock.Advance(150);
        Assert.Equal(4, output.Value);
    }

    [Fact]
    public void EveryThrottleFactoryAdmitsItsDocumentedMode()
    {
        using var graph = new Graph();
        using var active = graph.Activate();
        var clock = new Clock();
        var input = Signal(0);
        var delay = TimeSpan.FromMilliseconds(100);
        using var first = ThrottleFirst(delay, () => input.Value, clock);
        using var last = ThrottleLast(delay, () => input.Value, clock);
        using var both = Throttle(delay, () => input.Value, clock);
        input.Value = 1;
        input.Value = 2;
        Assert.Equal(1, first.Value);
        Assert.Equal(0, last.Value);
        Assert.Equal(1, both.Value);
        clock.Advance(100);
        Assert.Equal(1, first.Value);
        Assert.Equal(2, last.Value);
        Assert.Equal(2, both.Value);
    }

    [Fact]
    public void CustomComparerAndDefaultClockAreAvailable()
    {
        using var graph = new Graph();
        using var active = graph.Activate();
        var source = Signal("initial");
        using var timed = Debounce(TimeSpan.Zero, () => source.Value, comparer: StringComparer.OrdinalIgnoreCase);
        var seen = new List<string>();
        Effect(() => seen.Add(timed.Value));
        source.Value = "INITIAL";
        Assert.Equal(new[] { "initial" }, seen);
        Assert.Throws<ArgumentOutOfRangeException>(() => Debounce(TimeSpan.FromMilliseconds(-1), () => 0));
    }
}
