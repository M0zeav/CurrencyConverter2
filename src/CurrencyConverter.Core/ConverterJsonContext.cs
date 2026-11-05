using System.Text.Json.Serialization;

namespace CurrencyConverter.Core;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RatesSnapshot))]
[JsonSerializable(typeof(ConverterState))]
public partial class ConverterJsonContext : JsonSerializerContext;
