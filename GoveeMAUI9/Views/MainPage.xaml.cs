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

    private async void OnSettingsClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_settingsPage);
}
