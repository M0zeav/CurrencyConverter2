using CurrencyConverter.Core;
using CurrencyConverter2.Services;
using Microsoft.Extensions.Logging;

namespace CurrencyConverter2;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        ConfigureFieldAppearance();
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddSingleton<ServerSettings>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(75) });
        builder.Services.AddSingleton<IRatesService>(services => new HttpRatesService(
            services.GetRequiredService<HttpClient>(),
            () => new Uri(services.GetRequiredService<ServerSettings>().Address)));
        builder.Services.AddSingleton<IStateStore, JsonStateStore>();
        builder.Services.AddSingleton<ConverterViewModel>();
        builder.Services.AddSingleton<MainPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
    private static void ConfigureFieldAppearance()
    {
#if ANDROID
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("PlainFields", (handler, view) =>
        {
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
        });
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("PlainFields", (handler, view) =>
        {
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
        });
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping("PlainFields", (handler, view) =>
        {
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
        });
#endif
    }
}
