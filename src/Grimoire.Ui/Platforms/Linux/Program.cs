#if LINUX_GTK
using Platform.Maui.Linux.Gtk4.Platform;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Grimoire.Ui;
#pragma warning restore IDE0130

/// <summary>
/// Represents the Linux GTK entry-point host that boots the MAUI application.
/// </summary>
internal sealed class Program : GtkMauiApplication
{
    /// <summary>
    /// Creates the MAUI app graph for the Linux host and returns a <see cref="MauiApp"/> representing the configured application services and UI.
    /// </summary>
    /// <returns>A <see cref="MauiApp"/> representing the initialized MAUI application.</returns>
    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }

    /// <summary>
    /// Starts the Linux MAUI process and enters the GTK message loop.
    /// </summary>
    /// <param name="args">Command-line arguments representing process startup options.</param>
    public static void Main(string[] args)
    {
        Program app = new();
        app.Run(args);
    }
}
#endif
