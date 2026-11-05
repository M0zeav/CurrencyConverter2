using System.Net.Http.Json;

namespace CurrencyConverter.Core;

public sealed class HttpRatesService(HttpClient httpClient, Func<Uri> serverAddress) : IRatesService
{
    public async Task<RatesSnapshot> GetRatesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var dateText = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var address = new Uri(serverAddress(), "api/rates?date=" + dateText);
        using var response = await httpClient.GetAsync(address, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ConverterJsonContext.Default.RatesSnapshot, cancellationToken)
            ?? throw new InvalidDataException("Сервер вернул пустой ответ.");
    }
}
