using Microsoft.Extensions.Logging;
using GoveeMAUI.Services;
using GoveeMAUI.ViewModels;
using GoveeMAUI.Views;

namespace GoveeMAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf",   "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf",  "OpenSansSemibold");
            });

        // Servicios (singleton: una única instancia en toda la app)
        builder.Services.AddSingleton<GoveeService>();
        builder.Services.AddSingleton<MerossService>();
        builder.Services.AddSingleton<MonitorService>();

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Páginas
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
