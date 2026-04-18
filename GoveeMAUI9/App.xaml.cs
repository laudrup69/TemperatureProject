using GoveeMAUI.Views;

namespace GoveeMAUI;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var navPage = new NavigationPage(_mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#1E1E2E"),
            BarTextColor       = Colors.White
        };

        return new Window(navPage);
    }
}
