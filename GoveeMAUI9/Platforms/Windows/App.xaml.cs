using Microsoft.UI.Xaml;

namespace GoveeMAUI.WinUI;

public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
