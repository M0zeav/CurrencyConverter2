namespace CurrencyConverter.Core;

public interface IRatesService
{
    Task<RatesSnapshot> GetRatesAsync(DateOnly date, CancellationToken cancellationToken);
}

public interface IStateStore
{
    Task<ConverterState?> LoadAsync();
    Task SaveAsync(ConverterState state);
}
