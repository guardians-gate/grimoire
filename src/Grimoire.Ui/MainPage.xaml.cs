using System.Globalization;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net.Http;
#if WINDOWS
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
#endif
using Grimoire.Core;
using Grimoire.Core.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Grimoire.Ui;

/// <summary>
/// Implements the main Grimoire desktop workbench page.
/// </summary>
public partial class MainPage : ContentPage
{
    /// <summary>
    /// Coordinates UI workflow operations.
    /// </summary>
    private readonly GrimoireUiWorkflowController _controller;
    /// <summary>
    /// Resolves localized UI strings.
    /// </summary>
    private readonly IStringLocalizer _localizer;
    /// <summary>
    /// Writes structured diagnostics for page event handling.
    /// </summary>
    private readonly ILogger<MainPage> _logger;
    /// <summary>
    /// Receives UI log entries from background operations.
    /// </summary>
    private readonly UiLogFeed _logFeed;
    /// <summary>
    /// Tracks open tabs and navigation history for the editor.
    /// </summary>
    private readonly EditorWorkspaceState _editorWorkspace = new();
    /// <summary>
    /// Tracks expanded directories in the project tree.
    /// </summary>
    private readonly HashSet<string> _expandedDirectories = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Caches all project items loaded from the controller.
    /// </summary>
    private List<ProjectFileItem> _allProjectItems = [];
    /// <summary>
    /// Stores the currently selected project tree row.
    /// </summary>
    private ProjectTreeRow? _selectedProjectRow;
    /// <summary>
    /// Stores the active editor file path.
    /// </summary>
    private string? _currentFilePath;
    /// <summary>
    /// Stores the active project root path.
    /// </summary>
    private string? _currentProjectPath;
    /// <summary>
    /// Stores the project-relative clipboard path for copy/cut operations.
    /// </summary>
    private string? _projectClipboardPath;
    /// <summary>
    /// Indicates whether the project clipboard is using cut mode.
    /// </summary>
    private bool _projectClipboardIsCut;
    /// <summary>
    /// Indicates whether a file load is currently in progress.
    /// </summary>
    private bool _loadingFile;
    /// <summary>
    /// Indicates whether the active editor document has unsaved changes.
    /// </summary>
    private bool _hasUnsavedChanges;
    /// <summary>
    /// Stores the most recently requested build target.
    /// </summary>
    private BuildTarget _lastBuildTarget = BuildTarget.All;
    /// <summary>
    /// Stores the most recently used build output paths.
    /// </summary>
    private BuildOutputPaths _lastBuildOutputs = new("site", "foundry.db", "book.pdf");
    /// <summary>
    /// Stores the current width of the project dock pane.
    /// </summary>
    private double _projectPaneWidth = 300;
    /// <summary>
    /// Stores the current width of the tools dock pane.
    /// </summary>
    private double _toolsPaneWidth = 360;
    /// <summary>
    /// Stores the current height of the bottom dock pane.
    /// </summary>
    private double _bottomPaneHeight = 220;
    /// <summary>
    /// Stores the starting project pane width while resizing.
    /// </summary>
    private double _projectSplitterStartWidth;
    /// <summary>
    /// Stores the starting tools pane width while resizing.
    /// </summary>
    private double _toolsSplitterStartWidth;
    /// <summary>
    /// Stores the starting bottom pane height while resizing.
    /// </summary>
    private double _bottomSplitterStartHeight;
    /// <summary>
    /// Stores the currently dragged project tree path.
    /// </summary>
    private string? _draggedProjectPath;
    /// <summary>
    /// Stores the pending build target while prompting for output paths.
    /// </summary>
    private BuildTarget _pendingBuildTarget;
    /// <summary>
    /// Completes when the build outputs dialog is confirmed or canceled.
    /// </summary>
    private TaskCompletionSource<BuildOutputPaths?>? _buildOutputsCompletionSource;
    /// <summary>
    /// Stores the active settings editor session when editing settings YAML.
    /// </summary>
    private SettingsEditorSession? _settingsEditorSession;
    /// <summary>
    /// Indicates whether syntax preview mode is enabled for the source pane.
    /// </summary>
    private bool _syntaxViewEnabled;
    /// <summary>
    /// Stores the latest source editor text snapshot.
    /// </summary>
    private string _sourceEditorText = string.Empty;
    /// <summary>
    /// Stores the latest source editor line number.
    /// </summary>
    private int _sourceEditorLineNumber = 1;
    /// <summary>
    /// Maps normalized preview navigation keys to project-relative source paths.
    /// </summary>
    private Dictionary<string, string> _previewNavigationTargets = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Indicates whether tab selection synchronization is in progress.
    /// </summary>
    private bool _updatingEditorTabSelection;
    /// <summary>
    /// Stores the name of the currently running busy action.
    /// </summary>
    private string _currentBusyAction = "Ready";
    /// <summary>
    /// Stores the pane currently being dragged for docking operations.
    /// </summary>
    private DockPaneKind? _draggedDockPane;
    /// <summary>
    /// Defines the drag data key used for dock pane moves.
    /// </summary>
    private const string DockPaneDragDataKey = "grimoire-dock-pane";
    /// <summary>
    /// Defines the URI scheme used by source editor WebView messages.
    /// </summary>
    private const string SourceEditorMessageScheme = "grimoire-editor";
    /// <summary>
    /// Defines the payload type for source editor text interop messages.
    /// </summary>
    private const string SourceEditorInteropPayloadType = "source-editor-text";

