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

        // HttpClient Factory (maneja reutilización de conexiones y ciclo de vida)
        builder.Services.AddHttpClient("govee")
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://openapi.api.govee.com");
                client.Timeout = TimeSpan.FromSeconds(15);
            });

        builder.Services.AddHttpClient("meross")
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://iotx-eu.meross.com");
                client.DefaultRequestHeaders.Add("vender", "Meross");
                client.DefaultRequestHeaders.Add("AppVersion", "1.9.0");
                client.DefaultRequestHeaders.Add("AppLanguage", "en");
                client.DefaultRequestHeaders.Add("User-Agent", "okhttp/3.6.0");
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
