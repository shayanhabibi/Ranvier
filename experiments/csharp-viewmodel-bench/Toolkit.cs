using CommunityToolkit.Mvvm.ComponentModel;
namespace ToolkitExample;

public partial class OrderViewModel(IPriceService prices) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total), nameof(Display))]
    private int quantity = 1;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total), nameof(Display))]
    private decimal? price;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total), nameof(Display))]
    private bool isLoading;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Total), nameof(Display), nameof(HasErrors))]
    private string? error;

    public decimal? Total => IsLoading || HasErrors ? null : Price * Quantity;
    public bool HasErrors => Error is not null;
    public string Display => IsLoading ? "Loading…"
        : Error is not null ? $"Unavailable: {Error}"
        : Total is decimal total ? $"Total: {total:C}"
        : "No quote";

    public async Task LoadAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsLoading) return;
        var token = lifetime.Token;
        IsLoading = true;
        Error = null;
        Price = null;
        try
        {
            var result = await prices.GetPriceAsync("coffee", token);
            token.ThrowIfCancellationRequested();
            Price = result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsLoading = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
