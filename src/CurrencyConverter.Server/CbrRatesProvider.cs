using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CurrencyConverter.Core;
using Microsoft.Extensions.Caching.Memory;

namespace CurrencyConverter.Server;

public sealed class CbrRatesProvider
{
    public static readonly DateOnly MinimumDate = new(1999, 1, 1);
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    private readonly IHttpClientFactory clients;
    private readonly IMemoryCache memory;
    private readonly ILogger<CbrRatesProvider> logger;
    private readonly string cacheDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CbrRatesProvider(IHttpClientFactory clients, IMemoryCache memory,
        IWebHostEnvironment environment, ILogger<CbrRatesProvider> logger)
    {
        this.clients = clients;
        this.memory = memory;
        this.logger = logger;
        cacheDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "rates-cache");
        Directory.CreateDirectory(cacheDirectory);
    }

    public async Task<RatesSnapshot> GetRatesAsync(DateOnly requested, CancellationToken token)
    {
        if (requested < MinimumDate || requested > Today) throw new ArgumentOutOfRangeException(nameof(requested));
        for (var offset = 0; offset <= 31 && requested.AddDays(-offset) >= MinimumDate; offset++)
        {
            token.ThrowIfCancellationRequested();
            var found = await GetDayAsync(requested.AddDays(-offset), token);
            if (found is not null && found.EffectiveDate <= requested)
                return found with { RequestedDate = requested };
        }
        throw new KeyNotFoundException("Курсы за выбранный период не найдены.");
    }

    private async Task<RatesSnapshot?> GetDayAsync(DateOnly date, CancellationToken token)
    {
        var key = date.ToString("yyyy-MM-dd");
        if (memory.TryGetValue<CacheEntry>(key, out var cached)) return cached!.Snapshot;
        await gate.WaitAsync(token);
        try
        {
            if (memory.TryGetValue<CacheEntry>(key, out cached)) return cached!.Snapshot;
            var file = Path.Combine(cacheDirectory, key + ".json");
            if (File.Exists(file))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<CacheEntry>(await File.ReadAllTextAsync(file, token), Json);
                    if (entry is not null && entry.ExpiresAt > DateTimeOffset.UtcNow)
                    {
                        memory.Set(key, entry, entry.ExpiresAt);
                        return entry.Snapshot;
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    logger.LogWarning(exception, "Повреждён кеш {File}; загрузим данные заново", file);
                }
            }
            var snapshot = await DownloadAsync(date, token);
            var ttl = snapshot is null || date >= Today.AddDays(-2)
                ? TimeSpan.FromMinutes(30) : TimeSpan.FromDays(365);
            var result = new CacheEntry(DateTimeOffset.UtcNow.Add(ttl), snapshot);
            memory.Set(key, result, result.ExpiresAt);
            try
            {
                var temporary = file + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result, Json), token);
                File.Move(temporary, file, overwrite: true);
            }
            catch (IOException exception) { logger.LogWarning(exception, "Дисковый кеш недоступен"); }
            return snapshot;
        }
        finally { gate.Release(); }
    }

    private async Task<RatesSnapshot?> DownloadAsync(DateOnly date, CancellationToken token)
    {
        var client = clients.CreateClient("cbr");
        var path = "archive/" + date.ToString("yyyy'/'MM'/'dd", System.Globalization.CultureInfo.InvariantCulture) + "/daily_json.js";
        logger.LogInformation("Запрос к источнику: {Path}", path);
        using var response = await client.GetAsync(path, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<CbrDocument>(cancellationToken: token)
            ?? throw new InvalidDataException("Пустой ответ источника.");
        var effectiveDate = DateOnly.FromDateTime(document.Date.DateTime);
        if (effectiveDate > date) return null;
        if (effectiveDate < MinimumDate || document.Valute is not { Count: > 0 })
            throw new InvalidDataException("Некорректная дата или пустой список валют.");
        var currencies = document.Valute.Values.ToList();
        if (currencies.Any(c => c.Nominal <= 0 || c.Value <= 0 || string.IsNullOrWhiteSpace(c.CharCode)))
            throw new InvalidDataException("Некорректный номинал или курс.");
        var rates = currencies.Select(c => new CurrencyRate(c.CharCode, c.Name, c.Nominal, c.Value))
            .OrderBy(c => c.Code, StringComparer.Ordinal).ToList();
        rates.Insert(0, new CurrencyRate("RUB", "Российский рубль", 1, 1));
        return new RatesSnapshot(date, effectiveDate, rates);
    }

    public sealed record CacheEntry(DateTimeOffset ExpiresAt, RatesSnapshot? Snapshot);
    private sealed record CbrDocument(DateTimeOffset Date, Dictionary<string, CbrCurrency> Valute);
    private sealed record CbrCurrency(string CharCode, string Name, int Nominal, decimal Value);
}
