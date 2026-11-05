using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CurrencyConverter.Core;

public sealed class ConverterViewModel : ObservableObject
{
    private readonly IRatesService ratesService;
    private readonly IStateStore stateStore;
    private CancellationTokenSource? requestCancellation;
    private long requestVersion;
    private bool suppressChanges;
    private bool initialized;
    private bool editFrom = true;
    private RatesSnapshot? snapshot;
    private DateTime selectedDate = DateTime.Today;
    private CurrencyRate? fromCurrency;
    private CurrencyRate? toCurrency;
    private string fromAmount = "1";
    private string toAmount = "";
    private string status = "Выберите дату и загрузите курсы";
    private string error = "";
    private string connectionError = "";
    private bool isBusy;
    private Task pendingSave = Task.CompletedTask;

    public ConverterViewModel(IRatesService ratesService, IStateStore stateStore)
    {
        this.ratesService = ratesService;
        this.stateStore = stateStore;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SwapCommand = new RelayCommand(Swap, () => HasRates);
    }

    public ObservableCollection<CurrencyRate> Currencies { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand SwapCommand { get; }
    public DateTime MinimumDate => new(1999, 1, 1);
    public DateTime MaximumDate => DateTime.Today;
    public bool HasRates => snapshot is not null;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error
    {
        get => error;
        private set { if (SetProperty(ref error, value)) OnPropertyChanged(nameof(HasError)); }
    }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value)) RefreshCommand.NotifyCanExecuteChanged();
        }
    }
    public DateTime SelectedDate
    {
        get => selectedDate;
        set
        {
            if (!SetProperty(ref selectedDate, value.Date) || suppressChanges) return;
            QueueSave();
            if (initialized) _ = RefreshAsync();
        }
    }
    public CurrencyRate? FromCurrency
    {
        get => fromCurrency;
        set { if (SetProperty(ref fromCurrency, value)) InputsChanged(); }
    }
    public CurrencyRate? ToCurrency
    {
        get => toCurrency;
        set { if (SetProperty(ref toCurrency, value)) InputsChanged(); }
    }
    public string FromAmount
    {
        get => fromAmount;
        set
        {
            if (!SetProperty(ref fromAmount, value ?? "") || suppressChanges) return;
            editFrom = true;
            InputsChanged();
        }
    }
    public string ToAmount
    {
        get => toAmount;
        set
        {
            if (!SetProperty(ref toAmount, value ?? "") || suppressChanges) return;
            editFrom = false;
            InputsChanged();
        }
    }

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            var saved = await stateStore.LoadAsync();
            if (saved is not null)
            {
                suppressChanges = true;
                SelectedDate = saved.SelectedDate < MinimumDate ? MinimumDate
                    : saved.SelectedDate > MaximumDate ? MaximumDate : saved.SelectedDate;
                FromAmount = saved.FromAmount;
                ToAmount = saved.ToAmount;
                editFrom = saved.EditFrom;
                if (IsValid(saved.Snapshot)) ApplySnapshot(saved.Snapshot!, saved.FromCode, saved.ToCode);
                suppressChanges = false;
            }
        }
        catch (Exception)
        {
            Error = "Не удалось прочитать сохранённое состояние. Загрузим курсы заново.";
        }
        finally { suppressChanges = false; }
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var version = ++requestVersion;
        requestCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        requestCancellation = cancellation;
        IsBusy = true;
        connectionError = "";
        Error = "";
        var date = DateOnly.FromDateTime(SelectedDate);
        try
        {
            var result = await ratesService.GetRatesAsync(date, cancellation.Token);
            if (version != requestVersion) return;
            if (!IsValid(result) || result.RequestedDate != date || result.EffectiveDate > date)
                throw new InvalidDataException("Некорректный ответ сервера.");
            ApplySnapshot(result, FromCurrency?.Code ?? "RUB", ToCurrency?.Code ?? "USD");
            Recalculate();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (version != requestVersion) return;
            connectionError = HasRates
                ? "Сервер недоступен. Используются сохранённые курсы; их дата указана ниже."
                : "Не удалось загрузить курсы. Проверьте адрес сервера и подключение.";
            Error = connectionError;
            UpdateStatus();
        }
        finally
        {
            if (version == requestVersion)
            {
                requestCancellation = null;
                IsBusy = false;
                QueueSave();
            }
        }
    }

    private static bool IsValid(RatesSnapshot? rates) => rates is not null
        && rates.Currencies is { Count: > 0 }
        && rates.Currencies.All(c => c.Nominal > 0 && c.Value > 0)
        && rates.Currencies.Select(c => c.Code).Distinct().Count() == rates.Currencies.Count;

    private void ApplySnapshot(RatesSnapshot rates, string fromCode, string toCode)
    {
        var previous = suppressChanges;
        suppressChanges = true;
        snapshot = rates;
        Currencies.Clear();
        foreach (var currency in rates.Currencies) Currencies.Add(currency);
        FromCurrency = Currencies.FirstOrDefault(c => c.Code == fromCode) ?? Currencies[0];
        ToCurrency = Currencies.FirstOrDefault(c => c.Code == toCode) ?? Currencies[0];
        suppressChanges = previous;
        OnPropertyChanged(nameof(HasRates));
        SwapCommand.NotifyCanExecuteChanged();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (snapshot is null) { Status = "Курсы ещё не загружены"; return; }
        var actual = snapshot.EffectiveDate.ToString("dd.MM.yyyy");
        Status = snapshot.EffectiveDate == DateOnly.FromDateTime(SelectedDate)
            ? $"Курс на {actual}"
            : $"Выбрано {SelectedDate:dd.MM.yyyy}. Ближайший найденный курс на {actual}";
    }

    private void InputsChanged()
    {
        if (suppressChanges) return;
        Recalculate();
        QueueSave();
    }

    private void Recalculate()
    {
        if (FromCurrency is null || ToCurrency is null) return;
        var text = (editFrom ? FromAmount : ToAmount).Trim().Replace(',', '.');
        var valid = decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount) && amount >= 0;
        string converted = "";
        if (valid)
        {
            try
            {
                var source = editFrom ? FromCurrency : ToCurrency;
                var target = editFrom ? ToCurrency : FromCurrency;
                converted = (amount * source.UnitRate / target.UnitRate)
                    .ToString("0.########", CultureInfo.GetCultureInfo("ru-RU"));
            }
            catch (OverflowException) { valid = false; }
        }
        suppressChanges = true;
        if (editFrom) ToAmount = valid ? converted : "";
        else FromAmount = valid ? converted : "";
        suppressChanges = false;
        Error = valid || string.IsNullOrWhiteSpace(text) ? connectionError : "Введите неотрицательное число без разделителей тысяч.";
    }

    private void Swap()
    {
        suppressChanges = true;
        (FromCurrency, ToCurrency) = (ToCurrency, FromCurrency);
        (FromAmount, ToAmount) = (ToAmount, FromAmount);
        suppressChanges = false;
        editFrom = !editFrom;
        QueueSave();
    }

    private ConverterState CaptureState() => new(SelectedDate, FromCurrency?.Code ?? "RUB",
        ToCurrency?.Code ?? "USD", FromAmount, ToAmount, editFrom, snapshot);

    private void QueueSave()
    {
        var state = CaptureState();
        pendingSave = SaveAfterAsync(pendingSave, state);
    }

    private async Task SaveAfterAsync(Task previous, ConverterState state)
    {
        try { await previous; await stateStore.SaveAsync(state); }
        catch (Exception) { Error = "Не удалось сохранить настройки приложения."; }
    }

    public Task SaveStateAsync()
    {
        QueueSave();
        return pendingSave;
    }
}
