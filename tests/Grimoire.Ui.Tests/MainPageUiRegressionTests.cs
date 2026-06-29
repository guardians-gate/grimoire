using System.Xml.Linq;

namespace Grimoire.Ui.Tests;

/// <summary>
/// Represents UI regression tests that enforce key MainPage XAML wiring and visual contracts.
/// </summary>
public sealed class MainPageUiRegressionTests
{
    /// <summary>
    /// Verifies that the Material Symbols font alias remains registered in MAUI startup and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void MauiProgramRegistersMaterialSymbolsFontAlias()
    {
        string source = LoadSourceText(Path.Combine("src", "Grimoire.Ui", "MauiProgram.cs"));

        Assert.Contains(
            "fonts.AddFont(\"MaterialSymbolsSharp.ttf\", \"MaterialSymbolsSharp\")",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that toolbar icon buttons use the Material Symbols font family and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void MainPageToolbarButtonsUseMaterialSymbolFont()
    {
        XDocument xaml = LoadMainPageXaml();

        XElement navigateBack = FindNamedElement(xaml, MauiNamespace + "Button", XamlNamespace + "Name", "NavigateBackButton");
        XElement navigateForward = FindNamedElement(xaml, MauiNamespace + "Button", XamlNamespace + "Name", "NavigateForwardButton");
        XElement saveButton = xaml
            .Descendants(MauiNamespace + "Button")
            .Single(static item =>
                string.Equals((string?)item.Attribute("Text"), "save", StringComparison.Ordinal) &&
                string.Equals((string?)item.Attribute("Clicked"), "OnSaveFileClicked", StringComparison.Ordinal));

        Assert.Equal("arrow_back", (string?)navigateBack.Attribute("Text"));
        Assert.Equal("MaterialSymbolsSharp", (string?)navigateBack.Attribute("FontFamily"));
        Assert.Equal("arrow_forward", (string?)navigateForward.Attribute("Text"));
        Assert.Equal("MaterialSymbolsSharp", (string?)navigateForward.Attribute("FontFamily"));
        Assert.Equal("MaterialSymbolsSharp", (string?)saveButton.Attribute("FontFamily"));
    }

    /// <summary>
    /// Verifies that dock panes and headers retain drag-and-drop gesture wiring and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void MainPageWiresDockPaneDragAndDropGestures()
    {
        XDocument xaml = LoadMainPageXaml();

        XElement workbenchGrid = FindNamedElement(xaml, MauiNamespace + "Grid", XamlNamespace + "Name", "WorkbenchGrid");
        XElement? workbenchGestures = workbenchGrid.Element(MauiNamespace + "Grid.GestureRecognizers");
        Assert.NotNull(workbenchGestures);
        Assert.Contains(
            workbenchGestures!.Elements(MauiNamespace + "DropGestureRecognizer"),
            static gesture => string.Equals((string?)gesture.Attribute("Drop"), "OnDockPaneDropped", StringComparison.Ordinal) &&
                       string.Equals((string?)gesture.Attribute("DragOver"), "OnDockPaneDragOver", StringComparison.Ordinal) &&
                       string.Equals((string?)gesture.Attribute("AllowDrop"), "True", StringComparison.Ordinal));

        AssertDockDropSurface(xaml, MauiNamespace + "Border", "ProjectDock");
        AssertDockDropSurface(xaml, MauiNamespace + "Grid", "EditorDock");
        AssertDockDropSurface(xaml, MauiNamespace + "ScrollView", "ToolsDock");
        AssertDockDropSurface(xaml, MauiNamespace + "Border", "BottomDock");

        AssertDockHeaderGestures(xaml, "ProjectDockHeader", "OnProjectDockHeaderPanUpdated", "OnProjectDockDragStarting");
        AssertDockHeaderGestures(xaml, "ToolsDockHeader", "OnToolsDockHeaderPanUpdated", "OnToolsDockDragStarting");
        AssertDockHeaderGestures(xaml, "BottomDockHeader", "OnBottomDockHeaderPanUpdated", "OnBottomDockDragStarting");

        Assert.Contains(
            xaml.Descendants(MauiNamespace + "DragGestureRecognizer"),
            static gesture => string.Equals((string?)gesture.Attribute("DragStarting"), "OnProjectEntryDragStarting", StringComparison.Ordinal));
        Assert.Contains(
            xaml.Descendants(MauiNamespace + "DropGestureRecognizer"),
            static gesture => string.Equals((string?)gesture.Attribute("Drop"), "OnProjectEntryDropped", StringComparison.Ordinal) &&
                       string.Equals((string?)gesture.Attribute("AllowDrop"), "True", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that key visual polish tokens remain intact in MainPage XAML and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void MainPageRetainsKeyVisualPolishTokens()
    {
        XDocument xaml = LoadMainPageXaml();

        XElement toolbar = xaml
            .Descendants(MauiNamespace + "Grid")
            .First(static item => string.Equals((string?)item.Attribute(XamlNamespace + "Name"), "TopToolbar", StringComparison.Ordinal));
        Assert.Equal("Transparent", (string?)toolbar.Attribute("BackgroundColor"));

        XElement heading = FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingLabel");
        Assert.Equal("NodestoCapsCondensedHeading", (string?)heading.Attribute("FontFamily"));
        Assert.Equal("#D9B76E", (string?)heading.Attribute("TextColor"));
        Assert.Equal("Grimoire - No project", (string?)heading.Attribute("Text"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineTopLabel"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineRightLabel"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineBottomLabel"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineLeftLabel"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineTopLeftLabel"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineTopRightLabel"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineBottomLeftLabel"));
        Assert.NotNull(FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "MainHeadingOutlineBottomRightLabel"));

        XElement codeEditor = FindNamedElement(xaml, MauiNamespace + "WebView", XamlNamespace + "Name", "CodeEditorWebView");
        Assert.Equal("OnCodeEditorNavigating", (string?)codeEditor.Attribute("Navigating"));
        Assert.DoesNotContain(
            xaml.Descendants(MauiNamespace + "Editor"),
            static editor => string.Equals((string?)editor.Attribute(XamlNamespace + "Name"), "SourceEditor", StringComparison.Ordinal));
        XElement sourcePaneBorder = codeEditor.Ancestors(MauiNamespace + "Border").First();
        Assert.Equal("#0D1113", (string?)sourcePaneBorder.Attribute("BackgroundColor"));

        XElement previewWebView = FindNamedElement(xaml, MauiNamespace + "WebView", XamlNamespace + "Name", "PreviewWebView");
        XElement previewPaneBorder = previewWebView.Ancestors(MauiNamespace + "Border").First();
        Assert.Equal("#F6EBD6", (string?)previewPaneBorder.Attribute("BackgroundColor"));

        AssertRailButtonDefaults(xaml, "ProjectRail");
        AssertRailButtonDefaults(xaml, "ToolsRail");
        AssertRailButtonDefaults(xaml, "BottomRail");
    }

    /// <summary>
    /// Verifies search/input legibility and editor chrome wiring regressions remain fixed and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void MainPageRepairsEditorChromeAndInputLegibility()
    {
        XDocument xaml = LoadMainPageXaml();

        XElement projectSearch = FindNamedElement(xaml, MauiNamespace + "SearchBar", XamlNamespace + "Name", "ProjectSearchBar");
        XElement loreSearch = FindNamedElement(xaml, MauiNamespace + "SearchBar", XamlNamespace + "Name", "LoreQueryEntry");
        AssertDarkSearchBar(projectSearch);
        AssertDarkSearchBar(loreSearch);

        XElement navigateBack = FindNamedElement(xaml, MauiNamespace + "Button", XamlNamespace + "Name", "NavigateBackButton");
        XElement navigateForward = FindNamedElement(xaml, MauiNamespace + "Button", XamlNamespace + "Name", "NavigateForwardButton");
        Assert.Equal("Navigate back", (string?)navigateBack.Attribute("ToolTipProperties.Text"));
        Assert.Equal("Navigate forward", (string?)navigateForward.Attribute("ToolTipProperties.Text"));
        XElement projectTreeToggle = xaml
            .Descendants(MauiNamespace + "Button")
            .Single(static item => string.Equals((string?)item.Attribute("Clicked"), "OnProjectTreeToggleClicked", StringComparison.Ordinal));
        Assert.Equal("Expand or collapse folder", (string?)projectTreeToggle.Attribute("ToolTipProperties.Text"));

        XElement busyIndicator = FindNamedElement(xaml, MauiNamespace + "ActivityIndicator", XamlNamespace + "Name", "BusyIndicator");
        XElement statusAction = FindNamedElement(xaml, MauiNamespace + "Label", XamlNamespace + "Name", "StatusActionLabel");
        Assert.Equal("0", (string?)busyIndicator.Attribute("Grid.Column"));
        Assert.Equal("1", (string?)statusAction.Attribute("Grid.Column"));
    }

    /// <summary>
    /// Asserts that a dock header exposes expected pan and drag gesture handlers and returns <see langword="void"/>.
    /// </summary>
    /// <param name="xaml">The XAML document representing the parsed MainPage markup.</param>
    /// <param name="headerName">The header name representing the target dock header element.</param>
    /// <param name="panHandler">The pan handler name representing expected pan gesture wiring.</param>
    /// <param name="dragHandler">The drag handler name representing expected drag gesture wiring.</param>
    private static void AssertDockHeaderGestures(XDocument xaml, string headerName, string panHandler, string dragHandler)
    {
        XElement header = FindNamedElement(xaml, MauiNamespace + "Grid", XamlNamespace + "Name", headerName);
        XElement? gestures = header.Element(MauiNamespace + "Grid.GestureRecognizers");
        Assert.NotNull(gestures);

        Assert.Contains(
            gestures!.Elements(MauiNamespace + "PanGestureRecognizer"),
            gesture => string.Equals((string?)gesture.Attribute("PanUpdated"), panHandler, StringComparison.Ordinal));
        Assert.Contains(
            gestures.Elements(MauiNamespace + "DragGestureRecognizer"),
            gesture => string.Equals((string?)gesture.Attribute("DragStarting"), dragHandler, StringComparison.Ordinal));
    }

    /// <summary>
    /// Asserts that a dock drop surface exposes expected drop gesture wiring and returns <see langword="void"/>.
    /// </summary>
    /// <param name="xaml">The XAML document representing the parsed MainPage markup.</param>
    /// <param name="elementName">The element name representing the dock surface type to locate.</param>
    /// <param name="elementNameValue">The element name value representing the specific dock surface instance.</param>
    private static void AssertDockDropSurface(XDocument xaml, XName elementName, string elementNameValue)
    {
        XElement surface = FindNamedElement(xaml, elementName, XamlNamespace + "Name", elementNameValue);
        XElement? gestures = surface.Element(elementName.Namespace + elementName.LocalName + ".GestureRecognizers");
        Assert.NotNull(gestures);

        Assert.Contains(
            gestures!.Elements(MauiNamespace + "DropGestureRecognizer"),
            static gesture => string.Equals((string?)gesture.Attribute("Drop"), "OnDockPaneDropped", StringComparison.Ordinal) &&
                       string.Equals((string?)gesture.Attribute("DragOver"), "OnDockPaneDragOver", StringComparison.Ordinal) &&
                       string.Equals((string?)gesture.Attribute("AllowDrop"), "True", StringComparison.Ordinal));
    }

    /// <summary>
    /// Asserts default rail button visibility and font settings and returns <see langword="void"/>.
    /// </summary>
    /// <param name="xaml">The XAML document representing the parsed MainPage markup.</param>
    /// <param name="name">The button name representing the rail control to validate.</param>
    private static void AssertRailButtonDefaults(XDocument xaml, string name)
    {
        XElement rail = FindNamedElement(xaml, MauiNamespace + "Button", XamlNamespace + "Name", name);
        Assert.Equal("MaterialSymbolsSharp", (string?)rail.Attribute("FontFamily"));
        Assert.Equal("False", (string?)rail.Attribute("IsVisible"));
    }

    /// <summary>
    /// Asserts dark-theme search bar colors and returns <see langword="void"/>.
    /// </summary>
    /// <param name="searchBar">The search bar element representing a UI search control to validate.</param>
    private static void AssertDarkSearchBar(XElement searchBar)
    {
        Assert.Equal("#F7FAF8", (string?)searchBar.Attribute("TextColor"));
        Assert.Equal("#9EAEB0", (string?)searchBar.Attribute("PlaceholderColor"));
        Assert.Equal("#D9B76E", (string?)searchBar.Attribute("CancelButtonColor"));
        Assert.Equal("#E6253138", (string?)searchBar.Attribute("BackgroundColor"));
    }

    /// <summary>
    /// Finds an element by name attribute and returns an <see cref="XElement"/> representing the matched node.
    /// </summary>
    /// <param name="container">The container representing the root search scope.</param>
    /// <param name="elementName">The element name representing which node type to search.</param>
    /// <param name="attributeName">The attribute name representing the identity attribute.</param>
    /// <param name="attributeValue">The attribute value representing the expected element name.</param>
    /// <returns>An <see cref="XElement"/> representing the matched node.</returns>
    private static XElement FindNamedElement(XContainer container, XName elementName, XName attributeName, string attributeValue)
    {
        XElement? element = container
            .Descendants(elementName)
            .SingleOrDefault(item => string.Equals((string?)item.Attribute(attributeName), attributeValue, StringComparison.Ordinal));

        Assert.NotNull(element);
        return element!;
    }

    /// <summary>
    /// Loads MainPage XAML and returns an <see cref="XDocument"/> representing the parsed page markup.
    /// </summary>
    /// <returns>An <see cref="XDocument"/> representing MainPage XAML content.</returns>
    private static XDocument LoadMainPageXaml()
    {
        return XDocument.Load(ResolveRepositoryPath(Path.Combine("src", "Grimoire.Ui", "MainPage.xaml")));
    }

    /// <summary>
    /// Loads source text from a repository-relative path and returns a <see cref="string"/> representing file contents.
    /// </summary>
    /// <param name="relativePath">The repository-relative path representing the source file to load.</param>
    /// <returns>A <see cref="string"/> representing the source file contents.</returns>
    private static string LoadSourceText(string relativePath)
    {
        return File.ReadAllText(ResolveRepositoryPath(relativePath));
    }

    /// <summary>
    /// Resolves a repository-relative path and returns a <see cref="string"/> representing an absolute filesystem path.
    /// </summary>
    /// <param name="relativePath">The repository-relative path representing the target resource.</param>
    /// <returns>A <see cref="string"/> representing the absolute path to the resource.</returns>
    private static string ResolveRepositoryPath(string relativePath)
    {
        return Path.Combine(FindRepositoryRoot(), relativePath);
    }

    /// <summary>
    /// Locates the repository root from the current test base directory and returns a <see cref="string"/> representing the root path.
    /// </summary>
    /// <returns>A <see cref="string"/> representing the repository root path.</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = current.FullName;
            if (File.Exists(Path.Combine(candidate, "AGENTS.md")) &&
                File.Exists(Path.Combine(candidate, "src", "Grimoire.Ui", "MainPage.xaml")) &&
                File.Exists(Path.Combine(candidate, "src", "Grimoire.Ui", "MauiProgram.cs")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for UI regression tests.");
    }

    /// <summary>
    /// A <see cref="XNamespace"/> representing the MAUI XML namespace used in MainPage markup assertions.
    /// </summary>
    private static readonly XNamespace MauiNamespace = "http://schemas.microsoft.com/dotnet/2021/maui";

    /// <summary>
    /// A <see cref="XNamespace"/> representing the XAML namespace used for named-element lookup.
    /// </summary>
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2009/xaml";
}
