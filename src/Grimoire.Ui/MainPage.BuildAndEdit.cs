using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
#if WINDOWS
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
#endif
using Grimoire.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Grimoire.Ui;


/// <summary>
/// Implements build, import/export, and settings editor interactions for <see cref="MainPage"/>.
/// </summary>
public partial class MainPage : ContentPage
{
    /// <summary>
    /// Cancels the build output path dialog.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnCancelBuildOutputsClicked(object? sender, EventArgs e)
    {
        _buildOutputsCompletionSource?.TrySetResult(null);
    }

    /// <summary>
    /// Confirms selected build output paths.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnConfirmBuildOutputsClicked(object? sender, EventArgs e)
    {
        string html = BuildHtmlOutputEntry.Text?.Trim() ?? string.Empty;
        string foundry = BuildFoundryOutputEntry.Text?.Trim() ?? string.Empty;
        string pdf = BuildPdfOutputEntry.Text?.Trim() ?? string.Empty;
        if ((_pendingBuildTarget is BuildTarget.All or BuildTarget.Html && string.IsNullOrWhiteSpace(html)) ||
            (_pendingBuildTarget is BuildTarget.All or BuildTarget.FoundryVtt && string.IsNullOrWhiteSpace(foundry)) ||
            (_pendingBuildTarget is BuildTarget.All or BuildTarget.Pdf && string.IsNullOrWhiteSpace(pdf)))
        {
            StatusActionLabel.Text = "Build outputs required";
            return;
        }

        _buildOutputsCompletionSource?.TrySetResult(new(html, foundry, pdf));
    }

    /// <summary>
    /// Saves the active editor content to disk.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnSaveFileClicked(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath))
            {
                return;
            }

