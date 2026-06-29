using Grimoire.Core;
using Grimoire.Core.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Maui.Controls;

namespace Grimoire.Ui;

/// <summary>
/// Represents the top-level shell container that hosts the Grimoire workspace page and route metadata.
/// </summary>
public partial class AppShell : Shell
{
    /// <summary>
    /// Initializes the shell with localized titles and registers the primary workspace route.
    /// </summary>
    /// <param name="mainPage">The main page representing the editor and workflow surface to display in the shell.</param>
    /// <param name="localizer">The optional localizer representing UI string resources; defaults to the built-in localization factory.</param>
    public AppShell(MainPage mainPage, IStringLocalizer? localizer = null)
    {
        IStringLocalizer strings = localizer ?? new GrimoireLocalizationFactory().CreateDefault();
        InitializeComponent();
        Title = strings["Ui:Title"].Value;
        Items.Add(new ShellContent
        {
            Title = strings["Ui:Shell:Workspace"].Value,
            Content = mainPage,
            Route = nameof(MainPage),
        });
    }
}
