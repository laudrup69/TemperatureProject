using GoveeMAUI.Views;

namespace GoveeMAUI;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    // Se llama de nuevo cada vez que Android recrea la pantalla (p. ej. al reabrir la
    // app tras cerrarla desde recientes con la monitorización aún en marcha). Por eso
    // la página se crea nueva cada vez: reutilizar una ligada a la ventana anterior
    // falla. El estado vive en los ViewModels y servicios, que son singletons.
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var navPage = new NavigationPage(_services.GetRequiredService<MainPage>())
        {
            BarBackgroundColor = Color.FromArgb("#1E1E2E"),
            BarTextColor       = Colors.White
        };

        return new Window(navPage);
    }
}
