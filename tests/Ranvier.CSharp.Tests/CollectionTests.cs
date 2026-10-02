using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers;

public record Todo(int Id, string Title, bool Done, decimal Hours);

public class CollectionTests
{
    [Fact]
    public void ObservableAdapterReplacesEqualPayloadAfterRowReplacementAndMove()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            using var todos = KeyedCollection<Todo, int>(t => t.Id);
            todos.Edit(edit => { foreach (var todo in Initial) edit.AddOrUpdate(todo); });
            var titles = todos.Rows.Select(t => t.Title);
            using var identity = titles.NewKeyReader();
            identity.Read();
            using var sourceIdentity = todos.Rows.NewKeyReader();
            sourceIdentity.Read();
            var view = titles.AsObservableCollection();
            var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
            view.CollectionChanged += (_, change) => actions.Add(change.Action);
            todos.Edit(edit => { edit.Remove(1); edit.AddOrUpdate(Initial[0]); });
            Assert.Equal([new KeyValuePair<int, KeyChange>(1, KeyChange.Replaced)], sourceIdentity.Read().Changes);
            Assert.Equal([new KeyValuePair<int, KeyChange>(1, KeyChange.Replaced)], identity.Read().Changes);
            Assert.Equal(["test", "ship", "write"], view);
            Assert.Equal(1, actions.Count(a => a == System.Collections.Specialized.NotifyCollectionChangedAction.Replace));
        });
    }

    [Fact]
    public void EditableSourceFeedsFilterSortMapAndObservableAdapter()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            using var todos = KeyedCollection<Todo, int>(t => t.Id);
            todos.Edit(edit => { foreach (var todo in Initial) edit.AddOrUpdate(todo); });
            var titles = todos.Rows.Where(t => !t.Done).OrderBy(t => t.Title).Select(t => t.Title);
            var view = titles.AsObservableCollection();
            Assert.Equal(["ship", "write"], view);
            todos.AddOrUpdate(Initial[0] with { Title = "draft" });
            Assert.Equal(["draft", "ship"], view);
            todos.Edit(edit => { edit.Remove(3); edit.AddOrUpdate(Initial[1] with { Done = false }); });
            Assert.Equal(["draft", "test"], view);
            todos.Clear();
            Assert.Empty(view);
        });
    }

    [Fact]
    public void ObservableAdapterStartsWithPreviouslySettledFailedRows()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            var source = AsyncSource<int>();
            var rows = IndexProjection(() => new[] { 0 }, _ => source.Value);
            source.Settle(7);
            Assert.Equal(7, rows.Get(0));
            source.Fail(new InvalidOperationException("later failure"));
            var view = rows.AsObservableCollection();
            Assert.Equal([7], view);
        });
    }

    [Fact]
    public void ValueReaderReportsOnlyEditedRows()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            var todos = Signal(Initial);
            var rows = Projection(() => todos.Value, t => t.Id, t => t.Title);
            using var values = rows.NewValueReader();
            using var keys = rows.NewKeyReader();
            Assert.True(values.Read().IsReset);
            keys.Read();
            todos.Value = [Initial[0] with { Title = "rewrite" }, Initial[1], Initial[2]];
            var delta = values.Read();
            Assert.Equal([new KeyValuePair<int, KeyChange>(1, KeyChange.Changed)], delta.Changes);
            Assert.False(delta.OrderChanged);
            Assert.True(keys.Read().IsEmpty);
            Assert.True(values.Read().IsEmpty);
        });
    }

    [Fact]
    public void ObservableAdapterTouchesOnlyOneRowForOneEdit()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            var input = Signal(Initial);
            var mapped = 0;
            var rows = Projection(() => input.Value, t => t.Id, t => { mapped++; return t.Title; });
            var view = rows.AsObservableCollection();
            var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
            view.CollectionChanged += (_, change) => actions.Add(change.Action);
            input.Value = [Initial[0], Initial[1] with { Title = "retest" }, Initial[2]];
            Assert.Equal(4, mapped);
            Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Replace], actions);
            Assert.Equal(["write", "retest", "ship"], view);
        });
    }

    [Fact]
    public void ObservableAdapterRebuildsAfterAnEventHandlerThrows()
    {
        using var graph = new Graph();
        graph.Run(() =>
        {
            var input = Signal(Initial);
            var rows = Projection(() => input.Value, t => t.Id, t => t.Title);
            var view = rows.AsObservableCollection();
            var fail = true;
            view.CollectionChanged += (_, _) =>
            {
                if (fail) { fail = false; throw new InvalidOperationException("consumer failed"); }
            };
            input.Value = [Initial[0] with { Title = "rewrite" }, Initial[1], Initial[2]];
            input.Value = [input.Peek[0], Initial[1] with { Title = "retest" }, Initial[2]];
            Assert.Equal(["rewrite", "retest", "ship"], view);
        });
    }

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

    [Fact]
    public void ObservableCollectionRaisesMinimalEvents()
    {
        using var active = new Graph().Activate();
        var todos = Signal(Initial);
        var rows = Projection(() => todos.Value, t => t.Id, t => t.Title);
        var view = rows.AsObservableCollection();
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        view.CollectionChanged += (_, e) => actions.Add(e.Action);

        todos.Value = [Initial[0], Initial[2], Initial[1] with { Title = "retest" }];

        Assert.Equal(["write", "ship", "retest"], view);
        Assert.Equal(
            [
                System.Collections.Specialized.NotifyCollectionChangedAction.Move,
                System.Collections.Specialized.NotifyCollectionChangedAction.Replace,
            ],
            actions);
    }

    [Fact]
    public void KeyReaderReportsMembershipChanges()
    {
        using var active = new Graph().Activate();
        var todos = Signal(Initial);
        var rows = Projection(() => todos.Value, t => t.Id, t => t.Title);
        using var reader = rows.NewKeyReader();

        var first = reader.Read();
        Assert.True(first.IsReset);
        Assert.Equal([1, 2, 3], first.Keys);

        todos.Value = [Initial[2], Initial[0], new Todo(4, "review", false, 1m)];
        var delta = reader.Read();

        var added = new List<int>();
        var removed = new List<int>();

        foreach (var (key, change) in delta.Changes)
        {
            switch (change)
            {
                case KeyChange.Added:
                    added.Add(key);
                    break;
                case KeyChange.Removed:
                    removed.Add(key);
                    break;
                default:
                    Assert.Fail($"unexpected {change} for {key}");
                    break;
            }
        }

        Assert.Equal([4], added);
        Assert.Equal([2], removed);
        Assert.True(delta.OrderChanged);
        Assert.Equal([1, 2, 3], delta.PreviousKeys);
        Assert.Equal([3, 1, 4], delta.Keys);

        var mirror = new List<int>(delta.PreviousKeys);

        foreach (var edit in delta.Positional)
        {
            switch (edit)
            {
                case PositionalChange<int>.RemoveAt r:
                    mirror.RemoveAt(r.Index);
                    break;
                case PositionalChange<int>.InsertAt i:
                    mirror.Insert(i.Index, i.Key);
                    break;
                case PositionalChange<int>.Move m:
                    var moved = mirror[m.OldIndex];
                    mirror.RemoveAt(m.OldIndex);
                    mirror.Insert(m.NewIndex, moved);
                    break;
            }
        }

        Assert.Equal(delta.Keys, mirror);
        Assert.True(reader.Read().IsEmpty);
    }
}