            await RunBusyAsync("Saving", async () =>
            {
                string content = _settingsEditorSession is null
                    ? await GetSourceEditorTextAsync().ConfigureAwait(true)
                    : BuildSettingsEditorYaml();
                string message = await _controller
                    .SaveFileAsync(ProjectPathEntry.Text ?? string.Empty, _currentFilePath, content, CancellationToken.None)
                    .ConfigureAwait(true);
                _loadingFile = true;
                _sourceEditorText = content;
                _loadingFile = false;
                _hasUnsavedChanges = false;
                _editorWorkspace.MarkActiveClean(content, GetCurrentEditorLineNumber());
                RefreshSourceSyntaxPreview();
                UpdateCurrentFileLabel();
                UpdateEditorTabsSelection();
                AppendLog(message);
                LoadProjectTree();
                await RenderCurrentPreviewAsync().ConfigureAwait(true);
                await RefreshGitAsync().ConfigureAwait(true);
                await RefreshReferenceScanAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnSaveFileClicked), ex);
        }
    }

    /// <summary>
    /// Creates a new Markdown content file.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnNewContentClicked(object? sender, EventArgs e)
    {
        try
        {
            string fileName = $"content/untitled-{DateTimeOffset.Now:yyyyMMddHHmmss}.md";
            await CreateAndOpenFileAsync(fileName, "# Untitled\n\n").ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnNewContentClicked), ex);
        }
    }

    /// <summary>
    /// Creates a new snippet JSON file.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnNewSnippetClicked(object? sender, EventArgs e)
    {
        try
        {
            string fileName = $"snippets/untitled-{DateTimeOffset.Now:yyyyMMddHHmmss}.json";
            await CreateAndOpenFileAsync(fileName, "{\n  \"title\": \"Untitled\",\n  \"content\": \"\"\n}\n").ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnNewSnippetClicked), ex);
        }
    }

    /// <summary>
    /// Syncs D&amp;D Beyond content into the current project.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnSyncDndbClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunBusyAsync("Importing DDB", async () =>
            {
                string message = await _controller
                    .SyncDndbAsync(
                        ProjectPathEntry.Text ?? string.Empty,
                        CobaltTokenEntry.Text,
                        CampaignEntry.Text,
                        ItemFilterEntry.Text,
                        CreatureFilterEntry.Text,
                        SpellFilterEntry.Text,
                        CharacterSheetFilterEntry.Text,
                        HomebrewCheckBox.IsChecked,
                        UpgradeCheckBox.IsChecked,
                        CancellationToken.None,
                        PatreonKeyEntry.Text)
                    .ConfigureAwait(true);
                AppendLog(message);
                LoadProjectTree();
                await RefreshReferenceScanAsync().ConfigureAwait(true);
                await RefreshGitAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnSyncDndbClicked), ex);
        }
    }

    /// <summary>
    /// Executes lore search and displays results.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnLoreSearchClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunBusyAsync("Searching lore", () =>
            {
                LoreSearchUiResult result = _controller.SearchLore(ProjectPathEntry.Text ?? string.Empty, LoreQueryEntry.Text ?? string.Empty, 50);
                LoreResultsView.ItemsSource = result.Results;
                AppendLog(result.Message);
                ShowBottomPanel("Lore search");
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnLoreSearchClicked), ex);
        }
    }

    /// <summary>
    /// Handles lore search submission from input controls.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnLoreSearchSubmitted(object? sender, EventArgs e)
    {
        OnLoreSearchClicked(sender, e);
    }

    /// <summary>
    /// Refreshes project reference scan results.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnRefreshReferenceScanClicked(object? sender, EventArgs e)
    {
        try
        {
            ShowBottomPanel("Reference scan");
            await RunBusyAsync("Scanning references", RefreshReferenceScanAsync).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnRefreshReferenceScanClicked), ex);
        }
    }

    /// <summary>
    /// Opens the selected lore search result in the editor.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The selection change arguments.</param>
    private async void OnLoreResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is LoreSearchResult result)
            {
                await OpenProjectFileAsync(result.Path, result.LineNumber).ConfigureAwait(true);
                AppendLog($"Opened {result.Path} at line {result.LineNumber}");
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnLoreResultSelected), ex);
        }
    }

    /// <summary>
    /// Refreshes git status for the active project.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnRefreshGitClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunBusyAsync("Refreshing git", RefreshGitAsync).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnRefreshGitClicked), ex);
        }
    }

    /// <summary>
    /// Commits staged project changes.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnCommitGitClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunBusyAsync("Committing", async () =>
            {
                string message = await _controller
                    .CommitGitAsync(ProjectPathEntry.Text ?? string.Empty, CommitMessageEntry.Text ?? string.Empty, CancellationToken.None)
                    .ConfigureAwait(true);
                AppendLog(message);
                CommitMessageEntry.Text = string.Empty;
                await RefreshGitAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnCommitGitClicked), ex);
        }
    }

    /// <summary>
    /// Imports an external asset into the project.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnImportAssetClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunBusyAsync("Importing asset", async () =>
            {
                string message = await _controller
                    .ImportAssetAsync(ProjectPathEntry.Text ?? string.Empty, AssetPathEntry.Text ?? string.Empty, AssetTargetEntry.Text, CancellationToken.None)
                    .ConfigureAwait(true);
                AppendLog(message);
                LoadProjectTree();
                await RefreshGitAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnImportAssetClicked), ex);
        }
    }

    /// <summary>
    /// Opens a picker for choosing an asset source path.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBrowseAssetClicked(object? sender, EventArgs e)
    {
        try
        {
            FileResult? asset = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select asset",
            }).ConfigureAwait(true);
            if (asset is not null)
            {
                AssetPathEntry.Text = asset.FullPath;
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBrowseAssetClicked), ex);
        }
    }

    /// <summary>
    /// Opens a picker for choosing a zip path.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBrowseZipClicked(object? sender, EventArgs e)
    {
        try
        {
            FileResult? zip = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select Grimoire zip",
            }).ConfigureAwait(true);
            if (zip is not null)
            {
                ZipPathEntry.Text = zip.FullPath;
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBrowseZipClicked), ex);
        }
    }

    /// <summary>
    /// Opens a save picker for zip export output.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnBrowseZipExportClicked(object? sender, EventArgs e)
    {
        try
        {
            string defaultName = string.IsNullOrWhiteSpace(ZipPathEntry.Text) ? "project.zip" : Path.GetFileName(ZipPathEntry.Text);
            string? zipPath = await PickSaveFilePathAsync(defaultName, CancellationToken.None).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(zipPath))
            {
                ZipPathEntry.Text = zipPath;
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnBrowseZipExportClicked), ex);
        }
    }

    /// <summary>
    /// Imports project content from a zip archive.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnImportZipClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunBusyAsync("Importing zip", async () =>
            {
                string message = await _controller
                    .ImportZipAsync(ZipPathEntry.Text ?? string.Empty, ProjectPathEntry.Text ?? string.Empty, CancellationToken.None)
                    .ConfigureAwait(true);
                AppendLog(message);
                LoadProjectTree();
                await RefreshReferenceScanAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnImportZipClicked), ex);
        }
    }

    /// <summary>
    /// Exports project content to a zip archive.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnExportZipClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunBusyAsync("Exporting zip", async () =>
            {
                string message = await _controller
                    .ExportZipAsync(ProjectPathEntry.Text ?? string.Empty, ZipPathEntry.Text ?? string.Empty, CancellationToken.None)
                    .ConfigureAwait(true);
                AppendLog(message);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnExportZipClicked), ex);
        }
    }

    /// <summary>
    /// Intercepts preview navigation and routes recognized links to project files.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The navigation event arguments.</param>
    private async void OnPreviewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        try
        {
            string? path = IsTrustedPreviewOpenUrl(e.Url) ? ExtractPreviewPath(e.Url) : null;
            path ??= ResolvePreviewNavigationPath(e.Url);
            path ??= ResolvePreviewProjectRelativePath(e.Url, ProjectPathEntry.Text ?? string.Empty, _currentFilePath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            e.Cancel = true;
            await OpenProjectFileAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnPreviewNavigating), ex);
        }
    }

    /// <summary>
    /// Handles source editor WebView callback navigation messages.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The navigation event arguments.</param>
    private void OnCodeEditorNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, SourceEditorMessageScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        SourceEditorMessage? message = ParseSourceEditorMessage(uri.Query);
        int lineNumber = message?.Line ?? ParseSourceEditorLineNumber(uri.Query);
        if (_loadingFile || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        _sourceEditorLineNumber = Math.Max(1, lineNumber);
        if (message is not null)
        {
            _sourceEditorText = message.Text;
        }

        if (string.Equals(uri.Host, "changed", StringComparison.OrdinalIgnoreCase))
        {
            _hasUnsavedChanges = true;
        }

        _editorWorkspace.UpdateActiveContent(_sourceEditorText, _sourceEditorLineNumber, _hasUnsavedChanges);
        UpdateCurrentFileLabel();
        UpdateCurrentLineLabel(_sourceEditorLineNumber);
        UpdateEditorTabsSelection();
        RecordCurrentEditorLocation();
    }

    /// <summary>
    /// Parses the line number reported by source editor callbacks.
    /// </summary>
    /// <param name="query">The callback query string.</param>
    /// <returns>The parsed line number, or <c>1</c> when unavailable.</returns>
    private static int ParseSourceEditorLineNumber(string query)
    {
        string? line = ExtractQueryValue(query, "line");
        return int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 1;
    }

    /// <summary>
    /// Parses a source editor payload message from callback query text.
    /// </summary>
    /// <param name="query">The callback query string.</param>
    /// <returns>The parsed message, or <see langword="null"/> when invalid.</returns>
    private static SourceEditorMessage? ParseSourceEditorMessage(string query)
    {
        string? payload = ExtractRawQueryValue(query, "payload");
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SourceEditorMessage>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a raw query value without path normalization.
    /// </summary>
    /// <param name="queryOrUrl">The query string or URL.</param>
    /// <param name="key">The key to resolve.</param>
    /// <returns>The decoded raw value, or <see langword="null"/> when missing.</returns>
    private static string? ExtractRawQueryValue(string queryOrUrl, string key)
    {
        string marker = "?" + key + "=";
        int queryIndex = queryOrUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (queryIndex < 0)
        {
            marker = "&" + key + "=";
            queryIndex = queryOrUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        }

        int valueIndex;
        if (queryIndex < 0 && queryOrUrl.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
        {
            valueIndex = key.Length + 1;
        }
        else if (queryIndex < 0)
        {
            return null;
        }
        else
        {
            valueIndex = queryIndex + marker.Length;
        }

        string encoded = queryOrUrl[valueIndex..];
        int separatorIndex = encoded.IndexOf('&', StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            encoded = encoded[..separatorIndex];
        }

        return Uri.UnescapeDataString(encoded);
    }

    /// <summary>
    /// Retrieves the current text value from the browser-based source editor.
    /// </summary>
    /// <returns>The editor text snapshot.</returns>
    private async Task<string> GetSourceEditorTextAsync()
    {
        try
        {
            string? encoded = await CodeEditorWebView
                .EvaluateJavaScriptAsync("JSON.stringify({type:'source-editor-text',text:window.grimoireEditor ? (window.grimoireEditor.getValue ? window.grimoireEditor.getValue() : (window.grimoireEditor.getValueJson ? JSON.parse(window.grimoireEditor.getValueJson()) : '')) : ''})")
                .ConfigureAwait(true);
            return DecodeJavaScriptStringResult(encoded);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return _sourceEditorText;
        }
    }

    /// <summary>
    /// Decodes a JavaScript evaluation result into editor text.
    /// </summary>
    /// <param name="encoded">The encoded JavaScript result.</param>
    /// <returns>The decoded editor text.</returns>
    internal static string DecodeJavaScriptStringResult(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return string.Empty;
        }

        string trimmed = encoded.Trim();
        if (TryDecodeSourceEditorInteropPayload(trimmed, out string decodedPayload))
        {
            return decodedPayload;
        }

        try
        {
            string? decoded = JsonSerializer.Deserialize<string>(trimmed);
            if (TryDecodeSourceEditorInteropPayload(decoded, out decodedPayload))
            {
                return decodedPayload;
            }

            return decoded ?? string.Empty;
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    /// <summary>
    /// Attempts to decode a source editor interop envelope payload.
    /// </summary>
    /// <param name="payload">The serialized payload text.</param>
    /// <param name="text">The extracted editor text when decoding succeeds.</param>
    /// <returns><see langword="true"/> when payload decoding succeeds.</returns>
    private static bool TryDecodeSourceEditorInteropPayload(string? payload, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            SourceEditorInteropPayload? envelope = JsonSerializer.Deserialize<SourceEditorInteropPayload>(payload);
            if (envelope is null ||
                !string.Equals(envelope.Type, SourceEditorInteropPayloadType, StringComparison.Ordinal))
            {
                return false;
            }

            text = envelope.Text ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Switches the source pane into editable mode.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnSourceEditModeClicked(object? sender, EventArgs e)
    {
        SetSourcePaneDisplay(showSyntax: false);
    }

    /// <summary>
    /// Switches the source pane into syntax preview mode.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private void OnSourceSyntaxModeClicked(object? sender, EventArgs e)
    {
        RefreshSourceSyntaxPreview();
        SetSourcePaneDisplay(showSyntax: true);
    }

    /// <summary>
    /// Configures editor controls for an opened project document.
    /// </summary>
    /// <param name="document">The opened project file document.</param>
    private void ConfigureSourcePaneForDocument(ProjectFileDocument document)
    {
        _settingsEditorSession = null;
        SettingsEditorScroll.IsVisible = false;
        PreviewWebView.IsVisible = true;
        SourceModeBar.IsVisible = false;
        CodeEditorWebView.IsVisible = true;

        if (TryGetSettingsProfile(document.RelativePath, out SettingsProfile profile))
        {
            ConfigureSettingsEditor(profile, document.Content);
            return;
        }

        _syntaxViewEnabled = SupportsSyntaxHighlight(Path.GetExtension(document.RelativePath));
        SourceModeBar.IsVisible = false;
        SetSourcePaneDisplay(showSyntax: true);
        RefreshSourceSyntaxPreview();
    }

    /// <summary>
    /// Initializes the structured settings editor for a settings file.
    /// </summary>
    /// <param name="profile">The settings profile being edited.</param>
    /// <param name="content">The YAML content to load.</param>
    private void ConfigureSettingsEditor(SettingsProfile profile, string content)
    {
        IReadOnlyList<SettingsFieldDefinition> fields = GetSettingsFields(profile);
        YamlSettingsDocument localSettings = YamlSettingsDocument.Parse(content);
        YamlSettingsDocument globalSettings = profile == SettingsProfile.Global
            ? localSettings.Clone()
            : LoadProjectSettingsDocument("settings/global.yml");

        _settingsEditorSession = new SettingsEditorSession(profile, localSettings, globalSettings, fields);
        BuildSettingsEditorUi(_settingsEditorSession);
        SettingsEditorScroll.IsVisible = true;
        PreviewWebView.IsVisible = false;
        _previewNavigationTargets.Clear();
        SourceModeBar.IsVisible = false;
        CodeEditorWebView.IsVisible = true;
        _syntaxViewEnabled = true;
        SyncSourceTextFromSettingsEditor(markDirty: false);
    }

    /// <summary>
    /// Loads a settings YAML file from the active project.
    /// </summary>
    /// <param name="relativePath">The project-relative settings path.</param>
    /// <returns>The parsed settings document.</returns>
    private YamlSettingsDocument LoadProjectSettingsDocument(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(ProjectPathEntry.Text))
        {
            return new YamlSettingsDocument();
        }

        string fullPath = _controller.GetProjectEntryFullPath(ProjectPathEntry.Text, relativePath);
        if (!File.Exists(fullPath))
        {
            return new YamlSettingsDocument();
        }

        return YamlSettingsDocument.Parse(File.ReadAllText(fullPath));
    }

    /// <summary>
    /// Builds controls for the active settings editor session.
    /// </summary>
    /// <param name="session">The active settings editor session.</param>
    private void BuildSettingsEditorUi(SettingsEditorSession session)
    {
        SettingsEditorStack.Children.Clear();
        session.FieldStates.Clear();

        SettingsEditorStack.Children.Add(new Label
        {
            Text = $"Settings editor: {GetSettingsFileName(session.Profile)}",
            TextColor = Color.FromArgb("#D9B76E"),
            FontAttributes = FontAttributes.Bold,
        });

        foreach (IGrouping<string, SettingsFieldDefinition> section in session.Fields.GroupBy(static field => field.Section))
        {
            SettingsEditorStack.Children.Add(new Label
            {
                Text = section.Key,
                TextColor = Color.FromArgb("#C9D3D1"),
                FontAttributes = FontAttributes.Bold,
                Margin = new Thickness(0, 8, 0, 0),
            });

            foreach (SettingsFieldDefinition definition in section)
            {
                SettingsFieldState state = CreateSettingsFieldState(session, definition);
                session.FieldStates.Add(state);
                SettingsEditorStack.Children.Add(state.Container);
            }
        }
    }

    /// <summary>
    /// Creates runtime state for a single settings editor field.
    /// </summary>
    /// <param name="session">The active settings editor session.</param>
    /// <param name="definition">The field definition.</param>
    /// <returns>The initialized settings field state.</returns>
    private SettingsFieldState CreateSettingsFieldState(SettingsEditorSession session, SettingsFieldDefinition definition)
    {
        bool hasLocal = session.LocalSettings.TryGetValue(definition.Key, out object? localValue);
        bool hasInherited = definition.Inheritable && session.GlobalSettings.TryGetValue(definition.Key, out _);
        object? effectiveValue = hasLocal
            ? localValue
            : hasInherited
                ? session.GlobalSettings.GetValue(definition.Key)
                : definition.DefaultValue;
        bool isOverridden = definition.Inheritable ? hasLocal : true;

        VerticalStackLayout container = new()
        {
            Spacing = 6,
            Padding = new Thickness(0, 2),
        };
        Label titleLabel = new()
        {
            Text = definition.Label,
            TextColor = Color.FromArgb("#FFFFFF"),
            FontSize = 13,
        };
        container.Children.Add(titleLabel);

        Microsoft.Maui.Controls.Switch? overrideSwitch = null;
        if (definition.Inheritable)
        {
            Label overrideLabel = new()
            {
                Text = "Override in this file",
                TextColor = Color.FromArgb("#89989A"),
                VerticalTextAlignment = TextAlignment.Center,
                FontSize = 12,
            };
            overrideSwitch = new Microsoft.Maui.Controls.Switch
            {
                IsToggled = isOverridden,
                HorizontalOptions = LayoutOptions.Start,
            };
            HorizontalStackLayout overrideRow = new()
            {
                Spacing = 8,
                Children = { overrideSwitch, overrideLabel },
            };
            container.Children.Add(overrideRow);
        }

        View inputControl = BuildSettingsInputControl(definition, effectiveValue);
        inputControl.IsEnabled = isOverridden;
        container.Children.Add(inputControl);

        Label statusLabel = new()
        {
            TextColor = Color.FromArgb("#89989A"),
            FontSize = 12,
        };
        container.Children.Add(statusLabel);

        SettingsFieldState state = new(definition, container, inputControl, statusLabel, overrideSwitch, hasInherited);
        UpdateSettingsFieldStatus(state);
        RegisterSettingsFieldChangeHandlers(state);
        return state;
    }

    /// <summary>
    /// Builds the appropriate input control for a settings field.
    /// </summary>
    /// <param name="definition">The field definition.</param>
    /// <param name="value">The effective field value.</param>
    /// <returns>The configured input <see cref="View"/>.</returns>
    private static View BuildSettingsInputControl(SettingsFieldDefinition definition, object? value)
    {
        switch (definition.Type)
        {
            case SettingFieldType.Boolean:
                return new Microsoft.Maui.Controls.Switch
                {
                    IsToggled = ParseBooleanValue(value, defaultValue: string.Equals(definition.DefaultValue, "true", StringComparison.OrdinalIgnoreCase)),
                };
            case SettingFieldType.Integer:
                return new Entry
                {
                    Text = ParseIntegerValue(value)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    Keyboard = Keyboard.Numeric,
                    TextColor = Color.FromArgb("#FFFFFF"),
                    BackgroundColor = Color.FromArgb("#253138"),
                };
            case SettingFieldType.Multiline:
                return new Editor
                {
                    Text = ConvertToString(value),
                    AutoSize = EditorAutoSizeOption.TextChanges,
                    MinimumHeightRequest = 90,
                    FontSize = 13,
                    TextColor = Color.FromArgb("#FFFFFF"),
                    BackgroundColor = Color.FromArgb("#253138"),
                };
            case SettingFieldType.List:
                return new Editor
                {
                    Text = string.Join(Environment.NewLine, ConvertToList(value)),
                    AutoSize = EditorAutoSizeOption.TextChanges,
                    MinimumHeightRequest = 72,
                    FontSize = 13,
                    FontFamily = "Courier New",
                    TextColor = Color.FromArgb("#FFFFFF"),
                    BackgroundColor = Color.FromArgb("#253138"),
                };
            case SettingFieldType.Choice:
                Picker picker = new()
                {
                    TextColor = Color.FromArgb("#FFFFFF"),
                    BackgroundColor = Color.FromArgb("#253138"),
                };
                foreach (string choice in definition.Choices)
                {
                    picker.Items.Add(choice);
                }

                string selected = ConvertToString(value);
                int selectedIndex = picker.Items.IndexOf(selected);
                picker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                return picker;
            case SettingFieldType.Text:
            default:
                return new Entry
                {
                    Text = ConvertToString(value),
                    TextColor = Color.FromArgb("#FFFFFF"),
                    BackgroundColor = Color.FromArgb("#253138"),
                };
        }
    }

    /// <summary>
    /// Wires change handlers that keep settings YAML synchronized with UI edits.
    /// </summary>
    /// <param name="state">The field state to wire.</param>
    private void RegisterSettingsFieldChangeHandlers(SettingsFieldState state)
    {
        if (state.OverrideSwitch is not null)
        {
            state.OverrideSwitch.Toggled += (_, _) =>
            {
                state.InputControl.IsEnabled = state.OverrideSwitch.IsToggled;
                UpdateSettingsFieldStatus(state);
                SyncSourceTextFromSettingsEditor(markDirty: true);
            };
        }

        switch (state.InputControl)
        {
            case Entry entry:
                entry.TextChanged += (_, _) => SyncSourceTextFromSettingsEditor(markDirty: true);
                break;
            case Editor editor:
                editor.TextChanged += (_, _) => SyncSourceTextFromSettingsEditor(markDirty: true);
                break;
            case Microsoft.Maui.Controls.Switch toggle:
                toggle.Toggled += (_, _) => SyncSourceTextFromSettingsEditor(markDirty: true);
                break;
            case Picker picker:
                picker.SelectedIndexChanged += (_, _) => SyncSourceTextFromSettingsEditor(markDirty: true);
                break;
        }
    }

    /// <summary>
    /// Updates inheritance status text for a settings field.
    /// </summary>
    /// <param name="state">The field state to update.</param>
    private static void UpdateSettingsFieldStatus(SettingsFieldState state)
    {
        if (!state.Definition.Inheritable)
        {
            state.StatusLabel.Text = "Local value";
            return;
        }

        bool overrideEnabled = state.OverrideSwitch?.IsToggled ?? false;
        if (overrideEnabled)
        {
            state.StatusLabel.Text = "Local override";
            return;
        }

        if (state.HasInheritedValue)
        {
            state.StatusLabel.Text = "Inherited from settings/global.yml";
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.Definition.DefaultValue))
        {
            state.StatusLabel.Text = $"Using default ({state.Definition.DefaultValue})";
            return;
        }

        state.StatusLabel.Text = "Inherited (unset)";
    }

    /// <summary>
    /// Synchronizes YAML source text from the settings editor controls.
    /// </summary>
    /// <param name="markDirty">A value indicating whether unsaved changes should be marked.</param>
    private void SyncSourceTextFromSettingsEditor(bool markDirty)
    {
        if (_settingsEditorSession is null)
        {
            return;
        }

        string yaml = BuildSettingsEditorYaml();
        _loadingFile = true;
        _sourceEditorText = yaml;
        _loadingFile = false;
        RefreshSourceSyntaxPreview();
        if (markDirty)
        {
            _hasUnsavedChanges = true;
            UpdateCurrentFileLabel();
        }
    }

    /// <summary>
    /// Builds YAML content from the current settings editor session.
    /// </summary>
    /// <returns>The generated YAML content.</returns>
    private string BuildSettingsEditorYaml()
    {
        if (_settingsEditorSession is null)
        {
            return _sourceEditorText;
        }

        YamlSettingsDocument updated = _settingsEditorSession.LocalSettings.Clone();
        foreach (SettingsFieldState state in _settingsEditorSession.FieldStates)
        {
            bool applyLocalValue = !state.Definition.Inheritable || state.OverrideSwitch?.IsToggled == true;
            if (!applyLocalValue)
            {
                updated.Remove(state.Definition.Key);
                continue;
            }

            object? value = ReadSettingsFieldValue(state);
            if (value is null)
            {
                updated.Remove(state.Definition.Key);
                continue;
            }

            updated.SetValue(state.Definition.Key, value);
        }

        return updated.ToYaml();
    }

    /// <summary>
    /// Reads the current value from a settings field control.
    /// </summary>
    /// <param name="state">The field state to read.</param>
    /// <returns>The normalized field value, or <see langword="null"/> when unset.</returns>
    private static object? ReadSettingsFieldValue(SettingsFieldState state)
    {
        switch (state.Definition.Type)
        {
            case SettingFieldType.Boolean:
                return state.InputControl is Microsoft.Maui.Controls.Switch toggle ? toggle.IsToggled : null;
            case SettingFieldType.Integer:
                if (state.InputControl is Entry numberEntry &&
                    int.TryParse(numberEntry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                {
                    return number;
                }

                return null;
            case SettingFieldType.List:
                string listText = state.InputControl switch
                {
                    Editor listEditor => listEditor.Text ?? string.Empty,
                    Entry listEntry => listEntry.Text ?? string.Empty,
                    _ => string.Empty,
                };
                string[] listItems =
                [
                    .. listText
                        .Split([',', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                ];
                return listItems.Length == 0 ? null : listItems;
            case SettingFieldType.Choice:
                if (state.InputControl is Picker picker && picker.SelectedIndex >= 0 && picker.SelectedIndex < picker.Items.Count)
                {
                    string selected = picker.Items[picker.SelectedIndex];
                    return string.IsNullOrWhiteSpace(selected) ? null : selected.Trim();
                }

                return null;
            case SettingFieldType.Multiline:
                string multiline = state.InputControl is Editor multilineEditor ? multilineEditor.Text ?? string.Empty : string.Empty;
                return string.IsNullOrWhiteSpace(multiline) ? null : multiline.Trim();
            case SettingFieldType.Text:
            default:
                string text = state.InputControl switch
                {
                    Entry entry => entry.Text ?? string.Empty,
                    Editor editor => editor.Text ?? string.Empty,
                    _ => string.Empty,
                };
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
    }
}
