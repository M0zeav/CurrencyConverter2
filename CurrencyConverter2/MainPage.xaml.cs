using CurrencyConverter.Core;
using CurrencyConverter2.Services;

namespace CurrencyConverter2;

public partial class MainPage : ContentPage
{
    private readonly ConverterViewModel viewModel;
    private readonly ServerSettings settings;
    public MainPage(ConverterViewModel viewModel, ServerSettings settings)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.settings = settings;
        BindingContext = viewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }
    private void OnCalendarClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (RateDatePicker.Handler?.PlatformView is Android.Views.View native) native.PerformClick();
#elif WINDOWS
        if (RateDatePicker.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.CalendarDatePicker native) native.IsCalendarOpen = true;
#else
        RateDatePicker.Focus();
#endif
    }
    private void OnFromCurrencyTapped(object? sender, TappedEventArgs e) => OpenCurrencyPicker(FromCurrencyPicker);
    private void OnToCurrencyTapped(object? sender, TappedEventArgs e) => OpenCurrencyPicker(ToCurrencyPicker);
    private static void OpenCurrencyPicker(Picker picker)
    {
#if ANDROID
        if (picker.Handler?.PlatformView is Android.Views.View native) native.PerformClick();
#elif WINDOWS
        if (picker.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ComboBox native) native.IsDropDownOpen = true;
#else
        picker.Focus();
#endif
    }
    private async void OnMenuClicked(object? sender, EventArgs e)
    {
        var action = await DisplayActionSheet("Конвертер валют", "Отмена", null,
            "Обновить курсы", "Поменять валюты", "Адрес сервера");
        if (action == "Обновить курсы") await viewModel.RefreshCommand.ExecuteAsync(null);
        else if (action == "Поменять валюты" && viewModel.SwapCommand.CanExecute(null))
            viewModel.SwapCommand.Execute(null);
        else if (action == "Адрес сервера")
        {
            var address = await DisplayPromptAsync("Адрес сервера", "Введите адрес своего сервера курсов",
                "Сохранить", "Отмена", initialValue: settings.Address, keyboard: Keyboard.Url);
            if (address is null) return;
            if (!settings.TrySave(address))
            {
                await DisplayAlert("Адрес сервера", "Введите HTTPS-адрес. Для localhost и 10.0.2.2 разрешён HTTP.", "OK");
                return;
            }
            await viewModel.RefreshCommand.ExecuteAsync(null);
        }
    }
}
