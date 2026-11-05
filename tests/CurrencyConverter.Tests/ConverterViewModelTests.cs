using CurrencyConverter.Core;
using Xunit;

namespace CurrencyConverter.Tests;

public sealed class ConverterViewModelTests
{
    private static readonly DateOnly Date = new(2025, 1, 20);
    private static RatesSnapshot Rates(DateOnly? requested = null) => new(requested ?? Date, Date,
        [new("RUB", "Российский рубль", 1, 1), new("USD", "Доллар США", 1, 100),
         new("JPY", "Японские иены", 100, 60)]);

    private static async Task<(ConverterViewModel Vm, MemoryStore Store)> CreateAsync()
    {
        var store = new MemoryStore();
        var vm = new ConverterViewModel(new FakeRates((date, _) => Task.FromResult(Rates(date))), store);
        vm.SelectedDate = Date.ToDateTime(TimeOnly.MinValue);
        await vm.InitializeAsync();
        return (vm, store);
    }

    [Fact]
    public async Task InitialRubAmountIsConvertedToUsd()
    {
        var (vm, _) = await CreateAsync();
        Assert.Equal("0,01", vm.ToAmount);
        Assert.Equal("RUB", vm.FromCurrency!.Code);
        Assert.True(vm.HasRates);
    }

    [Theory]
    [InlineData("250", "2,5")]
    [InlineData("250,5", "2,505")]
    [InlineData("250.5", "2,505")]
    [InlineData("0", "0")]
    public async Task EditingFirstFieldRecalculatesSecond(string text, string expected)
    {
        var (vm, _) = await CreateAsync();
        vm.FromAmount = text;
        Assert.Equal(expected, vm.ToAmount);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task EditingSecondFieldRecalculatesFirst()
    {
        var (vm, _) = await CreateAsync();
        vm.ToAmount = "2,5";
        Assert.Equal("250", vm.FromAmount);
        Assert.Equal("2,5", vm.ToAmount);
    }

    [Fact]
    public async Task NominalOfOneHundredIsTakenIntoAccount()
    {
        var (vm, _) = await CreateAsync();
        vm.FromCurrency = vm.Currencies.Single(c => c.Code == "JPY");
        vm.ToCurrency = vm.Currencies.Single(c => c.Code == "RUB");
        vm.FromAmount = "100";
        Assert.Equal("60", vm.ToAmount);
    }

    [Fact]
    public async Task CurrencyChangeRespectsLastEditedField()
    {
        var (vm, _) = await CreateAsync();
        vm.ToAmount = "2";
        vm.ToCurrency = vm.Currencies.Single(c => c.Code == "JPY");
        Assert.Equal("1,2", vm.FromAmount);
        Assert.Equal("2", vm.ToAmount);
    }

    [Fact]
    public async Task SameCurrencyKeepsTheAmount()
    {
        var (vm, _) = await CreateAsync();
        vm.ToCurrency = vm.FromCurrency;
        vm.FromAmount = "123,45";
        Assert.Equal("123,45", vm.ToAmount);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1,2.3")]
    [InlineData("999999999999999999999999999999999")]
    public async Task InvalidInputClearsResultAndShowsError(string text)
    {
        var (vm, _) = await CreateAsync();
        vm.FromAmount = text;
        Assert.Empty(vm.ToAmount);
        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task EmptyInputClearsResultWithoutError()
    {
        var (vm, _) = await CreateAsync();
        vm.FromAmount = "";
        Assert.Empty(vm.ToAmount);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task EarlierRateDateIsShownToUser()
    {
        var vm = new ConverterViewModel(new FakeRates((date, _) =>
            Task.FromResult(Rates(date) with { EffectiveDate = date.AddDays(-2) })), new MemoryStore());
        vm.SelectedDate = Date.ToDateTime(TimeOnly.MinValue);
        await vm.InitializeAsync();
        Assert.Contains("18.01.2025", vm.Status);
        Assert.Contains("Ближайший", vm.Status);
    }

    [Fact]
    public async Task StateSavesDateCurrenciesBothAmountsAndRates()
    {
        var (vm, store) = await CreateAsync();
        vm.ToAmount = "7,5";
        await vm.SaveStateAsync();
        Assert.Equal(Date, DateOnly.FromDateTime(store.State!.SelectedDate));
        Assert.Equal("750", store.State.FromAmount);
        Assert.Equal("7,5", store.State.ToAmount);
        Assert.False(store.State.EditFrom);
        Assert.NotNull(store.State.Snapshot);
    }

    [Fact]
    public async Task SavedStateWorksWhenServerIsOffline()
    {
        var store = new MemoryStore { State = new(Date.ToDateTime(TimeOnly.MinValue),
            "USD", "RUB", "2", "200", true, Rates()) };
        var vm = new ConverterViewModel(new FakeRates((_, _) => throw new HttpRequestException()), store);
        await vm.InitializeAsync();
        Assert.Equal("USD", vm.FromCurrency!.Code);
        Assert.Equal("2", vm.FromAmount);
        Assert.Equal("200", vm.ToAmount);
        Assert.True(vm.HasRates);
        Assert.Contains("сохранённые", vm.Error);
        vm.FromAmount = "3";
        Assert.Equal("300", vm.ToAmount);
        Assert.Contains("сохранённые", vm.Error);
    }

    [Fact]
    public async Task FirstLaunchOfflineDoesNotInventRates()
    {
        var vm = new ConverterViewModel(new FakeRates((_, _) => throw new HttpRequestException()), new MemoryStore());
        await vm.InitializeAsync();
        Assert.False(vm.HasRates);
        Assert.True(vm.HasError);
        Assert.False(vm.SwapCommand.CanExecute(null));
        Assert.True(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task InvalidRatesAreRejected()
    {
        var vm = new ConverterViewModel(new FakeRates((date, _) => Task.FromResult(
            Rates(date) with { Currencies = [new("USD", "Dollar", 0, 100)] })), new MemoryStore());
        await vm.InitializeAsync();
        Assert.False(vm.HasRates);
        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task SwapChangesDirectionAndRetainsConversion()
    {
        var (vm, _) = await CreateAsync();
        vm.FromAmount = "200";
        vm.SwapCommand.Execute(null);
        Assert.Equal("USD", vm.FromCurrency!.Code);
        Assert.Equal("2", vm.FromAmount);
        Assert.Equal("200", vm.ToAmount);
    }

    [Fact]
    public async Task SwapDoesNotIntroduceRoundingDrift()
    {
        var (vm, _) = await CreateAsync();
        vm.ToCurrency = vm.Currencies.Single(c => c.Code == "JPY");
        Assert.Equal("1,66666667", vm.ToAmount);
        vm.SwapCommand.Execute(null);
        Assert.Equal("1,66666667", vm.FromAmount);
        Assert.Equal("1", vm.ToAmount);
    }

    [Fact]
    public async Task LateResponseCannotOverwriteNewerDate()
    {
        var first = new TaskCompletionSource<RatesSnapshot>();
        var second = new TaskCompletionSource<RatesSnapshot>();
        var calls = 0;
        var vm = new ConverterViewModel(new FakeRates((date, _) =>
            ++calls == 1 ? first.Task : second.Task), new MemoryStore());
        vm.SelectedDate = Date.ToDateTime(TimeOnly.MinValue);
        var oldRequest = vm.RefreshAsync();
        vm.SelectedDate = Date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var newRequest = vm.RefreshAsync();
        second.SetResult(Rates(Date.AddDays(1)) with { EffectiveDate = Date.AddDays(1) });
        await newRequest;
        first.SetResult(Rates(Date));
        await oldRequest;
        Assert.Contains("21.01.2025", vm.Status);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task BusyRefreshCommandIsDisabledThenEnabled()
    {
        var completion = new TaskCompletionSource<RatesSnapshot>();
        var vm = new ConverterViewModel(new FakeRates((_, _) => completion.Task), new MemoryStore());
        vm.SelectedDate = Date.ToDateTime(TimeOnly.MinValue);
        var task = vm.RefreshAsync();
        Assert.False(vm.RefreshCommand.CanExecute(null));
        completion.SetResult(Rates());
        await task;
        Assert.True(vm.RefreshCommand.CanExecute(null));
    }

    private sealed class FakeRates(Func<DateOnly, CancellationToken, Task<RatesSnapshot>> get) : IRatesService
    {
        public Task<RatesSnapshot> GetRatesAsync(DateOnly date, CancellationToken cancellationToken) => get(date, cancellationToken);
    }
    private sealed class MemoryStore : IStateStore
    {
        public ConverterState? State { get; set; }
        public Task<ConverterState?> LoadAsync() => Task.FromResult(State);
        public Task SaveAsync(ConverterState state) { State = state; return Task.CompletedTask; }
    }
}
