namespace CurrencyConverter.Core;

public sealed record CurrencyRate(string Code, string Name, int Nominal, decimal Value)
{
    public decimal UnitRate => Value / Nominal;
    public string DisplayName => $"{Name} ({Code})";
}

public sealed record RatesSnapshot(DateOnly RequestedDate, DateOnly EffectiveDate,
    List<CurrencyRate> Currencies);

public sealed record ConverterState(DateTime SelectedDate, string FromCode, string ToCode,
    string FromAmount, string ToAmount, bool EditFrom, RatesSnapshot? Snapshot);
