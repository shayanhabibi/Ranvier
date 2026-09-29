using Ranvier;
using Ranvier.CSharp;
using Xunit;
using static Ranvier.CSharp.Reactive;

namespace CSharpCallers.Headline;

// The headline sample at the top of docs/content/guide/csharp.md, as published.
public interface IWeatherService
{
    Task<int> TemperatureAsync(string city, CancellationToken token);
}

public sealed class WeatherViewModel : ReactiveObject
{
    readonly BoundSignal<string> city;
    readonly Signal<int> attempt;
    readonly BoundValue<string> forecast;

    public WeatherViewModel(Graph graph, IWeatherService weather) : base(graph)
    {
        city = Bindings.Writable(nameof(City), "Oslo");
        attempt = Bindings.Run(() => Signal(0));
        var celsius = Bindings.Run(() => Async(token =>
        {
            _ = attempt.Value;
            return weather.TemperatureAsync(city.Value, token);
        }));
        forecast = Bindings.Computed(nameof(Forecast), () => $"{City}: {celsius.Value} °C");
    }

    public string City { get => city.Value; set => city.Value = value; }
    public string Forecast => forecast.Value;
    public void Retry() => attempt.Value++;
}

// Replies completed by hand, on the graph's thread.
sealed class ManualWeather : IWeatherService
{
    public readonly List<(string City, TaskCompletionSource<int> Reply)> Requests = new();

    public Task<int> TemperatureAsync(string city, CancellationToken token)
    {
        var reply = new TaskCompletionSource<int>();
        Requests.Add((city, reply));
        return reply.Task;
    }
}

public class HeadlineSampleTests
{
    [Fact]
    public void TheWeatherViewModelLoadsFailsAndRetries()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            using var graph = new Graph(GraphOptions.Default.WithDispatcher(new ManualDispatcher()));
            var weather = new ManualWeather();
            using var vm = new WeatherViewModel(graph, weather);
            var names = new List<string>();
            var errors = new List<string>();
            vm.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
            vm.ErrorsChanged += (_, e) => errors.Add(e.PropertyName!);

            Assert.True(vm.IsLoading);
            Assert.Null(vm.Forecast);
            weather.Requests[^1].Reply.SetResult(12);
            Assert.Equal("Oslo: 12 °C", vm.Forecast);
            Assert.False(vm.IsLoading);
            Assert.Equal(["Forecast", "IsLoading"], names);

            names.Clear();
            vm.City = "Bergen";
            Assert.True(vm.IsLoading);
            Assert.Equal("Oslo: 12 °C", vm.Forecast);
            weather.Requests[^1].Reply.SetException(new HttpRequestException("offline"));
            Assert.False(vm.IsLoading);
            Assert.True(vm.HasErrors);
            Assert.Equal(["offline"], vm.GetErrors(nameof(WeatherViewModel.Forecast)).Cast<string>());
            Assert.Equal("Oslo: 12 °C", vm.Forecast);
            Assert.Equal(["City", "IsLoading", "IsLoading", "HasErrors"], names);
            Assert.Equal(["Forecast"], errors);

            names.Clear();
            errors.Clear();
            vm.Retry();
            Assert.True(vm.IsLoading);
            Assert.False(vm.HasErrors);
            Assert.Equal(["Oslo", "Bergen", "Bergen"], weather.Requests.Select(r => r.City));
            weather.Requests[^1].Reply.SetResult(9);
            Assert.Equal("Bergen: 9 °C", vm.Forecast);
            Assert.False(vm.IsLoading);
            Assert.Empty(vm.GetErrors(nameof(WeatherViewModel.Forecast)).Cast<string>());
            Assert.Equal(["IsLoading", "HasErrors", "Forecast", "IsLoading"], names);
            Assert.Equal(["Forecast"], errors);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