    /// <summary>
    /// Gets a <see cref="Regex"/> representing JSON object key matching.
    /// </summary>
    [GeneratedRegex("""^(\s*)"(?<key>(?:\\.|[^"\\])*)"(\s*:)(?<rest>.*)$""", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex JsonKeyRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing JSON string literal matching.
    /// </summary>
    [GeneratedRegex("""
                    "(?:\\.|[^"\\])*"
                    """, RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex JsonStringRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing numeric token matching.
    /// </summary>
    [GeneratedRegex(@"-?\b\d+(\.\d+)?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing boolean and null token matching.
    /// </summary>
    [GeneratedRegex(@"\b(true|false|null)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BoolNullRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing Markdown inline code spans.
    /// </summary>
    [GeneratedRegex(@"`[^`]+`", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownInlineCodeRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing Markdown link syntax.
    /// </summary>
    [GeneratedRegex(@"(?<bang>!?)\[(?<text>[^\]]*)\]\((?<url>[^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing macro page-count tokens.
    /// </summary>
    [GeneratedRegex(@"\{\{(?<scope>macro)\.pageCount\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownProjectPageCountTokenRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing macro see-also tokens.
    /// </summary>
    [GeneratedRegex(@"\{\{(?<scope>macro)\.seeAlso:(?<topic>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownProjectSeeAlsoTokenRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing macro name tokens.
    /// </summary>
    [GeneratedRegex(@"\{\{macro\.(?<name>[A-Za-z0-9_.-]+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownMacroTokenRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing entity lookup tokens.
    /// </summary>
    [GeneratedRegex(@"\{\{%(?<name>[^}:]+)(?::(?<property>[^}]+))?\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownEntityLookupTokenRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing file substitution tokens.
    /// </summary>
    [GeneratedRegex(@"\{\{@(?<path>[^}:]+)(?::(?<property>[^}]+))?\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownFileSubstitutionTokenRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing inline preview query flags.
    /// </summary>
    [GeneratedRegex(@"(?<prefix>[\?&;])(?<flag>inline)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownInlineFlagRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing fraction-style progress markers.
    /// </summary>
    [GeneratedRegex(@"\b(?<index>\d+)/(?<total>\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ProgressFractionRegex { get; }
    /// <summary>
    /// Defines available page-size choices for print settings.
    /// </summary>
    private static readonly ImmutableArray<string> PageSizeChoices = ["letter", "legal", "tabloid", "a3", "a4", "a5"];
    /// <summary>
    /// Defines editable fields for global settings.
    /// </summary>
    private static readonly ImmutableArray<SettingsFieldDefinition> GlobalSettingsFields =
    [
        new("project.title", "Title", SettingFieldType.Text, "Project"),
        new("project.author", "Author", SettingFieldType.Text, "Project"),
        new("project.authors", "Authors (comma-separated)", SettingFieldType.List, "Project"),
        new("project.description", "Description", SettingFieldType.Multiline, "Project"),
        new("project.copyright", "Copyright", SettingFieldType.Text, "Project"),
        new("project.license", "License", SettingFieldType.Text, "Project"),
        new("project.jumbotron", "Jumbotron image", SettingFieldType.Text, "Project"),
        new("compiler.dictionary.enabled", "Enable dictionary", SettingFieldType.Boolean, "Compiler: Dictionary", DefaultValue: "false"),
        new("compiler.dictionary.unreferenced", "Include unreferenced entries", SettingFieldType.Boolean, "Compiler: Dictionary", DefaultValue: "false"),
        new("compiler.dictionary.shadowReferences", "Shadow references (one per line)", SettingFieldType.List, "Compiler: Dictionary"),
        new("compiler.engine", "Engine", SettingFieldType.Text, "Compiler: Engine"),
        new("compiler.print.columns", "Print columns", SettingFieldType.Integer, "Compiler: Print", DefaultValue: "2"),
        new("compiler.print.pageSize", "Print page size", SettingFieldType.Choice, "Compiler: Print", DefaultValue: "letter", Choices: PageSizeChoices),
    ];
    /// <summary>
    /// Defines editable fields for font settings.
    /// </summary>
    private static readonly ImmutableArray<SettingsFieldDefinition> FontSettingsFields =
    [
        new("fonts.headings.family", "Heading font family", SettingFieldType.Text, "Fonts", DefaultValue: "Nodesto Caps Condensed"),
        new("fonts.headings.color", "Heading color", SettingFieldType.Text, "Fonts", DefaultValue: "#7a0d0d"),
        new("fonts.body.family", "Body font family", SettingFieldType.Text, "Fonts", DefaultValue: "Libre Baskerville"),
    ];
    /// <summary>
    /// Defines editable fields for screen output settings.
    /// </summary>
    private static readonly ImmutableArray<SettingsFieldDefinition> ScreenSettingsFields =
    [
        new("compiler.screen.columns", "Screen columns", SettingFieldType.Integer, "Compiler: Screen", DefaultValue: "1"),
        new("compiler.screen.pageLevelToc", "Page-level table of contents", SettingFieldType.Boolean, "Compiler: Screen", DefaultValue: "true"),
    ];
    /// <summary>
    /// Defines editable fields for Foundry output settings.
    /// </summary>
    private static readonly ImmutableArray<SettingsFieldDefinition> FoundrySettingsFields =
    [
        new("foundry.packName", "Foundry pack name", SettingFieldType.Text, "Foundry"),
    ];

    /// <summary>
    /// Logs an async event-handler failure.
    /// </summary>
    /// <param name="logger">The logger used for output.</param>
    /// <param name="handlerName">The failing handler name.</param>
    /// <param name="exception">The exception that was thrown.</param>
    [LoggerMessage(EventId = 5100, Level = LogLevel.Error, Message = "MainPage async event handler failed: {handlerName}.")]
    private static partial void MainPageAsyncEventHandlerFailed(ILogger logger, string handlerName, Exception exception);

    /// <summary>
    /// Initializes a new instance of the <see cref="MainPage"/> class with default dependencies.
    /// </summary>
    public MainPage()
        : this(new GrimoireUiWorkflowController(new GrimoireUiWorkflowService()), null, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainPage"/> class with explicit dependencies.
    /// </summary>
    /// <param name="controller">The workflow controller used by the page.</param>
    /// <param name="localizer">The optional localizer override.</param>
    /// <param name="logFeed">The optional UI log feed override.</param>
    /// <param name="logger">The optional logger override.</param>
    public MainPage(
        GrimoireUiWorkflowController controller,
        IStringLocalizer? localizer = null,
        UiLogFeed? logFeed = null,
        ILogger<MainPage>? logger = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _localizer = localizer ?? new GrimoireLocalizationFactory().CreateDefault();
        _logger = logger ?? NullLogger<MainPage>.Instance;
        _logFeed = logFeed ?? new UiLogFeed();
        InitializeComponent();
        Loaded += OnMainPageLoaded;
        HandlerChanged += OnMainPageHandlerChanged;
        EditorTabsView.ItemsSource = _editorWorkspace.OpenTabs;
        _logFeed.EntryWritten += OnUiLogEntryWritten;
        ApplyLocalization();
        UpdateEditorTabsSelection();
        UpdateNavigationButtons();
        UpdateWorkbenchLayout();
    }

    /// <summary>
    /// Logs and surfaces an async UI event-handler failure.
    /// </summary>
    /// <param name="handlerName">The failing handler name.</param>
    /// <param name="exception">The thrown exception.</param>
    private void LogAsyncEventHandlerFailure(string handlerName, Exception exception)
    {
        MainPageAsyncEventHandlerFailed(_logger, handlerName, exception);
        AppendLog($"Error in {handlerName}: {exception.Message}");
    }

    /// <summary>
    /// Determines whether an exception is expected and safely handled in UI event flows.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns><see langword="true"/> when the exception should be handled locally.</returns>
    private static bool IsHandledUiEventException(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or TimeoutException
            or HttpRequestException
            or JsonException
            or FormatException
            or FeatureNotSupportedException
            or PermissionException
            or System.ComponentModel.Win32Exception;
    }

    /// <summary>
    /// Handles the page loaded lifecycle event.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnMainPageLoaded(object? sender, EventArgs e)
    {
        TryConfigureTopToolbarChrome();
    }

    /// <summary>
    /// Handles platform handler changes for the page.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnMainPageHandlerChanged(object? sender, EventArgs e)
    {
        TryConfigureTopToolbarChrome();
    }

    /// <summary>
    /// Applies platform-specific title bar wiring for the top toolbar.
    /// </summary>
    private void TryConfigureTopToolbarChrome()
    {
        _ = TopToolbar;
#if WINDOWS
        if (Window?.Handler?.PlatformView is MauiWinUIWindow nativeWindow &&
            TopToolbar.Handler?.PlatformView is UIElement toolbarElement)
        {
            nativeWindow.SetTitleBar(toolbarElement);
        }
#endif
    }

    /// <summary>
    /// Applies localized text values to static UI controls.
    /// </summary>
    private void ApplyLocalization()
    {
        Title = Text("Ui:Title");
        DndbHeadingLabel.Text = Text("Ui:Dndb:Heading");
        CobaltTokenEntry.Placeholder = Text("Ui:Dndb:CobaltPlaceholder");
        PatreonKeyEntry.Placeholder = Text("Ui:Dndb:PatreonPlaceholder");
        CampaignEntry.Placeholder = Text("Ui:Dndb:CampaignPlaceholder");
        ItemFilterEntry.Placeholder = Text("Ui:Dndb:ItemPlaceholder");
        SpellFilterEntry.Placeholder = Text("Ui:Dndb:SpellPlaceholder");
        CreatureFilterEntry.Placeholder = Text("Ui:Dndb:CreaturePlaceholder");
        CharacterSheetFilterEntry.Placeholder = Text("Ui:Dndb:CharacterSheetPlaceholder");
        HomebrewLabel.Text = Text("Ui:Dndb:Homebrew");
        UpgradeLabel.Text = Text("Ui:Dndb:Upgrade");
        DndbButton.Text = Text("Ui:Dndb:Button");
        LoreSearchButton.Text = Text("Ui:Lore:Button");
        UpdateBuildButtonText();
        UpdateProjectTitle();
    }

    /// <summary>
    /// Handles creating a new project from scaffold templates.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnNewProjectMenuClicked(object? sender, EventArgs e)
    {
        try
        {
            string? targetPath = await PickProjectDirectoryAsync(CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            bool overwrite = false;
            if (Directory.Exists(targetPath))
            {
                overwrite = await DisplayAlertAsync("Overwrite templates?", "This directory already exists. Overwrite existing scaffold templates?", "Overwrite", "Keep existing").ConfigureAwait(true);
            }

            ProjectPathEntry.Text = targetPath.Trim();
            await RunBusyAsync("Scaffolding", async () =>
            {
                string message = await _controller.ScaffoldAsync(targetPath, overwrite, CancellationToken.None).ConfigureAwait(true);
                AppendLog(message);
                OpenProjectFromPath(targetPath);
                await RefreshGitAsync().ConfigureAwait(true);
                await RefreshReferenceScanAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnNewProjectMenuClicked), ex);
        }
    }

    /// <summary>
    /// Handles opening an existing project directory.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnOpenDirectoryMenuClicked(object? sender, EventArgs e)
    {
        try
        {
            OpenMenuFlyout.IsVisible = false;
            string? path = await PickProjectDirectoryAsync(CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            ProjectPathEntry.Text = path.Trim();
            await OpenProjectAsync(path.Trim()).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnOpenDirectoryMenuClicked), ex);
        }
    }

    /// <summary>
    /// Handles importing a project from a Grimoire zip file.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnOpenZipMenuClicked(object? sender, EventArgs e)
    {
        try
        {
            OpenMenuFlyout.IsVisible = false;
            FileResult? zip = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open Grimoire zip",
            }).ConfigureAwait(true);
            if (zip is null)
            {
                return;
            }

            string? target = await PickProjectDirectoryAsync(CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            ZipPathEntry.Text = zip.FullPath;
            ProjectPathEntry.Text = target.Trim();
            await RunBusyAsync("Importing zip", async () =>
            {
                string message = await _controller.ImportZipAsync(zip.FullPath, target, CancellationToken.None).ConfigureAwait(true);
                AppendLog(message);
                OpenProjectFromPath(target);
                await RefreshReferenceScanAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnOpenZipMenuClicked), ex);
        }
    }

    /// <summary>
    /// Toggles the open-project flyout menu.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnOpenDropdownClicked(object? sender, EventArgs e)
    {
        BuildMenuFlyout.IsVisible = false;
        OpenMenuFlyout.IsVisible = !OpenMenuFlyout.IsVisible;
    }

    /// <summary>
    /// Opens a project and refreshes dependent UI state.
    /// </summary>
    /// <param name="path">The project path to open.</param>
    private async Task OpenProjectAsync(string path)
    {
        await RunBusyAsync("Loading project", async () =>
        {
            OpenProjectFromPath(path);
            await RefreshGitAsync().ConfigureAwait(true);
            await RefreshReferenceScanAsync().ConfigureAwait(true);
            AppendLog($"Opened project: {path}");
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Applies a project path to the workspace and resets editor state.
    /// </summary>
    /// <param name="path">The project path to open.</param>
    private void OpenProjectFromPath(string path)
    {
        _currentProjectPath = Path.GetFullPath(path.Trim());
        ProjectPathEntry.Text = _currentProjectPath;
        _expandedDirectories.Clear();
        _editorWorkspace.Clear();
        ClearEditorSurface();
        LoadProjectTree();
        UpdateProjectTitle();
    }

    /// <summary>
    /// Loads and caches project tree items from the active project.
    /// </summary>
    private void LoadProjectTree()
    {
        _allProjectItems = [.. _controller.OpenProject(ProjectPathEntry.Text)];
        var currentDirectories = _allProjectItems
            .Where(static item => item.IsDirectory)
            .Select(static item => item.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] pathsToCollapse =
        [
            .. _expandedDirectories.Where(path => !currentDirectories.Contains(path)),
        ];
        foreach (string path in pathsToCollapse)
        {
            _expandedDirectories.Remove(path);
        }

        if (_expandedDirectories.Count == 0)
        {
            foreach (ProjectFileItem item in _allProjectItems.Where(static item => item is { IsDirectory: true, Depth: 0 }))
            {
                if (string.Equals(item.RelativePath, "content", StringComparison.OrdinalIgnoreCase))
                {
                    _expandedDirectories.Add(item.RelativePath);
                }
            }
        }

        ApplyProjectFilter();
    }

    /// <summary>
    /// Applies the current project filter and expansion state to visible rows.
    /// </summary>
    private void ApplyProjectFilter()
    {
        string filter = ProjectSearchBar.Text?.Trim() ?? string.Empty;
        List<ProjectTreeRow> rows = [];
        foreach (ProjectFileItem item in _allProjectItems.OrderBy(static item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsProjectItemVisible(item, filter))
            {
                continue;
            }

            rows.Add(new ProjectTreeRow(item, IsExpanded: _expandedDirectories.Contains(item.RelativePath)));
        }

        ProjectFilesView.ItemsSource = rows;
    }

    /// <summary>
    /// Determines whether a project tree item should be visible.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <param name="filter">The active text filter.</param>
    /// <returns><see langword="true"/> when the item should be displayed.</returns>
    private bool IsProjectItemVisible(ProjectFileItem item, string filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            return item.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   item.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(item.ParentPath))
        {
            return true;
        }

        string[] segments = item.ParentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = string.Empty;
        foreach (string segment in segments)
        {
            current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
            if (!_expandedDirectories.Contains(current))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Handles project-search text updates.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The text-change event arguments.</param>
    private void OnProjectSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyProjectFilter();
    }

    /// <summary>
    /// Toggles a directory row expansion state in the project tree.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnProjectTreeToggleClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ProjectTreeRow row || !row.IsDirectory)
        {
            return;
        }

        if (!_expandedDirectories.Remove(row.RelativePath))
        {
            _expandedDirectories.Add(row.RelativePath);
        }

        ApplyProjectFilter();
    }

    /// <summary>
    /// Handles project tree selection changes.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The selection-change arguments.</param>
    private async void OnProjectFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.Count == 0 || e.CurrentSelection[0] is not ProjectTreeRow row)
            {
                return;
            }

            _selectedProjectRow = row;
            if (!row.IsDirectory)
            {
                await OpenProjectFileAsync(row.RelativePath).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectFileSelected), ex);
        }
    }

    /// <summary>
    /// Handles project tree double-tap interactions.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The tap event arguments.</param>
    private async void OnProjectFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (ResolveProjectContextRow(sender) is not { } row)
            {
                return;
            }

            if (row.IsDirectory)
            {
                ToggleProjectDirectory(row);
                return;
            }

            await OpenProjectFileAsync(row.RelativePath).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectFileDoubleTapped), ex);
        }
    }

    /// <summary>
    /// Opens the selected project tree item from the context menu.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnProjectContextOpenClicked(object? sender, EventArgs e)
    {
        try
        {
            if (SelectProjectContextRow(sender) is not { } row)
            {
                return;
            }

            if (row.IsDirectory)
            {
                ToggleProjectDirectory(row);
                return;
            }

            await OpenProjectFileAsync(row.RelativePath).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectContextOpenClicked), ex);
        }
    }

    /// <summary>
    /// Reveals the selected project item in the operating system file browser.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnProjectContextShowClicked(object? sender, EventArgs e)
    {
        if (SelectProjectContextRow(sender) is not ProjectTreeRow row)
        {
            return;
        }

        ShowInFilesystem(_controller.GetProjectEntryFullPath(ProjectPathEntry.Text ?? string.Empty, row.RelativePath));
    }

    /// <summary>
    /// Opens or creates the template file for the selected directory.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnProjectContextEditTemplateClicked(object? sender, EventArgs e)
    {
        try
        {
            if (SelectProjectContextRow(sender) is not { } row || !CanEditDirectoryTemplate(row))
            {
                return;
            }

            await EditTemplateForDirectoryAsync(row.RelativePath).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectContextEditTemplateClicked), ex);
        }
    }

    /// <summary>
    /// Cuts the selected project entry into the internal clipboard.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnProjectContextCutClicked(object? sender, EventArgs e)
    {
        if (SelectProjectContextRow(sender) is not { } row)
        {
            return;
        }

        _projectClipboardPath = row.RelativePath;
        _projectClipboardIsCut = true;
        AppendLog($"Cut {row.RelativePath}");
    }

    /// <summary>
    /// Copies the selected project entry into the internal clipboard.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnProjectContextCopyClicked(object? sender, EventArgs e)
    {
        if (SelectProjectContextRow(sender) is not { } row)
        {
            return;
        }

        _projectClipboardPath = row.RelativePath;
        _projectClipboardIsCut = false;
        AppendLog($"Copied {row.RelativePath}");
    }

    /// <summary>
    /// Pastes the internal project clipboard into the selected destination.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnProjectContextPasteClicked(object? sender, EventArgs e)
    {
        try
        {
            if (SelectProjectContextRow(sender) is not { } row)
            {
                return;
            }

            string targetDirectory = row.IsDirectory ? row.RelativePath : row.ParentPath;
            await PasteProjectClipboardAsync(targetDirectory).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectContextPasteClicked), ex);
        }
    }

    /// <summary>
    /// Renames the selected project entry.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnProjectContextRenameClicked(object? sender, EventArgs e)
    {
        try
        {
            if (SelectProjectContextRow(sender) is null)
            {
                return;
            }

            await RenameSelectedProjectEntryAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectContextRenameClicked), ex);
        }
    }

    /// <summary>
    /// Deletes the selected project entry after confirmation.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnProjectContextDeleteClicked(object? sender, EventArgs e)
    {
        try
        {
            if (SelectProjectContextRow(sender) is null)
            {
                return;
            }

            await DeleteSelectedProjectEntryAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectContextDeleteClicked), ex);
        }
    }

    /// <summary>
    /// Starts dragging a project entry.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The drag-starting arguments.</param>
    private void OnProjectEntryDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (ResolveProjectContextRow(sender) is not { } row)
        {
            return;
        }

        _draggedProjectPath = row.RelativePath;
        e.Data.Text = row.RelativePath;
    }

    /// <summary>
    /// Handles dropping a dragged project entry onto a new target.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The drop event arguments.</param>
    private async void OnProjectEntryDropped(object? sender, DropEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_draggedProjectPath) || ResolveProjectContextRow(sender) is not ProjectTreeRow targetRow)
            {
                return;
            }

            string sourcePath = _draggedProjectPath;
            _draggedProjectPath = null;
            string targetDirectory = targetRow.IsDirectory ? targetRow.RelativePath : targetRow.ParentPath;
            if (sourcePath.Equals(targetDirectory, StringComparison.OrdinalIgnoreCase) ||
                targetDirectory.StartsWith($"{sourcePath}/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await RunBusyAsync("Moving project entry", async () =>
            {
                string message = await _controller.MoveProjectEntryAsync(ProjectPathEntry.Text ?? string.Empty, sourcePath, targetDirectory, CancellationToken.None).ConfigureAwait(true);
                AppendLog(message);
                LoadProjectTree();
                await RefreshReferenceScanAsync().ConfigureAwait(true);
                await RefreshGitAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnProjectEntryDropped), ex);
        }
    }

    /// <summary>
    /// Selects the project row associated with a context action.
    /// </summary>
    /// <param name="sender">The context action sender.</param>
    /// <returns>The resolved row or the previous selection when unresolved.</returns>
    private ProjectTreeRow? SelectProjectContextRow(object? sender)
    {
        ProjectTreeRow? row = ResolveProjectContextRow(sender);
        if (row is not null)
        {
            _selectedProjectRow = row;
        }

        return row ?? _selectedProjectRow;
    }

    /// <summary>
    /// Resolves a project tree row from common context-menu senders.
    /// </summary>
    /// <param name="sender">The context action sender.</param>
    /// <returns>The resolved row, or <see langword="null"/> when unavailable.</returns>
    private static ProjectTreeRow? ResolveProjectContextRow(object? sender)
    {
        if (sender is MenuFlyoutItem { CommandParameter: ProjectTreeRow commandRow })
        {
            return commandRow;
        }

        return (sender as BindableObject)?.BindingContext as ProjectTreeRow;
    }

    /// <summary>
    /// Toggles expansion for a directory row.
    /// </summary>
    /// <param name="row">The directory row to toggle.</param>
    private void ToggleProjectDirectory(ProjectTreeRow row)
    {
        if (!row.IsDirectory)
        {
            return;
        }

        if (!_expandedDirectories.Remove(row.RelativePath))
        {
            _expandedDirectories.Add(row.RelativePath);
        }

        ApplyProjectFilter();
    }

    /// <summary>
    /// Determines whether a directory supports template editing.
    /// </summary>
    /// <param name="row">The candidate project row.</param>
    /// <returns><see langword="true"/> when template editing is supported.</returns>
    private bool CanEditDirectoryTemplate(ProjectTreeRow row)
    {
        if (!row.IsDirectory || string.IsNullOrWhiteSpace(ProjectPathEntry.Text))
        {
            return false;
        }

        string directoryPath = Path.Combine(ProjectPathEntry.Text, row.RelativePath);
        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        if (File.Exists(Path.Combine(directoryPath, "TEMPLATE.md")))
        {
            return true;
        }

        try
        {
            return Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly).Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the template file for a directory, creating it when needed.
    /// </summary>
    /// <param name="relativeDirectoryPath">The project-relative directory path.</param>
    private async Task EditTemplateForDirectoryAsync(string relativeDirectoryPath)
    {
        string normalizedDirectory = relativeDirectoryPath.Trim().TrimEnd('/', '\\');
        string templateRelativePath = $"{normalizedDirectory}/TEMPLATE.md";
        string templateFullPath = Path.GetFullPath(Path.Combine(ProjectPathEntry.Text, templateRelativePath));
        if (!File.Exists(templateFullPath))
        {
            string createMessage = await _controller
                .SaveFileAsync(ProjectPathEntry.Text ?? string.Empty, templateRelativePath, "# {{title}}\n\n{{content}}\n", CancellationToken.None)
                .ConfigureAwait(true);
            AppendLog($"{createMessage} (template)");
            LoadProjectTree();
        }

        await OpenProjectFileAsync(templateRelativePath).ConfigureAwait(true);
    }

    /// <summary>
    /// Pastes the project clipboard into a prompted destination.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private Task PasteProjectClipboardAsync()
    {
        return PasteProjectClipboardAsync(null);
    }

    /// <summary>
    /// Pastes the project clipboard into the specified destination.
    /// </summary>
    /// <param name="targetDirectoryOverride">An optional destination directory override.</param>
    private async Task PasteProjectClipboardAsync(string? targetDirectoryOverride)
    {
        if (string.IsNullOrWhiteSpace(_projectClipboardPath))
        {
            return;
        }

        string? targetDirectory = targetDirectoryOverride ?? await DisplayPromptAsync("Target directory", "Project-relative target directory", initialValue: "").ConfigureAwait(true);
        if (targetDirectory is null)
        {
            return;
        }

        string message = _projectClipboardIsCut
            ? await _controller.MoveProjectEntryAsync(ProjectPathEntry.Text, _projectClipboardPath, targetDirectory, CancellationToken.None).ConfigureAwait(true)
            : await _controller.CopyProjectEntryAsync(ProjectPathEntry.Text, _projectClipboardPath, targetDirectory, CancellationToken.None).ConfigureAwait(true);
        AppendLog(message);
        _projectClipboardPath = null;
        LoadProjectTree();
        await RefreshReferenceScanAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Renames the currently selected project entry.
    /// </summary>
    private async Task RenameSelectedProjectEntryAsync()
    {
        if (_selectedProjectRow is null)
        {
            return;
        }

        string? newName = await DisplayPromptAsync("Rename", "New name", initialValue: _selectedProjectRow.Name).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        string message = await _controller.RenameProjectEntryAsync(ProjectPathEntry.Text, _selectedProjectRow.RelativePath, newName, CancellationToken.None).ConfigureAwait(true);
        AppendLog(message);
        LoadProjectTree();
    }

    /// <summary>
    /// Deletes the currently selected project entry.
    /// </summary>
    private async Task DeleteSelectedProjectEntryAsync()
    {
        if (_selectedProjectRow is null)
        {
            return;
        }

        bool confirmed = await DisplayAlertAsync("Delete", $"Delete {_selectedProjectRow.RelativePath}?", "Delete", "Cancel").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        string message = await _controller.DeleteProjectEntryAsync(ProjectPathEntry.Text, _selectedProjectRow.RelativePath, CancellationToken.None).ConfigureAwait(true);
        AppendLog(message);
        LoadProjectTree();
        await RefreshReferenceScanAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Handles the primary build button click.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBuildClicked(object? sender, EventArgs e)
    {
        try
        {
            BuildMenuFlyout.IsVisible = false;
            await BuildSelectedTargetAsync(_lastBuildTarget).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBuildClicked), ex);
        }
    }

    /// <summary>
    /// Toggles the build-target flyout menu.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnBuildDropdownClicked(object? sender, EventArgs e)
    {
        OpenMenuFlyout.IsVisible = false;
        BuildMenuFlyout.IsVisible = !BuildMenuFlyout.IsVisible;
    }

    /// <summary>
    /// Starts an all-target build from the build menu.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBuildAllMenuClicked(object? sender, EventArgs e)
    {
        try
        {
            BuildMenuFlyout.IsVisible = false;
            await BuildSelectedTargetAsync(BuildTarget.All).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBuildAllMenuClicked), ex);
        }
    }

    /// <summary>
    /// Starts an HTML-only build from the build menu.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBuildHtmlMenuClicked(object? sender, EventArgs e)
    {
        try
        {
            BuildMenuFlyout.IsVisible = false;
            await BuildSelectedTargetAsync(BuildTarget.Html).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBuildHtmlMenuClicked), ex);
        }
    }

    /// <summary>
    /// Starts a Foundry build from the build menu.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBuildFoundryMenuClicked(object? sender, EventArgs e)
    {
        try
        {
            BuildMenuFlyout.IsVisible = false;
            await BuildSelectedTargetAsync(BuildTarget.FoundryVtt).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBuildFoundryMenuClicked), ex);
        }
    }

    /// <summary>
    /// Starts a PDF build from the build menu.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBuildPdfMenuClicked(object? sender, EventArgs e)
    {
        try
        {
            BuildMenuFlyout.IsVisible = false;
            await BuildSelectedTargetAsync(BuildTarget.Pdf).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBuildPdfMenuClicked), ex);
        }
    }

