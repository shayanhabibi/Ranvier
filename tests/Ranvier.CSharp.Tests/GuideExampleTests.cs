using System.Collections;
using System.ComponentModel;
using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers.GuideExample;

// The view model of docs/content/guide/csharp.md "Binding to XAML", over a stand-in for CommunityToolkit's
// ObservableObject.
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);
}

public interface IPriceService
{
    Task<decimal> QuoteAsync(int quantity, CancellationToken token);
}

public sealed class OrderViewModel : ObservableObject, INotifyDataErrorInfo, IDisposable
{
    readonly ReactiveBindings bindings;
    readonly BoundSignal<int> quantity;
    readonly BoundValue<decimal> price;
    readonly BoundValue<decimal> total;

    public OrderViewModel(Graph graph, IPriceService prices)
    {
        bindings = new ReactiveBindings(this, graph);
        bindings.PropertyChanged += (_, e) => OnPropertyChanged(e);
        bindings.ErrorsChanged += (_, e) => ErrorsChanged?.Invoke(this, e);

        quantity = bindings.Writable(nameof(Quantity), 1);
        var quote = bindings.Run(() => Async(token => prices.QuoteAsync(quantity.Value, token)));
        price = bindings.Computed(nameof(Price), () => quote.Value);
        total = bindings.Computed(nameof(Total), () => price.Memo.Value * quantity.Value);
    }

    public int Quantity { get => quantity.Value; set => quantity.Value = value; }
    public decimal Price => price.Value;
    public decimal Total => total.Value;
    public bool IsLoading => bindings.IsLoading;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => bindings.HasErrors;
    public IEnumerable GetErrors(string? propertyName) => bindings.GetErrors(propertyName!);
    public void Dispose() => bindings.Dispose();
}

sealed class ManualPrices : IPriceService
{
    public readonly List<TaskCompletionSource<decimal>> Requests = new();

    public Task<decimal> QuoteAsync(int quantity, CancellationToken token)
    {
        var request = new TaskCompletionSource<decimal>();
        Requests.Add(request);
        return request.Task;
    }
}

public class GuideExampleTests
{
    [Fact]
    public void TheGuideViewModelLoadsFailsAndRecovers()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            using var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
            var prices = new ManualPrices();
            using var vm = new OrderViewModel(graph, prices);
            var names = new List<string>();
            vm.PropertyChanged += (_, e) => names.Add(e.PropertyName!);

            Assert.True(vm.IsLoading);
            prices.Requests[^1].SetResult(2m);
            Assert.Equal(2m, vm.Total);
            Assert.Equal(["Price", "Total", "IsLoading"], names);

            names.Clear();
            vm.Quantity = 3;
            Assert.True(vm.IsLoading);
            Assert.Equal(2m, vm.Price);
            prices.Requests[^1].SetException(new TimeoutException("quote timed out"));
            Assert.True(vm.HasErrors);
            Assert.Equal(["quote timed out"], vm.GetErrors(nameof(OrderViewModel.Price)).Cast<string>());
            Assert.Equal(["quote timed out"], vm.GetErrors(nameof(OrderViewModel.Total)).Cast<string>());
            Assert.Equal(["Quantity", "IsLoading", "IsLoading", "HasErrors"], names);
            Assert.False(vm.IsLoading);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
