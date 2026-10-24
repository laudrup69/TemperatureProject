using GoveeMAUI.ViewModels;

namespace GoveeMAUI.Views;

public partial class MainPage : ContentPage
{
    private readonly SettingsPage _settingsPage;

    public MainPage(MainViewModel vm, SettingsPage settingsPage)
    {
        InitializeComponent();
        BindingContext = vm;
        _settingsPage  = settingsPage;
    }

    /// <summary>
    /// Al volver de Ajustes, recoge el umbral y el margen que se hayan cambiado allí.
    /// La monitorización NO se pausa al ocultar la página: debe seguir controlando
    /// la temperatura con la app en segundo plano o en Ajustes.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        ((MainViewModel)BindingContext).ReloadSettings();
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_settingsPage);
}
