#if WINDOWS
using CommunityToolkit.Maui;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
#endif
using Grimoire.Core;
using Grimoire.Core.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Handlers;
#if MACCATALYST
using Foundation;
using UIKit;
#endif
#if !(WINDOWS || MACCATALYST)
using Platform.Maui.Linux.Gtk4.Hosting;
#endif

namespace Grimoire.Ui;

/// <summary>
/// Represents the MAUI startup builder that wires fonts, dependency injection, logging, and platform UI mappings.
/// </summary>
internal sealed class MauiProgram
{
    /// <summary>
    /// Creates the application's configured MAUI host and returns a <see cref="MauiApp"/> representing the runtime service container and UI setup.
    /// </summary>
    /// <returns>A <see cref="MauiApp"/> representing the configured Grimoire UI application.</returns>
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        UiLogFeed uiLogFeed = new();
#if WINDOWS
        builder.UseMauiApp<App>().UseMauiCommunityToolkit();
#elif MACCATALYST
        builder.UseMauiApp<App>();
#else
        builder.UseMauiAppLinuxGtk4<App>();
#endif

        builder.ConfigureFonts(static fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSans");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            fonts.AddFont("LibreBaskerville-Regular.ttf", "LibreBaskervilleBody");
            fonts.AddFont("LibreBaskerville-Bold.ttf", "LibreBaskervilleBodyBold");
            fonts.AddFont("MaterialSymbolsSharp.ttf", "MaterialSymbolsSharp");
            fonts.AddFont("NodestoCapsCondensed-Regular.otf", "NodestoCapsCondensedHeading");
            fonts.AddFont("NodestoCapsCondensed-Bold.otf", "NodestoCapsCondensedHeadingBold");
        });

        builder.Services.AddSingleton<IStringLocalizer>(_ => new GrimoireLocalizationFactory().CreateDefault());
        builder.Services.AddSingleton(uiLogFeed);
        builder.Services.AddSingleton<ILoggerProvider>(_ => new UiLogFeedLoggerProvider(uiLogFeed));
        builder.Services.AddSingleton<IGrimoireUiWorkflowService, GrimoireUiWorkflowService>();
        builder.Services.AddSingleton<GrimoireUiWorkflowController>();
        builder.Services.AddSingleton<MainPage>();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        ConfigureSearchBarContrast();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Configures platform-specific search bar styling to maintain readable contrast in the app's dark visual theme.
    /// </summary>
    private static void ConfigureSearchBarContrast()
    {
        SearchBarHandler.Mapper.AppendToMapping("GrimoireSearchBarContrast", static (handler, _) =>
        {
#if WINDOWS
            if (handler.PlatformView is AutoSuggestBox searchBox)
            {
                searchBox.Foreground = new SolidColorBrush(Color.FromArgb(255, 247, 250, 248));
                searchBox.PlaceholderForeground = new SolidColorBrush(Color.FromArgb(255, 158, 174, 176));
                searchBox.Background = new SolidColorBrush(Color.FromArgb(230, 37, 49, 56));
            }
#elif MACCATALYST
            if (handler.PlatformView is UISearchBar searchBar)
            {
                UITextField searchField = searchBar.SearchTextField;
                searchField.TextColor = UIColor.FromRGB(247, 250, 248);
                searchField.BackgroundColor = UIColor.FromRGBA(37, 49, 56, 230);
                searchField.TintColor = UIColor.FromRGB(217, 183, 110);
                searchBar.TintColor = UIColor.FromRGB(217, 183, 110);
                searchField.AttributedPlaceholder = new NSAttributedString(
                    searchField.Placeholder ?? string.Empty,
                    foregroundColor: UIColor.FromRGB(158, 174, 176));
            }
#endif
        });
    }
}
