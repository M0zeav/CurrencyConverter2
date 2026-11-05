using CurrencyConverter.Core;

namespace CurrencyConverter2;

public partial class App : Application
{
    private readonly MainPage page;
    private readonly ConverterViewModel viewModel;
    public App(MainPage page, ConverterViewModel viewModel)
    {
        InitializeComponent();
        this.page = page;
        this.viewModel = viewModel;
    }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(page);
        window.Stopped += async (_, _) => await viewModel.SaveStateAsync();
        return window;
    }
}