    /// <summary>
    /// Builds the selected target using prompted output paths.
    /// </summary>
    /// <param name="target">The build target to execute.</param>
    private async Task BuildSelectedTargetAsync(BuildTarget target)
    {
        BuildOutputPaths? outputs = await PromptForBuildOutputsAsync(target).ConfigureAwait(true);
        if (outputs is null)
        {
            return;
        }

        _lastBuildTarget = target;
        _lastBuildOutputs = outputs;
        UpdateBuildButtonText();
        await RunBusyAsync("Building", async () =>
        {
            string message = await _controller.BuildAsync(ProjectPathEntry.Text, target, outputs, CancellationToken.None).ConfigureAwait(true);
            AppendLog(message);
            await RefreshGitAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Prompts for build outputs required by the selected target.
    /// </summary>
    /// <param name="target">The build target to configure.</param>
    /// <returns>The selected output paths, or <see langword="null"/> when canceled.</returns>
    private async Task<BuildOutputPaths?> PromptForBuildOutputsAsync(BuildTarget target)
    {
        string html = _lastBuildOutputs.HtmlPath;
        string foundry = _lastBuildOutputs.FoundryPath;
        string pdf = _lastBuildOutputs.PdfPath;

        if (target is BuildTarget.All)
        {
            return await ShowBuildOutputsDialogAsync(target, html, foundry, pdf).ConfigureAwait(true);
        }

        if (target is BuildTarget.Html)
        {
            string? value = await PickProjectDirectoryAsync(CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            html = value.Trim();
        }

        if (target is BuildTarget.FoundryVtt)
        {
            string? value = await PickSaveFilePathAsync(Path.GetFileName(foundry), CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            foundry = value.Trim();
        }

        if (target is BuildTarget.Pdf)
        {
            string? value = await PickSaveFilePathAsync(Path.GetFileName(pdf), CancellationToken.None).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            pdf = value.Trim();
        }

        return new BuildOutputPaths(html, foundry, pdf);
    }

    /// <summary>
    /// Shows the build output selection dialog.
    /// </summary>
    /// <param name="target">The build target being configured.</param>
    /// <param name="html">The initial HTML output path.</param>
    /// <param name="foundry">The initial Foundry output path.</param>
    /// <param name="pdf">The initial PDF output path.</param>
    /// <returns>The confirmed output paths, or <see langword="null"/> when canceled.</returns>
    private async Task<BuildOutputPaths?> ShowBuildOutputsDialogAsync(BuildTarget target, string html, string foundry, string pdf)
    {
        _pendingBuildTarget = target;
        BuildHtmlOutputEntry.Text = html;
        BuildFoundryOutputEntry.Text = foundry;
        BuildPdfOutputEntry.Text = pdf;
        BuildOutputsDialog.IsVisible = true;
        _buildOutputsCompletionSource = new TaskCompletionSource<BuildOutputPaths?>(TaskCreationOptions.RunContinuationsAsynchronously);
        BuildOutputPaths? result = await _buildOutputsCompletionSource.Task.ConfigureAwait(true);
        BuildOutputsDialog.IsVisible = false;
        _buildOutputsCompletionSource = null;
        return result;
    }

    /// <summary>
    /// Handles selecting an HTML output directory for builds.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBrowseHtmlBuildOutputClicked(object? sender, EventArgs e)
    {
        try
        {
            string? value = await PickProjectDirectoryAsync(CancellationToken.None).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(value))
            {
                BuildHtmlOutputEntry.Text = value;
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBrowseHtmlBuildOutputClicked), ex);
        }
    }

    /// <summary>
    /// Handles selecting a Foundry output file path for builds.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBrowseFoundryBuildOutputClicked(object? sender, EventArgs e)
    {
        try
        {
            string defaultName = Path.GetFileName(BuildFoundryOutputEntry.Text);
            string? value = await PickSaveFilePathAsync(string.IsNullOrWhiteSpace(defaultName) ? "foundry.db" : defaultName, CancellationToken.None).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(value))
            {
                BuildFoundryOutputEntry.Text = value;
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBrowseFoundryBuildOutputClicked), ex);
        }
    }

    /// <summary>
    /// Handles selecting a PDF output file path for builds.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBrowsePdfBuildOutputClicked(object? sender, EventArgs e)
    {
        try
        {
            string defaultName = Path.GetFileName(BuildPdfOutputEntry.Text);
            string? value = await PickSaveFilePathAsync(string.IsNullOrWhiteSpace(defaultName) ? "book.pdf" : defaultName, CancellationToken.None).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(value))
            {
                BuildPdfOutputEntry.Text = value;
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBrowsePdfBuildOutputClicked), ex);
        }
    }
}
