using System.Globalization;
using CurrencyConverter.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("cbr", client =>
{
    client.BaseAddress = new Uri("https://www.cbr-xml-daily.ru/");
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("StudentCurrencyConverter/1.0");
});
builder.Services.AddSingleton<CbrRatesProvider>();
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/rates", async (string? date, CbrRatesProvider provider,
    HttpContext context, ILogger<Program> logger) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var requested))
        return Results.BadRequest(new { error = "Дата должна иметь формат yyyy-MM-dd." });
    if (requested < CbrRatesProvider.MinimumDate || requested > CbrRatesProvider.Today)
        return Results.BadRequest(new { error = "Выберите дату с 01.01.1999 по сегодняшний день (Москва)." });
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(60));
    try
    {
        return Results.Ok(await provider.GetRatesAsync(requested, timeout.Token));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "В пределах 31 дня назад курсы не найдены." });
    }
    catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
    {
        return Results.Problem("Превышено время ожидания источника курсов.", statusCode: 504);
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
    {
        logger.LogWarning(exception, "Не удалось получить курсы за {Date}", requested);
        return Results.Problem("Источник курсов временно недоступен.", statusCode: 502);
    }
});
app.Run();

public partial class Program;
