using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;
namespace RanvierExample;

public sealed class OrderViewModel : ReactiveObject
{
    private readonly BoundSignal<int> quantity;
    private readonly Signal<int> request;
    private readonly BoundValue<decimal?> total;
    private readonly BoundValue<string?> error;
    private readonly BoundValue<string> display;

    public OrderViewModel(Graph graph, IPriceService prices) : base(graph)
    {
        quantity = Bindings.Writable(nameof(Quantity), 1);
        request = Bindings.Run(() => Signal(0));
        var price = Bindings.Run(() => Async(token =>
        {
            _ = request.Value;
            return prices.GetPriceAsync("coffee", token);
        }));
        var calculated = Bindings.Computed<decimal?>("CalculatedTotal", () =>
            request.Value == 0 ? null : price.Value * quantity.Value);
        var available = Bindings.Run(() => Boundary(
            () => calculated.Memo.Value,
            () => (decimal?)null,
            _ => (decimal?)null));
        total = Bindings.Computed(nameof(Total), () => available.Value);
        var view = Bindings.Run(() => Boundary(
            () => calculated.Memo.Value is decimal value ? $"Total: {value:C}" : "No quote",
            () => "Loading…",
            ex => $"Unavailable: {ex.Message}"));
        error = Bindings.Computed(nameof(Error), () => view.Caught?.Message);
        display = Bindings.Computed(nameof(Display), () => view.Value);
    }

    public int Quantity { get => quantity.Value; set => quantity.Value = value; }
    public decimal? Total => total.Value;
    public string? Error => error.Value;
    public string Display => display.Value;

    public void Load()
    {
        ObjectDisposedException.ThrowIf(Bindings.Owner.IsDisposed, this);
        if (!IsLoading) request.Value++;
    }
}
