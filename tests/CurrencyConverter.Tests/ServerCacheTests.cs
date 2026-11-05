using System.Net;
using CurrencyConverter.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CurrencyConverter.Tests;

public sealed class ServerCacheTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "currency-tests-" + Guid.NewGuid());
    private static readonly DateOnly Date = new(2025, 1, 20);
    private const string Body = """
        {"Date":"2025-01-20T11:30:00+03:00","Valute":{
          "USD":{"CharCode":"USD","Name":"Доллар США","Nominal":1,"Value":100},
          "JPY":{"CharCode":"JPY","Name":"Японских иен","Nominal":100,"Value":60}}}
        """;

    private CbrRatesProvider Provider(FakeHandler handler) => new(new Factory(handler),
        new MemoryCache(new MemoryCacheOptions()), new EnvironmentStub(directory), NullLogger<CbrRatesProvider>.Instance);

    [Fact]
    public async Task RepeatedRequestUsesMemoryCache()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Body) });
        var provider = Provider(handler);
        await provider.GetRatesAsync(Date, default);
        var rates = await provider.GetRatesAsync(Date, default);
        Assert.Equal(1, handler.Calls);
        Assert.Contains(rates.Currencies, c => c.Code == "RUB" && c.UnitRate == 1);
        Assert.Equal(0.6m, rates.Currencies.Single(c => c.Code == "JPY").UnitRate);
    }

    [Fact]
    public async Task DiskCacheSurvivesProviderRestart()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Body) });
        await Provider(handler).GetRatesAsync(Date, default);
        var offline = new FakeHandler(_ => throw new HttpRequestException());
        var rates = await Provider(offline).GetRatesAsync(Date, default);
        Assert.Equal(0, offline.Calls);
        Assert.Equal(Date, rates.EffectiveDate);
    }

    [Fact]
    public async Task MissingDateSearchesPreviousDaysAndCaches404()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.Contains("2025/01/20")
            ? new(HttpStatusCode.OK) { Content = new StringContent(Body) }
            : new(HttpStatusCode.NotFound));
        var provider = Provider(handler);
        var rates = await provider.GetRatesAsync(Date.AddDays(2), default);
        await provider.GetRatesAsync(Date.AddDays(2), default);
        Assert.Equal(Date.AddDays(2), rates.RequestedDate);
        Assert.Equal(Date, rates.EffectiveDate);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task ServerErrorIsNotMistakenForMissingDate()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(handler).GetRatesAsync(Date, default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneUpstreamRequest()
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Body) });
        var provider = Provider(handler);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => provider.GetRatesAsync(Date, default)));
        Assert.Equal(1, handler.Calls);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
    private sealed class Factory(FakeHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://www.cbr-xml-daily.ru/") };
    }
    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
