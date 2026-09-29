using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

public record Todo(int Id, string Title, bool Done, decimal Hours);

public class CollectionTests
{
    static readonly Todo[] Initial =
    [
        new(1, "write", false, 2m),
        new(2, "test", true, 1.5m),
        new(3, "ship", false, 0.5m),
    ];

    [Fact]
    public void OperatorsFollowTheSource()
    {
        using var active = new Graph().Activate();
        var todos = Signal(Initial);
        var rows = Projection(() => todos.Value, t => t.Id, t => t);

        var open = rows.Where(t => !t.Done);
        var hours = rows.Sum(t => t.Hours);
        var done = rows.Count(t => t.Done);
        var allDone = rows.All(t => t.Done);
        var titles = open.OrderBy(t => t.Title).Select(t => t.Title);

        Assert.Equal([1, 3], open.Keys);
        Assert.Equal(4m, hours.Value);
        Assert.Equal(1, done.Value);
        Assert.False(allDone.Value);
        Assert.Equal(["ship", "write"], titles.Keys.Select(titles.Get));

        todos.Update(ts => ts.Select(t => t with { Done = true }).ToArray());

        Assert.Empty(open.Keys);
        Assert.Equal(3, done.Value);
        Assert.True(allDone.Value);
    }

    [Fact]
    public void TakeSkipAndAggregate()
    {
        using var active = new Graph().Activate();
        var numbers = Signal(new[] { 1, 2, 3, 4, 5 });
        var rows = IndexProjection(() => numbers.Value, n => n);
        var window = Signal(2);

        var first = rows.Take(() => window.Value);
        var rest = rows.Skip(2);
        var product = rows.Aggregate(1, (acc, n) => acc * n);
        var sum = rows.Aggregate(0, (acc, n) => acc + n, (acc, n) => acc - n);

        Assert.Equal([0, 1], first.Keys);
        Assert.Equal([2, 3, 4], rest.Keys);
        Assert.Equal(120, product.Value);
        Assert.Equal(15, sum.Value);

        window.Value = 4;
        Assert.Equal(4, first.Keys.Length);
    }

    [Fact]
    public void GroupByGivesAnInnerProjectionPerGroup()
    {
        using var active = new Graph().Activate();
        var todos = Signal(Initial);
        var rows = Projection(() => todos.Value, t => t.Id, t => t);
        var byDone = rows.GroupBy(t => t.Done);

        Assert.Equal([false, true], byDone.Keys);
        Assert.Equal([1, 3], byDone.Get(false).Keys);
        Assert.True(byDone.GroupOf(2));
    }

    [Fact]
    public void TryGetValueReadsARow()
    {
        using var active = new Graph().Activate();
        var todos = Signal(Initial);
        var rows = Projection(() => todos.Value, t => t.Id, t => t.Title);

        Assert.True(rows.TryGetValue(2, out var title));
        Assert.Equal("test", title);
        Assert.False(rows.TryGetValue(9, out _));
    }

    [Fact]
    public void SelectorAndLookup()
    {
        using var active = new Graph().Activate();
        var selected = Signal(1);
        var isSelected = Selector(() => selected.Value);

        Assert.True(isSelected.Get(1));
        Assert.False(isSelected.Get(2));
        selected.Value = 2;
        Assert.False(isSelected.Get(1));
        Assert.True(isSelected.Get(2));

        var scale = Signal(10);
        var scaled = Lookup(() => scale.Value, (int s, int k) => s * k, (previous, next) => new[] { 1, 2, 3 });
        Assert.Equal(30, scaled.Get(3));
        Assert.True(scaled.TryGetValue(3, out var three));
        Assert.Equal(30, three);
        Assert.False(scaled.TryGetValue(7, out _));
    }
}
