using GoveeMAUI.ViewModels;

namespace GoveeMAUI.Views;

public partial class MainPage : ContentPage
{
    private readonly SettingsPage _settingsPage;
    private bool _wasMonitoringBeforePause = false;

    public MainPage(MainViewModel vm, SettingsPage settingsPage)
    {
        InitializeComponent();
        BindingContext = vm;
        _settingsPage  = settingsPage;
    }

    /// <summary>
    /// Se dispara cuando la página es mostrada o cuando el app vuelve del background.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        System.Diagnostics.Debug.WriteLine("[MainPage] 📲 OnAppearing - App regresó al foreground");
        
        try
        {
            var vm = (MainViewModel?)BindingContext;
            if (vm != null && _wasMonitoringBeforePause && !vm.IsRunning)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    System.Diagnostics.Debug.WriteLine("[MainPage] ▶️ Reanudando monitorización...");
                    await vm.ToggleMonitorCommand.ExecuteAsync(null);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Error en OnAppearing: {ex.Message}");
        }
    }

    /// <summary>
    /// Se dispara cuando la página es ocultada o cuando el app va al background.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        System.Diagnostics.Debug.WriteLine("[MainPage] 📴 OnDisappearing - App entró al background");
        
        try
        {
            var vm = (MainViewModel?)BindingContext;
            if (vm != null)
            {
                _wasMonitoringBeforePause = vm.IsRunning;
                if (_wasMonitoringBeforePause)
                {
                    System.Diagnostics.Debug.WriteLine("[MainPage] ⏸️ Pausando monitorización...");
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await vm.ToggleMonitorCommand.ExecuteAsync(null);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] ❌ Error en OnDisappearing: {ex.Message}");
        }
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_settingsPage);
}
