using Foundation;

namespace Grimoire.Ui;

/// <summary>
/// Represents the UIKit delegate bridge that creates the shared MAUI application for Mac Catalyst.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <summary>
    /// Builds the MAUI app graph for this platform host and returns a <see cref="MauiApp"/> representing the configured application services and pages.
    /// </summary>
    /// <returns>A <see cref="MauiApp"/> representing the initialized Grimoire UI app.</returns>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
