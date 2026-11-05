using System.Net;
using System.Text.Json;
using CurrencyConverter.Core;
using Xunit;

namespace CurrencyConverter.Tests;

public sealed class SerializationAndHttpTests
{
    [Fact]
    public async Task ClientCallsOwnServerWithInvariantDateAndReadsJson()
    {
        var handler = new RecordingHandler();
        var client = new HttpRatesService(new HttpClient(handler), () => new Uri("https://my-server.example/"));
        var rates = await client.GetRatesAsync(new DateOnly(2025, 1, 20), default);
        Assert.Equal("https://my-server.example/api/rates?date=2025-01-20", handler.Address!.AbsoluteUri);
        Assert.Equal(new DateOnly(2025, 1, 18), rates.EffectiveDate);
        Assert.Equal(1m, Assert.Single(rates.Currencies).UnitRate);
    }

    [Fact]
    public void GeneratedJsonPreservesTheCompleteState()
    {
        var snapshot = new RatesSnapshot(new(2025, 1, 20), new(2025, 1, 18),
            [new("RUB", "Российский рубль", 1, 1)]);
        var state = new ConverterState(new(2025, 1, 20), "RUB", "USD", "12,5", "0,125", false, snapshot);
        var json = JsonSerializer.Serialize(state, ConverterJsonContext.Default.ConverterState);
        var restored = JsonSerializer.Deserialize(json, ConverterJsonContext.Default.ConverterState)!;
        Assert.Equal(state.SelectedDate, restored.SelectedDate);
        Assert.Equal(state.FromAmount, restored.FromAmount);
        Assert.Equal(state.ToAmount, restored.ToAmount);
        Assert.False(restored.EditFrom);
        Assert.Equal(snapshot.EffectiveDate, restored.Snapshot!.EffectiveDate);
        Assert.Equal("RUB", Assert.Single(restored.Snapshot.Currencies).Code);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? Address { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Address = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"requestedDate":"2025-01-20","effectiveDate":"2025-01-18",
                     "currencies":[{"code":"RUB","name":"Российский рубль","nominal":1,"value":1}]}
                    """)
            });
        }
    }
}
