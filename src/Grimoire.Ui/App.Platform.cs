using System.Diagnostics.CodeAnalysis;

namespace Grimoire.Ui;

/// <summary>
/// Represents platform fallback hooks for targets that do not need custom window chrome behavior.
/// </summary>
public partial class App
{
#if !WINDOWS && !MACCATALYST
    /// <summary>
    /// Applies platform chrome customization for non-Windows and non-MacCatalyst targets.
    /// </summary>
    /// <param name="window">The window representing the native host surface to configure.</param>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Platform chrome is an instance partial hook shared across MAUI targets.")]
    partial void ConfigurePlatformChrome(Window window)
    {
    }

    /// <summary>
    /// Performs platform chrome initialization for targets that do not require additional setup.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Platform chrome initialization is an instance partial hook shared across MAUI targets.")]
    partial void InitializePlatformChrome()
    {
    }
#endif
}
