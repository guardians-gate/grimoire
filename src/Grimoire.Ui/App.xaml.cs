using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Grimoire.Ui;

/// <summary>
/// Represents the root MAUI application object that initializes shared resources and creates the main shell window.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes the UI application and executes platform-specific chrome initialization hooks.
    /// </summary>
    public App()
    {
        InitializeComponent();
        InitializePlatformChrome();
    }

    /// <summary>
    /// Creates the first top-level <see cref="Window"/> for the application and returns a <see cref="Window"/> representing the shell-hosted main workspace.
    /// </summary>
    /// <param name="activationState">The activation state representing how the app launch was requested.</param>
    /// <returns>A <see cref="Window"/> representing the initialized UI host for the main page.</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        MainPage mainPage = Handler?.MauiContext?.Services.GetService<MainPage>() ?? new MainPage();
        Window window = new(new AppShell(mainPage));
        window.HandlerChanged += (_, _) => ConfigurePlatformChrome(window);
        window.Created += (_, _) => ConfigurePlatformChrome(window);
        return window;
    }

    /// <summary>
    /// Applies platform-specific window chrome customization for a created MAUI window.
    /// </summary>
    /// <param name="window">The window representing the native host that should be styled.</param>
    partial void ConfigurePlatformChrome(Window window);

    /// <summary>
    /// Performs one-time platform chrome initialization that is required before the first window is presented.
    /// </summary>
    partial void InitializePlatformChrome();
}
