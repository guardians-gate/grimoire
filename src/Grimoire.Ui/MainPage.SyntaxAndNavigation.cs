using System.Collections.Immutable;
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
/// Implements source rendering, syntax highlighting, and editor navigation behavior for <see cref="MainPage"/>.
/// </summary>
public partial class MainPage : ContentPage
{
    /// <summary>
    /// Configures the source pane display mode.
    /// </summary>
    /// <param name="showSyntax">A value indicating whether syntax preview mode should be shown.</param>
    private void SetSourcePaneDisplay(bool showSyntax)
    {
        CodeEditorWebView.IsVisible = true;
    }

    /// <summary>
    /// Regenerates and displays syntax preview HTML for the active source content.
    /// </summary>
    private void RefreshSourceSyntaxPreview()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        string html = BuildEditableSyntaxEditorHtml(_sourceEditorText, Path.GetExtension(_currentFilePath), _sourceEditorLineNumber);
        CodeEditorWebView.Source = new HtmlWebViewSource { Html = html };
    }

    /// <summary>
    /// Provides the HTML template for the editable Ace source editor surface.
    /// </summary>
    private const string EditableSyntaxEditorHtmlTemplate = """
<!doctype html>
<html>
<head>
<meta charset="utf-8"/>
<style>
html,body{height:100%;margin:0;background:#0D1113;color:#F4E8D0;overflow:hidden;}
#editor{position:absolute;inset:0;}
</style>
<script src="https://cdnjs.cloudflare.com/ajax/libs/ace/1.36.0/ace.js"></script>
</head>
<body>
<div id="editor"></div>
<script>
const initialText = __GRIMOIRE_CONTENT_JSON__;
const extension = __GRIMOIRE_EXTENSION_JSON__;
const targetLine = __GRIMOIRE_LINE_JSON__;
const messageScheme = __GRIMOIRE_SCHEME_JSON__;
const editor = ace.edit('editor');
let notifying = false;
let notifyHandle = 0;
let pendingChanged = false;

function resolveMode() {
  switch (extension) {
    case '.JSON': return 'ace/mode/json';
    case '.YML':
    case '.YAML': return 'ace/mode/yaml';
    case '.MD': return 'ace/mode/markdown';
    default: return 'ace/mode/text';
  }
}

function getText() {
  return editor.getValue().replace(/\r\n/g, '\n');
}

function getCurrentLine() {
  return editor.getCursorPosition().row + 1;
}

function notify(kind) {
  if (notifying) {
    return;
  }
  notifying = true;
  const line = getCurrentLine();
  window.location.href = messageScheme + '://' + kind + '?line=' + encodeURIComponent(String(line));
  notifying = false;
}

function scheduleNotify(changed) {
  pendingChanged = pendingChanged || changed;
  if (notifyHandle) {
    clearTimeout(notifyHandle);
  }

  notifyHandle = setTimeout(() => {
    notify(pendingChanged ? 'changed' : 'cursor');
    pendingChanged = false;
    notifyHandle = 0;
  }, changed ? 90 : 50);
}

function goToLine(line) {
  editor.gotoLine(Math.max(1, line), 0, true);
  editor.focus();
}

editor.setTheme('ace/theme/one_dark');
editor.session.setMode(resolveMode());
editor.session.setUseSoftTabs(true);
editor.session.setTabSize(4);
editor.setValue(initialText.replace(/\r\n/g, '\n'), -1);
editor.setOptions({
  showPrintMargin: false,
  highlightActiveLine: true,
  enableBasicAutocompletion: false,
  enableLiveAutocompletion: false,
  enableSnippets: false
});

editor.session.on('change', () => {
  scheduleNotify(true);
});
editor.selection.on('changeCursor', () => {
  scheduleNotify(false);
});
window.grimoireEditor = {
  getValue: () => getText(),
  getValueJson: () => JSON.stringify(getText()),
  goToLine
};
goToLine(targetLine);
scheduleNotify(false);
</script>
</body>
</html>
""";

    /// <summary>
    /// Builds editable source editor HTML by injecting content and metadata tokens.
    /// </summary>
    /// <param name="content">The source content to display.</param>
    /// <param name="extension">The source file extension.</param>
    /// <param name="lineNumber">The initial caret line number.</param>
    /// <returns>The composed editor HTML.</returns>
    private static string BuildEditableSyntaxEditorHtml(string content, string extension, int lineNumber)
    {
        string contentJson = JsonSerializer.Serialize(content);
        string extensionJson = JsonSerializer.Serialize(extension.ToUpperInvariant());
        string schemeJson = JsonSerializer.Serialize(SourceEditorMessageScheme);
        string lineNumberJson = JsonSerializer.Serialize(Math.Max(1, lineNumber));

        StringBuilder builder = new(EditableSyntaxEditorHtmlTemplate);
        builder.Replace("__GRIMOIRE_CONTENT_JSON__", contentJson);
        builder.Replace("__GRIMOIRE_EXTENSION_JSON__", extensionJson);
        builder.Replace("__GRIMOIRE_LINE_JSON__", lineNumberJson);
        builder.Replace("__GRIMOIRE_SCHEME_JSON__", schemeJson);
        return builder.ToString();
    }

    /// <summary>
    /// Builds static syntax-highlighted HTML for source preview.
    /// </summary>
    /// <param name="content">The source content to highlight.</param>
    /// <param name="extension">The source file extension.</param>
    /// <returns>The rendered syntax-highlighted HTML.</returns>
    private static string BuildSyntaxHighlightedHtml(string content, string extension)
    {
        StringBuilder builder = new();
        builder.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"/>");
        builder.AppendLine("<style>html,body{height:100%;}body{margin:0;background:#0D1113;color:#d7dce0;font-family:'Courier New',monospace;font-size:13px;line-height:1.45;}pre{box-sizing:border-box;margin:0;padding:12px;width:100%;min-height:100vh;white-space:pre-wrap;word-break:break-word;font:inherit;line-height:inherit;tab-size:4;color:#d7dce0;} .k{color:#8cc6ff;} .s{color:#e6d06c;} .n{color:#91d7ae;} .b{color:#d895ff;} .c{color:#6f8b8f;} .h{color:#f0b167;font-weight:700;} .m{color:#80d7ff;} .u{color:#b4e0ff;} .sx{color:#8fa2ad;} .sk{color:#caa8ff;} .sv{color:#ffd48a;} .sf{color:#7fc8ff;} .sp{color:#9ee6b8;} .mq{color:#b7c5ff;} .mi{color:#ffb07c;font-weight:700;}</style>");
        builder.AppendLine("</head><body><pre>");

        string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            builder.Append(extension.ToUpperInvariant() switch
            {
                ".JSON" => HighlightJsonLine(line),
                ".YML" or ".YAML" => HighlightYamlLine(line),
                ".MD" => HighlightMarkdownLine(line),
                _ => EscapeHtml(line),
            });
            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        builder.AppendLine("</pre></body></html>");
        return builder.ToString();
    }

    /// <summary>
    /// Highlights a single JSON line for syntax preview.
    /// </summary>
    /// <param name="line">The line to highlight.</param>
    /// <returns>The highlighted HTML fragment.</returns>
    private static string HighlightJsonLine(string line)
    {
        string encoded = EscapeHtml(line);
        encoded = JsonKeyRegex.Replace(
            encoded,
            "${1}<span class=\"k\">\"${key}\"</span>${3}${rest}");
        encoded = JsonStringRegex.Replace(encoded, "<span class=\"s\">$0</span>");
        encoded = NumberRegex.Replace(encoded, "<span class=\"n\">$0</span>");
        return BoolNullRegex.Replace(encoded, "<span class=\"b\">$1</span>");
    }

    /// <summary>
    /// Highlights a single YAML line for syntax preview.
    /// </summary>
    /// <param name="line">The line to highlight.</param>
    /// <returns>The highlighted HTML fragment.</returns>
    private static string HighlightYamlLine(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith('#'))
        {
            return $"<span class=\"c\">{EscapeHtml(line)}</span>";
        }

        string encoded = EscapeHtml(line);
        int separator = encoded.IndexOf(':', StringComparison.Ordinal);
        if (separator >= 0)
        {
            string key = encoded[..separator];
            string remainder = encoded[separator..];
            encoded = $"<span class=\"k\">{key}</span>{remainder}";
        }

        encoded = JsonStringRegex.Replace(encoded, "<span class=\"s\">$0</span>");
        encoded = NumberRegex.Replace(encoded, "<span class=\"n\">$0</span>");
        return BoolNullRegex.Replace(encoded, "<span class=\"b\">$1</span>");
    }

    /// <summary>
    /// Highlights a single Markdown line for syntax preview.
    /// </summary>
    /// <param name="line">The line to highlight.</param>
    /// <returns>The highlighted HTML fragment.</returns>
    private static string HighlightMarkdownLine(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return $"<span class=\"c\">{EscapeHtml(line)}</span>";
        }

        string encoded = EscapeHtml(line);
        encoded = HighlightMarkdownSupersetTokens(encoded);
        encoded = MarkdownInlineCodeRegex.Replace(encoded, "<span class=\"m\">$0</span>");
        encoded = MarkdownLinkRegex.Replace(encoded, HighlightMarkdownLinkMatch);
        if (trimmed.StartsWith('#'))
        {
            return $"<span class=\"h\">{encoded}</span>";
        }

        return encoded;
    }

    /// <summary>
    /// Highlights Grimoire-specific Markdown superset tokens.
    /// </summary>
    /// <param name="encoded">The HTML-encoded line text.</param>
    /// <returns>The token-highlighted line text.</returns>
    private static string HighlightMarkdownSupersetTokens(string encoded)
    {
        string highlighted = MarkdownProjectPageCountTokenRegex.Replace(
            encoded,
            match =>
            {
                string scope = match.Groups["scope"].Value;
                return "<span class=\"sx\">{{</span><span class=\"sk\">" +
                       scope +
                       ".pageCount</span><span class=\"sx\">}}</span>";
            });
        highlighted = MarkdownProjectSeeAlsoTokenRegex.Replace(
            highlighted,
            match =>
            {
                string scope = match.Groups["scope"].Value;
                string topic = match.Groups["topic"].Value;
                return "<span class=\"sx\">{{</span><span class=\"sk\">" +
                       scope +
                       ".seeAlso:</span><span class=\"sv\">" +
                       topic +
                       "</span><span class=\"sx\">}}</span>";
            });
        highlighted = MarkdownMacroTokenRegex.Replace(
            highlighted,
            "<span class=\"sx\">{{</span><span class=\"sk\">macro.${name}</span><span class=\"sx\">}}</span>");
        highlighted = MarkdownEntityLookupTokenRegex.Replace(
            highlighted,
            match =>
            {
                string name = match.Groups["name"].Value;
                bool hasProperty = match.Groups["property"].Success;
                string property = hasProperty ? match.Groups["property"].Value : string.Empty;
                return hasProperty
                    ? "<span class=\"sx\">{{%</span><span class=\"sf\">" +
                      name +
                      "</span><span class=\"sx\">:</span><span class=\"sp\">" +
                      property +
                      "</span><span class=\"sx\">}}</span>"
                    : "<span class=\"sx\">{{%</span><span class=\"sf\">" +
                      name +
                      "</span><span class=\"sx\">}}</span>";
            });

        return MarkdownFileSubstitutionTokenRegex.Replace(
            highlighted,
            match =>
            {
                string path = match.Groups["path"].Value;
                bool hasProperty = match.Groups["property"].Success;
                string property = hasProperty ? match.Groups["property"].Value : string.Empty;
                return hasProperty
                    ? "<span class=\"sx\">{{@</span><span class=\"sf\">" +
                      path +
                      "</span><span class=\"sx\">:</span><span class=\"sp\">" +
                      property +
                      "</span><span class=\"sx\">}}</span>"
                    : "<span class=\"sx\">{{@</span><span class=\"sf\">" +
                      path +
                      "</span><span class=\"sx\">}}</span>";
            });
    }

    /// <summary>
    /// Highlights a Markdown link match.
    /// </summary>
    /// <param name="match">The regex match to convert.</param>
    /// <returns>The highlighted link HTML fragment.</returns>
    private static string HighlightMarkdownLinkMatch(Match match)
    {
        string bang = match.Groups["bang"].Value;
        string text = match.Groups["text"].Value;
        string url = match.Groups["url"].Value;
        string path = url;
        string query = string.Empty;
        int queryIndex = url.IndexOfAny(['?', '&', ';']);
        if (queryIndex >= 0)
        {
            path = url[..queryIndex];
            query = url[queryIndex..];
        }

        string highlightedQuery = MarkdownInlineFlagRegex.Replace(
            query,
            "${prefix}<span class=\"mi\">${flag}</span>");
        return "<span class=\"u\">" +
               bang +
               "[</span>" +
               text +
               "<span class=\"u\">](</span><span class=\"sf\">" +
               path +
               "</span><span class=\"mq\">" +
               highlightedQuery +
               "</span><span class=\"u\">)</span>";
    }

    /// <summary>
    /// Determines whether syntax highlighting is supported for a file extension.
    /// </summary>
    /// <param name="extension">The file extension to evaluate.</param>
    /// <returns><see langword="true"/> when syntax highlighting is supported.</returns>
    private static bool SupportsSyntaxHighlight(string extension)
    {
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to map a project-relative path to a settings profile.
    /// </summary>
    /// <param name="relativePath">The project-relative file path.</param>
    /// <param name="profile">The resolved settings profile.</param>
    /// <returns><see langword="true"/> when a settings profile is recognized.</returns>
    private static bool TryGetSettingsProfile(string relativePath, out SettingsProfile profile)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Equals("settings/global.yml", StringComparison.OrdinalIgnoreCase))
        {
            profile = SettingsProfile.Global;
            return true;
        }

        if (normalized.Equals("settings/html.yml", StringComparison.OrdinalIgnoreCase))
        {
            profile = SettingsProfile.Html;
            return true;
        }

        if (normalized.Equals("settings/pdf.yml", StringComparison.OrdinalIgnoreCase))
        {
            profile = SettingsProfile.Pdf;
            return true;
        }

        if (normalized.Equals("settings/foundry.yml", StringComparison.OrdinalIgnoreCase))
        {
            profile = SettingsProfile.Foundry;
            return true;
        }

        profile = default;
        return false;
    }

    /// <summary>
    /// Returns editable field definitions for a settings profile.
    /// </summary>
    /// <param name="profile">The settings profile.</param>
    /// <returns>The settings field definitions for the profile.</returns>
    private static ImmutableArray<SettingsFieldDefinition> GetSettingsFields(SettingsProfile profile)
    {
        ImmutableArray<SettingsFieldDefinition>.Builder fields = ImmutableArray.CreateBuilder<SettingsFieldDefinition>();
        bool inheritable = profile != SettingsProfile.Global;
        foreach (SettingsFieldDefinition field in GlobalSettingsFields)
        {
            fields.Add(field with { Inheritable = inheritable });
        }

        if (profile is SettingsProfile.Html or SettingsProfile.Pdf)
        {
            foreach (SettingsFieldDefinition field in FontSettingsFields)
            {
                fields.Add(field with { Inheritable = true });
            }
        }

        if (profile is SettingsProfile.Html or SettingsProfile.Foundry)
        {
            foreach (SettingsFieldDefinition field in ScreenSettingsFields)
            {
                fields.Add(field with { Inheritable = true });
            }
        }

        if (profile is SettingsProfile.Foundry)
        {
            fields.AddRange(FoundrySettingsFields);
        }

        return [.. fields];
    }

    /// <summary>
    /// Gets the canonical file name for a settings profile.
    /// </summary>
    /// <param name="profile">The settings profile.</param>
    /// <returns>The project-relative settings file path.</returns>
    private static string GetSettingsFileName(SettingsProfile profile)
    {
        return profile switch
        {
            SettingsProfile.Global => "global.yml",
            SettingsProfile.Html => "html.yml",
            SettingsProfile.Pdf => "pdf.yml",
            SettingsProfile.Foundry => "foundry.yml",
            _ => "settings.yml",
        };
    }

    /// <summary>
    /// Escapes source text for safe HTML rendering.
    /// </summary>
    /// <param name="value">The raw source text.</param>
    /// <returns>The HTML-escaped text.</returns>
    private static string EscapeHtml(string value)
    {
        if (value.IndexOfAny(['&', '<', '>']) < 0)
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        foreach (char character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses a boolean from a settings value.
    /// </summary>
    /// <param name="value">The value to parse.</param>
    /// <param name="defaultValue">The fallback value when parsing fails.</param>
    /// <returns>The parsed boolean result.</returns>
    private static bool ParseBooleanValue(object? value, bool defaultValue)
    {
        string raw = ConvertToString(value);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return string.Equals(raw, "1", StringComparison.Ordinal) ||
               string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses an integer from a settings value.
    /// </summary>
    /// <param name="value">The value to parse.</param>
    /// <returns>The parsed integer, or <see langword="null"/> when invalid.</returns>
    private static int? ParseIntegerValue(object? value)
    {
        string raw = ConvertToString(value);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Converts a settings value to a trimmed string.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted string value.</returns>
    private static string ConvertToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            int number => number.ToString(CultureInfo.InvariantCulture),
            IEnumerable<string> list => string.Join(", ", list),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Converts a settings value to a string list.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted list values.</returns>
    private static string[] ConvertToList(object? value)
    {
        return value switch
        {
            null => [],
            IEnumerable<string> list => [.. list],
            _ =>
            [
                .. ConvertToString(value)
                    .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ],
        };
    }

    /// <summary>
    /// Creates a project file and opens it in the editor.
    /// </summary>
    /// <param name="relativePath">The project-relative path to create.</param>
    /// <param name="content">The initial file content.</param>
    private async Task CreateAndOpenFileAsync(string relativePath, string content)
    {
        await RunBusyAsync("Creating file", async () =>
        {
            await _controller.SaveFileAsync(ProjectPathEntry.Text ?? string.Empty, relativePath, content, CancellationToken.None).ConfigureAwait(true);
            LoadProjectTree();
            await OpenProjectFileAsync(relativePath).ConfigureAwait(true);
            await RefreshGitAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens a project file in the editor.
    /// </summary>
    /// <param name="relativePath">The project-relative file path.</param>
    private Task OpenProjectFileAsync(string relativePath)
    {
        return OpenProjectFileAsync(relativePath, lineNumber: null);
    }

    /// <summary>
    /// Opens a project file in the editor and optionally moves to a line.
    /// </summary>
    /// <param name="relativePath">The project-relative file path.</param>
    /// <param name="lineNumber">The optional line to focus.</param>
    private async Task OpenProjectFileAsync(string relativePath, int? lineNumber)
    {
        await CaptureActiveEditorStateAsync().ConfigureAwait(true);
        EditorTabState? existingTab = _editorWorkspace.FindTab(relativePath);
        if (existingTab is not null)
        {
            int targetLine = lineNumber ?? existingTab.LineNumber;
            await RunBusyAsync("Switching file", async () =>
            {
                _editorWorkspace.OpenOrActivate(existingTab.RelativePath, existingTab.Kind, existingTab.Content, targetLine);
                ApplyTabToEditor(existingTab, targetLine);
                await RenderCurrentPreviewAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
            return;
        }

        await RunBusyAsync("Opening file", async () =>
        {
            const int totalSteps = 4;
            SetBusyStepProgress(1, totalSteps, "Opening file: reading source");
            ProjectFileDocument document = await _controller
                .OpenFileAsync(ProjectPathEntry.Text ?? string.Empty, relativePath, CancellationToken.None)
                .ConfigureAwait(true);
            SetBusyStepProgress(2, totalSteps, "Opening file: updating editor");
            int targetLine = lineNumber ?? 1;
            EditorTabState tab = _editorWorkspace.OpenOrActivate(document.RelativePath, document.Kind, document.Content, targetLine);
            tab.IsDirty = false;
            ApplyDocumentToEditor(document, targetLine);
            AppendKeywordHighlights(document);
            SetBusyStepProgress(3, totalSteps, "Opening file: rendering preview");
            await RenderCurrentPreviewAsync().ConfigureAwait(true);
            SetBusyStepProgress(4, totalSteps, "Opening file: finalizing");
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Applies a loaded document to editor UI state.
    /// </summary>
    /// <param name="document">The loaded document.</param>
    /// <param name="lineNumber">The target editor line.</param>
    private void ApplyDocumentToEditor(ProjectFileDocument document, int lineNumber)
    {
        _currentFilePath = document.RelativePath;
        _loadingFile = true;
        _sourceEditorText = document.Content;
        _sourceEditorLineNumber = Math.Max(1, lineNumber);
        _loadingFile = false;
        ConfigureSourcePaneForDocument(document);
        _hasUnsavedChanges = _editorWorkspace.ActiveTab?.IsDirty == true;
        MoveEditorToLine(lineNumber);
        UpdateCurrentFileLabel();
        UpdateEditorTabsSelection();
        RecordCurrentEditorLocation();
    }

    /// <summary>
    /// Applies an editor tab snapshot to the editor surface.
    /// </summary>
    /// <param name="tab">The tab snapshot to apply.</param>
    /// <param name="lineNumber">The target editor line.</param>
    private void ApplyTabToEditor(EditorTabState tab, int lineNumber)
    {
        ProjectFileDocument document = new(tab.RelativePath, tab.Content, tab.Kind, []);
        ApplyDocumentToEditor(document, lineNumber);
    }

    /// <summary>
    /// Clears all editor surface state.
    /// </summary>
    private void ClearEditorSurface()
    {
        _currentFilePath = null;
        _settingsEditorSession = null;
        _syntaxViewEnabled = false;
        _previewNavigationTargets.Clear();
        _loadingFile = true;
        _sourceEditorText = string.Empty;
        _sourceEditorLineNumber = 1;
        _loadingFile = false;
        CodeEditorWebView.IsVisible = true;
        CodeEditorWebView.Source = new HtmlWebViewSource { Html = BuildEditableSyntaxEditorHtml(string.Empty, ".md", 1) };
        SourceModeBar.IsVisible = false;
        SettingsEditorScroll.IsVisible = false;
        PreviewWebView.IsVisible = true;
        PreviewWebView.Source = new HtmlWebViewSource { Html = "<html><body></body></html>" };
        _hasUnsavedChanges = false;
        UpdateCurrentFileLabel();
        UpdateEditorTabsSelection();
        UpdateNavigationButtons();
    }

    /// <summary>
    /// Captures current editor content and line state into the active tab.
    /// </summary>
    private async Task CaptureActiveEditorStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        string content = _settingsEditorSession is null
            ? await GetSourceEditorTextAsync().ConfigureAwait(true)
            : BuildSettingsEditorYaml();
        _sourceEditorText = content;
        _editorWorkspace.UpdateActiveContent(content, GetCurrentEditorLineNumber(), _hasUnsavedChanges);
    }

    /// <summary>
    /// Moves the editor caret to a specific line.
    /// </summary>
    /// <param name="lineNumber">The target 1-based line number.</param>
    private void MoveEditorToLine(int lineNumber)
    {
        _sourceEditorLineNumber = Math.Max(1, lineNumber);
        RefreshSourceSyntaxPreview();
        UpdateCurrentLineLabel(lineNumber);
    }

    /// <summary>
    /// Renders preview output for the currently active file.
    /// </summary>
    private async Task RenderCurrentPreviewAsync()
    {
        if (_settingsEditorSession is not null)
        {
            SettingsEditorScroll.IsVisible = true;
            PreviewWebView.IsVisible = false;
            _previewNavigationTargets.Clear();
            return;
        }

        SettingsEditorScroll.IsVisible = false;
        PreviewWebView.IsVisible = true;
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            PreviewWebView.Source = new HtmlWebViewSource { Html = "<html><body></body></html>" };
            _previewNavigationTargets.Clear();
            return;
        }

        string extension = Path.GetExtension(_currentFilePath);
        if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            PreviewWebView.Source = new HtmlWebViewSource
            {
                Html = "<html><body style=\"font-family:sans-serif;padding:16px;\">Preview is available for Markdown and JSON sources.</body></html>",
            };
            _previewNavigationTargets.Clear();
            return;
        }

        Stopwatch previewTimer = Stopwatch.StartNew();
        StatusActionLabel.Text = "Rendering preview: includes, substitutions, and autolinks";
        StatusProgressBar.Progress = 0;
        ProgressTextLabel.Text = "Preparing preview";
        SourcebookPreviewResult preview = await _controller
            .RenderPreviewAsync(ProjectPathEntry.Text ?? string.Empty, _currentFilePath, CancellationToken.None)
            .ConfigureAwait(true);
        previewTimer.Stop();
        StatusProgressBar.Progress = 1;
        string baseUrl = BuildPreviewBaseUrl(ProjectPathEntry.Text ?? string.Empty, _currentFilePath);
        PreviewWebView.Source = new HtmlWebViewSource
        {
            Html = preview.Html,
            BaseUrl = baseUrl,
        };
        _previewNavigationTargets = BuildPreviewNavigationTargets(preview.LinkTargets, baseUrl);
        string previewReason = preview.Diagnostics switch
        {
            { FileCacheHit: true } => "file cache hit",
            { ProjectCacheHit: true } => "warm project cache",
            { ProjectCacheHit: false, CacheBuildElapsedMs: > 0 } diagnostics => $"cold cache rebuild ({diagnostics.IndexTopicCount} topics, {diagnostics.CacheBuildElapsedMs} ms)",
            _ => "rendered",
        };
        AppendLog($"Preview rendered {_currentFilePath} in {previewTimer.ElapsedMilliseconds} ms ({previewReason}, {preview.LinkTargets.Count} links).");
    }

    /// <summary>
    /// Builds a base URL used for preview link resolution.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The current project-relative file path.</param>
    /// <returns>The preview base URL.</returns>
    private static string BuildPreviewBaseUrl(string projectPath, string relativePath)
    {
        string baseDirectory = Path.GetFullPath(projectPath);
        string? sourceDirectory = Path.GetDirectoryName(relativePath);
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
        {
            baseDirectory = Path.GetFullPath(Path.Combine(projectPath, sourceDirectory));
        }

        string normalized = baseDirectory
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return new Uri(normalized, UriKind.Absolute).AbsoluteUri;
    }

    /// <summary>
    /// Refreshes git information for the active project.
    /// </summary>
    private async Task RefreshGitAsync()
    {
        IReadOnlyList<GitStatusEntry> status = await _controller.GetGitStatusAsync(ProjectPathEntry.Text ?? string.Empty, CancellationToken.None).ConfigureAwait(true);
        IReadOnlyList<GitHistoryEntry> history = await _controller.GetGitHistoryAsync(ProjectPathEntry.Text ?? string.Empty, 20, CancellationToken.None).ConfigureAwait(true);
        GitStatusView.ItemsSource = status;
        GitHistoryView.ItemsSource = history;
    }

    /// <summary>
    /// Refreshes reference scan information for the active project.
    /// </summary>
    private async Task RefreshReferenceScanAsync()
    {
        ReferenceScanResult scan = await _controller.ScanReferencesAsync(ProjectPathEntry.Text ?? string.Empty, CancellationToken.None).ConfigureAwait(true);
        ReferenceIssuesView.ItemsSource = scan.Issues;
        ReferenceSummaryLabel.Text = $"Files {scan.FilesScanned}, includes {scan.Includes}, macros {scan.Macros}, issues {scan.Issues.Count}";
        ReferenceStatusLabel.Text = $"References: {scan.Includes + scan.Macros} ({scan.Issues.Count} issues)";
        AppendLog($"Reference scan: files={scan.FilesScanned}, includes={scan.Includes}, macros={scan.Macros}, issues={scan.Issues.Count}");
    }

    /// <summary>
    /// Runs an asynchronous action while updating busy-state UI.
    /// </summary>
    /// <param name="actionName">The action label to show.</param>
    /// <param name="action">The asynchronous action to execute.</param>
    private async Task RunBusyAsync(string actionName, Func<Task> action)
    {
        _currentBusyAction = actionName;
        BusyIndicator.IsVisible = true;
        BusyIndicator.IsRunning = true;
        StatusActionLabel.Text = actionName;
        ProgressTextLabel.Text = actionName;
        StatusProgressBar.Progress = 0;
        try
        {
            await action().ConfigureAwait(true);
            StatusProgressBar.Progress = 1;
            StatusActionLabel.Text = "Ready";
            ProgressTextLabel.Text = "Complete";
            _currentBusyAction = "Ready";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            StatusActionLabel.Text = "Error";
            ProgressTextLabel.Text = ShortenStatusText(ex.Message);
            _currentBusyAction = "Error";
            AppendLog(Text("Ui:Status:Error", ex.Message));
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            await Task.Delay(150).ConfigureAwait(true);
            StatusProgressBar.Progress = 0;
        }
    }

    /// <summary>
    /// Updates progress labels using completed and total step counts.
    /// </summary>
    /// <param name="completedSteps">The completed step count.</param>
    /// <param name="totalSteps">The total step count.</param>
    /// <param name="status">The current status text.</param>
    private void SetBusyStepProgress(int completedSteps, int totalSteps, string status)
    {
        if (totalSteps <= 0)
        {
            return;
        }

        double target = Math.Clamp((double)completedSteps / totalSteps, 0, 1);
        if (target > StatusProgressBar.Progress)
        {
            StatusProgressBar.Progress = target;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            StatusActionLabel.Text = status;
            ProgressTextLabel.Text = status;
        }
    }

    /// <summary>
    /// Navigates backward through editor history.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnNavigateBackClicked(object? sender, EventArgs e)
    {
        try
        {
            EditorLocation? current = GetCurrentEditorLocation();
            if (current is null)
            {
                return;
            }

            EditorLocation? destination = _editorWorkspace.NavigateBack(current);
            UpdateNavigationButtons();
            if (destination is not null)
            {
                await NavigateToEditorLocationAsync(destination).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnNavigateBackClicked), ex);
        }
    }

    /// <summary>
    /// Navigates forward through editor history.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnNavigateForwardClicked(object? sender, EventArgs e)
    {
        try
        {
            EditorLocation? current = GetCurrentEditorLocation();
            if (current is null)
            {
                return;
            }

            EditorLocation? destination = _editorWorkspace.NavigateForward(current);
            UpdateNavigationButtons();
            if (destination is not null)
            {
                await NavigateToEditorLocationAsync(destination).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnNavigateForwardClicked), ex);
        }
    }

    /// <summary>
    /// Navigates to a recorded editor location.
    /// </summary>
    /// <param name="destination">The destination location.</param>
    private async Task NavigateToEditorLocationAsync(EditorLocation destination)
    {
        try
        {
            await OpenProjectFileAsync(destination.RelativePath, destination.LineNumber).ConfigureAwait(true);
        }
        finally
        {
            UpdateNavigationButtons();
        }
    }

    /// <summary>
    /// Handles editor tab selection changes.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The selection-change arguments.</param>
    private async void OnEditorTabSelected(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_updatingEditorTabSelection ||
                e.CurrentSelection.Count == 0 ||
                e.CurrentSelection[0] is not EditorTabState tab ||
                ReferenceEquals(tab, _editorWorkspace.ActiveTab))
            {
                return;
            }

            await OpenProjectFileAsync(tab.RelativePath, tab.LineNumber).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnEditorTabSelected), ex);
        }
    }

    /// <summary>
    /// Closes the editor tab associated with the clicked close affordance.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnCloseEditorTabClicked(object? sender, EventArgs e)
    {
        try
        {
            if ((sender as BindableObject)?.BindingContext is not EditorTabState tab)
            {
                return;
            }

            await CaptureActiveEditorStateAsync().ConfigureAwait(true);
            bool closedActive = ReferenceEquals(tab, _editorWorkspace.ActiveTab);
            EditorTabState? next = _editorWorkspace.Close(tab.RelativePath);
            if (closedActive && next is not null)
            {
                ApplyTabToEditor(next, next.LineNumber);
                await RenderCurrentPreviewAsync().ConfigureAwait(true);
            }
            else if (next is null)
            {
                ClearEditorSurface();
            }

            UpdateEditorTabsSelection();
            UpdateNavigationButtons();
        }
        catch (Exception ex) when (IsHandledUiEventException(ex))
        {
            LogAsyncEventHandlerFailure(nameof(OnCloseEditorTabClicked), ex);
        }
    }

    /// <summary>
    /// Records the current editor location into navigation history.
    /// </summary>
    private void RecordCurrentEditorLocation()
    {
        EditorLocation? location = GetCurrentEditorLocation();
        if (location is null)
        {
            return;
        }

        _editorWorkspace.RecordLocation(location);
        _editorWorkspace.UpdateActiveContent(_sourceEditorText, location.LineNumber, _hasUnsavedChanges);
        UpdateCurrentLineLabel(location.LineNumber);
        UpdateNavigationButtons();
    }

    /// <summary>
    /// Gets the current editor location snapshot.
    /// </summary>
    /// <returns>The current editor location, or <see langword="null"/> when unavailable.</returns>
    private EditorLocation? GetCurrentEditorLocation()
    {
        return string.IsNullOrWhiteSpace(_currentFilePath)
            ? null
            : new EditorLocation(_currentFilePath, GetCurrentEditorLineNumber()).Normalize();
    }

    /// <summary>
    /// Gets the current 1-based editor line number.
    /// </summary>
    /// <returns>The current line number.</returns>
    private int GetCurrentEditorLineNumber()
    {
        return Math.Max(1, _sourceEditorLineNumber);
    }

    /// <summary>
    /// Computes the character index for a target line in text.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="lineNumber">The 1-based line number.</param>
    /// <returns>The 0-based character index.</returns>
    private static int GetCharacterIndexForLine(string text, int lineNumber)
    {
        int targetLine = Math.Max(1, lineNumber);
        if (targetLine == 1)
        {
            return 0;
        }

        int currentLine = 1;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            currentLine++;
            if (currentLine == targetLine)
            {
                return Math.Min(index + 1, text.Length);
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Synchronizes selected UI tab state with workspace state.
    /// </summary>
    private void UpdateEditorTabsSelection()
    {
        _updatingEditorTabSelection = true;
        EditorTabsView.SelectedItem = _editorWorkspace.ActiveTab;
        _updatingEditorTabSelection = false;
    }

    /// <summary>
    /// Updates enabled state for editor navigation controls.
    /// </summary>
    private void UpdateNavigationButtons()
    {
        NavigateBackButton.IsEnabled = _editorWorkspace.CanNavigateBack;
        NavigateForwardButton.IsEnabled = _editorWorkspace.CanNavigateForward;
    }

    /// <summary>
    /// Updates the current line label shown in the toolbar.
    /// </summary>
    /// <param name="lineNumber">The current line number.</param>
    private void UpdateCurrentLineLabel(int lineNumber)
    {
        CurrentLineLabel.Text = $"Line {Math.Max(1, lineNumber).ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Handles new entries written to the UI log feed.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The log entry arguments.</param>
    private void OnUiLogEntryWritten(object? sender, UiLogEntryEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            string message = FormatProgressMessage(e.Entry.Message);
            ProgressTextLabel.Text = message;
            double? progress = TryMapPreviewProgress(e.Entry.Message);
            if (progress is not null && progress > StatusProgressBar.Progress)
            {
                StatusProgressBar.Progress = progress.Value;
            }

            if (BusyIndicator.IsRunning)
            {
                StatusActionLabel.Text = _currentBusyAction;
            }
        });
    }

    /// <summary>
    /// Normalizes progress messages for compact status display.
    /// </summary>
    /// <param name="message">The raw progress message.</param>
    /// <returns>The normalized status message.</returns>
    private static string FormatProgressMessage(string message)
    {
        string normalized = message.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= 140 ? normalized : "..." + normalized[^137..];
    }
}
