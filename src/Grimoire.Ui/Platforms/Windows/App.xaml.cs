using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Grimoire.Ui.WinUI;

/// <summary>
/// Represents the WinUI bootstrap application that hosts the MAUI runtime on Windows.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>
    /// Initializes the WinUI app host and loads generated XAML resources required for startup.
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Creates the MAUI app host for the Windows process and returns a <see cref="MauiApp"/> representing the configured application graph.
    /// </summary>
    /// <returns>A <see cref="MauiApp"/> representing the initialized Grimoire UI application.</returns>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
