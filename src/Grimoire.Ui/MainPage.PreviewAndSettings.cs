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
/// Provides preview-navigation and settings-editor helpers for <see cref="MainPage"/>.
/// </summary>
public partial class MainPage : ContentPage
{
    /// <summary>
    /// Extracts a decoded query value from a URL-like string.
    /// </summary>
    /// <param name="queryOrUrl">The full query string or URL.</param>
    /// <param name="key">The query key to extract.</param>
    /// <returns>The decoded query value, or <see langword="null"/> when the key is absent.</returns>
    private static string? ExtractQueryValue(string queryOrUrl, string key)
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

        return Uri.UnescapeDataString(encoded).Replace('\\', '/');
    }

    /// <summary>
    /// Resolves a preview navigation URL to a project-relative source path.
    /// </summary>
    /// <param name="url">The URL requested by the preview surface.</param>
    /// <returns>The resolved project-relative path, or <see langword="null"/> when no target exists.</returns>
    private string? ResolvePreviewNavigationPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || _previewNavigationTargets.Count == 0)
        {
            return null;
        }

        string normalized = NormalizePreviewNavigationKey(url);
        if (_previewNavigationTargets.TryGetValue(normalized, out string? directMatch))
        {
            return directMatch;
        }

        return null;
    }

    /// <summary>
    /// Resolves a preview URL to a project-relative source path.
    /// </summary>
    /// <param name="url">The URL to resolve.</param>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="currentFilePath">The current source file path used for relative navigation.</param>
    /// <returns>The matching project-relative source path, or <see langword="null"/> when unresolved.</returns>
    private static string? ResolvePreviewProjectRelativePath(string url, string projectPath, string? currentFilePath)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        string projectRoot = Path.GetFullPath(projectPath);
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absoluteUri))
        {
            if (absoluteUri.IsFile)
            {
                return TryConvertProjectPathToRelative(absoluteUri.LocalPath, projectRoot);
            }

            return null;
        }

        string rawPath = StripPreviewQueryAndFragment(url);
        return IsNavigableSourcePath(rawPath)
            ? TryResolveRelativePreviewPath(rawPath, projectRoot, currentFilePath)
            : null;
    }

    /// <summary>
    /// Removes query and fragment segments from a preview URL.
    /// </summary>
    /// <param name="url">The URL or path value.</param>
    /// <returns>The normalized URL path segment.</returns>
    private static string StripPreviewQueryAndFragment(string url)
    {
        int queryIndex = url.IndexOfAny(['?', '#']);
        string value = queryIndex >= 0 ? url[..queryIndex] : url;
        return Uri.UnescapeDataString(value)
            .Replace('\\', '/')
            .Trim();
    }

    /// <summary>
    /// Determines whether a path can be opened in the source editor.
    /// </summary>
    /// <param name="path">The relative or absolute path.</param>
    /// <returns><see langword="true"/> when the path references a supported source extension.</returns>
    private static bool IsNavigableSourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a preview path relative to the current file or project root.
    /// </summary>
    /// <param name="path">The path extracted from a preview link.</param>
    /// <param name="projectRoot">The absolute project root path.</param>
    /// <param name="currentFilePath">The active file path used as a base for relative links.</param>
    /// <returns>The project-relative path when resolvable; otherwise, <see langword="null"/>.</returns>
    private static string? TryResolveRelativePreviewPath(string path, string projectRoot, string? currentFilePath)
    {
        string trimmedPath = path.Trim().Replace('\\', '/');
        bool rootRelative = trimmedPath.Length > 0 && trimmedPath[0] == '/';
        string candidate = rootRelative ? trimmedPath.TrimStart('/') : trimmedPath;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string currentDirectory = rootRelative || string.IsNullOrWhiteSpace(currentFilePath)
            ? string.Empty
            : Path.GetDirectoryName(currentFilePath) ?? string.Empty;
        string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, currentDirectory, candidate));
        return TryConvertProjectPathToRelative(absolutePath, projectRoot);
    }

    /// <summary>
    /// Converts an absolute path to a project-relative source path when valid.
    /// </summary>
    /// <param name="absolutePath">The absolute path to convert.</param>
    /// <param name="projectRoot">The absolute project root path.</param>
    /// <returns>The validated project-relative source path, or <see langword="null"/>.</returns>
    private static string? TryConvertProjectPathToRelative(string absolutePath, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        string normalizedRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedPath = Path.GetFullPath(absolutePath);
        if (!normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(normalizedPath))
        {
            return null;
        }

        string relativePath = Path.GetRelativePath(normalizedRoot, normalizedPath).Replace('\\', '/');
        return IsNavigableSourcePath(relativePath)
            ? relativePath
            : null;
    }

    /// <summary>
    /// Builds normalized navigation targets for preview links.
    /// </summary>
    /// <param name="linkTargets">The known preview link-to-source map.</param>
    /// <param name="baseUrl">The preview base URL.</param>
    /// <returns>A normalized lookup of preview URL keys to source paths.</returns>
    private static Dictionary<string, string> BuildPreviewNavigationTargets(IReadOnlyDictionary<string, string> linkTargets, string baseUrl)
    {
        Dictionary<string, string> targets = new(StringComparer.OrdinalIgnoreCase);
        Uri? baseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsedBaseUri) ? parsedBaseUri : null;
        foreach ((string href, string sourcePath) in linkTargets)
        {
            AddPreviewNavigationTarget(targets, href, sourcePath);
            AddPreviewNavigationTarget(targets, "grimoire://open?path=" + Uri.EscapeDataString(sourcePath), sourcePath);
            AddPreviewNavigationTarget(targets, "grimoire://open/?path=" + Uri.EscapeDataString(sourcePath), sourcePath);
            if (baseUri is not null && Uri.TryCreate(baseUri, href, out Uri? absolute))
            {
                AddPreviewNavigationTarget(targets, absolute.AbsoluteUri, sourcePath);
            }
        }

        return targets;
    }

    /// <summary>
    /// Adds a normalized preview navigation key to the target map.
    /// </summary>
    /// <param name="targets">The target dictionary to update.</param>
    /// <param name="href">The preview hyperlink value.</param>
    /// <param name="sourcePath">The destination source path.</param>
    private static void AddPreviewNavigationTarget(Dictionary<string, string> targets, string href, string sourcePath)
    {
        string normalized = NormalizePreviewNavigationKey(href);
        if (!string.IsNullOrWhiteSpace(normalized) && !targets.ContainsKey(normalized))
        {
            targets[normalized] = sourcePath;
        }
    }

    /// <summary>
    /// Normalizes a preview link value for dictionary lookup.
    /// </summary>
    /// <param name="value">The raw link value.</param>
    /// <returns>The normalized lookup key.</returns>
    private static string NormalizePreviewNavigationKey(string value)
    {
        string normalized = Uri.UnescapeDataString(value.Trim())
            .Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    /// <summary>
    /// Opens a file or directory in the operating system file explorer.
    /// </summary>
    /// <param name="path">The path to reveal.</param>
    private static void ShowInFilesystem(string path)
    {
        string fileName;
        string arguments;
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            fileName = "open";
            arguments = $"-R \"{path}\"";
        }
        else if (OperatingSystem.IsWindows())
        {
            fileName = "explorer";
            arguments = $"/select,\"{path}\"";
        }
        else
        {
            fileName = "xdg-open";
            arguments = $"\"{(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path)}\"";
        }

        Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false });
    }

    /// <summary>
    /// Resolves a localized text resource.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <returns>The localized string value.</returns>
    private string Text(string key)
    {
        return _localizer[key].Value;
    }

    /// <summary>
    /// Resolves a formatted localized text resource.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="arguments">The format arguments.</param>
    /// <returns>The localized and formatted string value.</returns>
    private string Text(string key, params object[] arguments)
    {
        return _localizer[key, arguments].Value;
    }

    /// <summary>
    /// Represents the supported settings profiles.
    /// </summary>
    private enum SettingsProfile
    {
        /// <summary>
        /// Indicates the global project settings profile.
        /// </summary>
        Global,
        /// <summary>
        /// Indicates the HTML output settings profile.
        /// </summary>
        Html,
        /// <summary>
        /// Indicates the PDF output settings profile.
        /// </summary>
        Pdf,
        /// <summary>
        /// Indicates the Foundry output settings profile.
        /// </summary>
        Foundry,
    }

    /// <summary>
    /// Represents the supported settings field editor types.
    /// </summary>
    private enum SettingFieldType
    {
        /// <summary>
        /// Indicates a single-line text setting field.
        /// </summary>
        Text,
        /// <summary>
        /// Indicates a multi-line text setting field.
        /// </summary>
        Multiline,
        /// <summary>
        /// Indicates a boolean setting field.
        /// </summary>
        Boolean,
        /// <summary>
        /// Indicates an integer setting field.
        /// </summary>
        Integer,
        /// <summary>
        /// Indicates a constrained choice setting field.
        /// </summary>
        Choice,
        /// <summary>
        /// Indicates a list-based setting field.
        /// </summary>
        List,
    }

    /// <summary>
    /// Represents the dockable pane identities.
    /// </summary>
    private enum DockPaneKind
    {
        /// <summary>
        /// Indicates the project pane.
        /// </summary>
        Project,
        /// <summary>
        /// Indicates the tools pane.
        /// </summary>
        Tools,
        /// <summary>
        /// Indicates the bottom pane.
        /// </summary>
        Bottom,
    }

    /// <summary>
    /// Represents available docking targets.
    /// </summary>
    private enum DockTarget
    {
        /// <summary>
        /// Indicates docking to the left edge.
        /// </summary>
        Left,
        /// <summary>
        /// Indicates docking to the right edge.
        /// </summary>
        Right,
        /// <summary>
        /// Indicates docking to the bottom edge.
        /// </summary>
        Bottom,
    }

    /// <summary>
    /// Defines metadata and behavior for a settings editor field.
    /// </summary>
    private sealed record SettingsFieldDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsFieldDefinition"/> record.
        /// </summary>
        /// <param name="Key">The settings key path.</param>
        /// <param name="Label">The field label shown in the editor.</param>
        /// <param name="Type">The field editor type.</param>
        /// <param name="Section">The settings section heading.</param>
        /// <param name="DefaultValue">The optional default value.</param>
        /// <param name="Choices">The optional set of allowed values.</param>
        /// <param name="Inheritable">A value indicating whether this field can inherit from global settings.</param>
        public SettingsFieldDefinition(
            string Key,
            string Label,
            SettingFieldType Type,
            string Section,
            string? DefaultValue = null,
            IReadOnlyList<string>? Choices = null,
            bool Inheritable = false)
        {
            this.Key = Key;
            this.Label = Label;
            this.Type = Type;
            this.Section = Section;
            this.DefaultValue = DefaultValue;
            this.Choices = Choices ?? [];
            this.Inheritable = Inheritable;
        }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the settings key path.
        /// </summary>
        public string Key { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the display label.
        /// </summary>
        public string Label { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="SettingFieldType"/> indicating the editor control type.
        /// </summary>
        public SettingFieldType Type { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the field section heading.
        /// </summary>
        public string Section { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the default field value.
        /// </summary>
        public string? DefaultValue { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyList{T}"/> representing the available choices for the field.
        /// </summary>
        public IReadOnlyList<string> Choices { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether the field supports inheritance.
        /// </summary>
        public bool Inheritable { get; init; }
    }

    /// <summary>
    /// Stores runtime controls and metadata for one settings field row.
    /// </summary>
    private sealed class SettingsFieldState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsFieldState"/> class.
        /// </summary>
        /// <param name="definition">The field definition.</param>
        /// <param name="container">The container layout for the field.</param>
        /// <param name="inputControl">The field input control.</param>
        /// <param name="statusLabel">The field status label.</param>
        /// <param name="overrideSwitch">The optional override toggle.</param>
        /// <param name="hasInheritedValue">A value indicating whether the field currently inherits a value.</param>
        public SettingsFieldState(
            SettingsFieldDefinition definition,
            VerticalStackLayout container,
            View inputControl,
            Label statusLabel,
            Microsoft.Maui.Controls.Switch? overrideSwitch,
            bool hasInheritedValue)
        {
            Definition = definition;
            Container = container;
            InputControl = inputControl;
            StatusLabel = statusLabel;
            OverrideSwitch = overrideSwitch;
            HasInheritedValue = hasInheritedValue;
        }

        /// <summary>
        /// Gets a <see cref="SettingsFieldDefinition"/> representing the field metadata.
        /// </summary>
        public SettingsFieldDefinition Definition { get; }

        /// <summary>
        /// Gets a <see cref="VerticalStackLayout"/> representing the field container.
        /// </summary>
        public VerticalStackLayout Container { get; }

        /// <summary>
        /// Gets a <see cref="View"/> representing the field input control.
        /// </summary>
        public View InputControl { get; }

        /// <summary>
        /// Gets a <see cref="Label"/> representing the field status label.
        /// </summary>
        public Label StatusLabel { get; }

        /// <summary>
        /// Gets a <see cref="Microsoft.Maui.Controls.Switch"/> representing the optional override toggle.
        /// </summary>
        public Microsoft.Maui.Controls.Switch? OverrideSwitch { get; }

        /// <summary>
        /// Gets a value indicating whether the field currently inherits its value.
        /// </summary>
        public bool HasInheritedValue { get; }
    }

    /// <summary>
    /// Tracks the active settings editing session state.
    /// </summary>
    private sealed class SettingsEditorSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsEditorSession"/> class.
        /// </summary>
        /// <param name="profile">The active settings profile.</param>
        /// <param name="localSettings">The profile-specific settings document.</param>
        /// <param name="globalSettings">The global settings document.</param>
        /// <param name="fields">The fields available in this editor session.</param>
        public SettingsEditorSession(
            SettingsProfile profile,
            YamlSettingsDocument localSettings,
            YamlSettingsDocument globalSettings,
            IReadOnlyList<SettingsFieldDefinition> fields)
        {
            Profile = profile;
            LocalSettings = localSettings;
            GlobalSettings = globalSettings;
            Fields = fields;
        }

        /// <summary>
        /// Gets a <see cref="SettingsProfile"/> representing the active profile.
        /// </summary>
        public SettingsProfile Profile { get; }

        /// <summary>
        /// Gets a <see cref="YamlSettingsDocument"/> representing the profile-specific settings.
        /// </summary>
        public YamlSettingsDocument LocalSettings { get; }

        /// <summary>
        /// Gets a <see cref="YamlSettingsDocument"/> representing the inherited global settings.
        /// </summary>
        public YamlSettingsDocument GlobalSettings { get; }

        /// <summary>
        /// Gets a <see cref="IReadOnlyList{T}"/> representing the editable field definitions.
        /// </summary>
        public IReadOnlyList<SettingsFieldDefinition> Fields { get; }

        /// <summary>
        /// Gets a <see cref="List{T}"/> representing the runtime field state collection.
        /// </summary>
        public List<SettingsFieldState> FieldStates { get; } = [];
    }

    /// <summary>
    /// Represents a flattened settings document with YAML conversion helpers.
    /// </summary>
    private sealed class YamlSettingsDocument
    {
        /// <summary>
        /// Stores flattened settings values keyed by dotted paths.
        /// </summary>
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Attempts to resolve a stored value by key.
        /// </summary>
        /// <param name="key">The dotted settings key.</param>
        /// <param name="value">The resolved value when found.</param>
        /// <returns><see langword="true"/> when a value exists for the key; otherwise, <see langword="false"/>.</returns>
        public bool TryGetValue(string key, out object? value)
        {
            if (_values.TryGetValue(key, out object? resolved))
            {
                value = resolved;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Gets the value for a settings key when present.
        /// </summary>
        /// <param name="key">The dotted settings key.</param>
        /// <returns>The stored value, or <see langword="null"/> when absent.</returns>
        public object? GetValue(string key)
        {
            return _values.TryGetValue(key, out object? value) ? value : null;
        }

        /// <summary>
        /// Sets a settings value for the specified key.
        /// </summary>
        /// <param name="key">The dotted settings key.</param>
        /// <param name="value">The value to store.</param>
        public void SetValue(string key, object value)
        {
            _values[key] = value;
        }

        /// <summary>
        /// Removes a settings key from the document.
        /// </summary>
        /// <param name="key">The dotted settings key to remove.</param>
        public void Remove(string key)
        {
            _values.Remove(key);
        }

        /// <summary>
        /// Creates a deep copy of the settings document.
        /// </summary>
        /// <returns>A cloned <see cref="YamlSettingsDocument"/> instance.</returns>
        public YamlSettingsDocument Clone()
        {
            YamlSettingsDocument clone = new();
            foreach ((string key, object value) in _values)
            {
                clone._values[key] = value is string[] items ? (string[])[.. items] : value;
            }

            return clone;
        }

        /// <summary>
        /// Serializes stored settings values to YAML.
        /// </summary>
        /// <returns>The YAML representation of the document.</returns>
        public string ToYaml()
        {
            SortedDictionary<string, object> root = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string path, object value) in _values.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Length == 0)
                {
                    continue;
                }

                InsertPath(root, segments, value);
            }

            YamlMappingNode mapping = ToYamlMappingNode(root);
            YamlStream stream = new(new YamlDocument(mapping));
            using StringWriter writer = new(CultureInfo.InvariantCulture);
            stream.Save(writer, assignAnchors: false);
            return writer.ToString();
        }

        /// <summary>
        /// Parses a YAML string into a flattened settings document.
        /// </summary>
        /// <param name="yaml">The YAML content to parse.</param>
        /// <returns>A populated <see cref="YamlSettingsDocument"/> instance.</returns>
        public static YamlSettingsDocument Parse(string yaml)
        {
            YamlSettingsDocument document = new();
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return document;
            }

            YamlStream stream = new();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                return document;
            }

            FlattenMapping(document._values, string.Empty, mapping);
            return document;
        }

        /// <summary>
        /// Inserts a dotted path into a nested object graph.
        /// </summary>
        /// <param name="parent">The root map to mutate.</param>
        /// <param name="segments">The path segments.</param>
        /// <param name="value">The leaf value.</param>
        private static void InsertPath(IDictionary<string, object> parent, string[] segments, object value)
        {
            IDictionary<string, object> cursor = parent;
            for (int index = 0; index < segments.Length; index++)
            {
                string segment = segments[index];
                bool isLeaf = index == segments.Length - 1;
                if (isLeaf)
                {
                    cursor[segment] = value;
                    return;
                }

                if (!cursor.TryGetValue(segment, out object? child) || child is not IDictionary<string, object> childMap)
                {
                    childMap = new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    cursor[segment] = childMap;
                }

                cursor = childMap;
            }
        }

        /// <summary>
        /// Converts a key/value map into a YAML mapping node.
        /// </summary>
        /// <param name="map">The map to convert.</param>
        /// <returns>A <see cref="YamlMappingNode"/> representing the map.</returns>
        private static YamlMappingNode ToYamlMappingNode(IEnumerable<KeyValuePair<string, object>> map)
        {
            YamlMappingNode node = new();
            foreach ((string key, object value) in map)
            {
                node.Children.Add(new YamlScalarNode(key), ToYamlNode(value));
            }

            return node;
        }

        /// <summary>
        /// Converts a CLR value into a YAML node.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>A <see cref="YamlNode"/> representation of <paramref name="value"/>.</returns>
        private static YamlNode ToYamlNode(object value)
        {
            return value switch
            {
                IDictionary<string, object> nested => ToYamlMappingNode(nested),
                string[] sequence => new YamlSequenceNode(sequence.Select(static item => new YamlScalarNode(item))),
                bool boolean => new YamlScalarNode(boolean ? "true" : "false"),
                int number => new YamlScalarNode(number.ToString(CultureInfo.InvariantCulture)),
                _ => new YamlScalarNode(value.ToString() ?? string.Empty),
            };
        }

        /// <summary>
        /// Flattens a YAML mapping into dotted-path values.
        /// </summary>
        /// <param name="target">The flattened value dictionary.</param>
        /// <param name="path">The current dotted prefix.</param>
        /// <param name="mapping">The mapping node to flatten.</param>
        private static void FlattenMapping(Dictionary<string, object> target, string path, YamlMappingNode mapping)
        {
            foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
            {
                string key = (keyNode as YamlScalarNode)?.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string childPath = string.IsNullOrWhiteSpace(path) ? key : $"{path}.{key}";
                switch (valueNode)
                {
                    case YamlMappingNode childMapping:
                        FlattenMapping(target, childPath, childMapping);
                        break;
                    case YamlSequenceNode sequence:
                        target[childPath] = (string[])
                        [
                            .. sequence.Children
                                .OfType<YamlScalarNode>()
                                .Select(static item => item.Value ?? string.Empty)
                                .Where(static item => !string.IsNullOrWhiteSpace(item)),
                        ];
                        break;
                    case YamlScalarNode scalar:
                        if (bool.TryParse(scalar.Value, out bool boolValue))
                        {
                            target[childPath] = boolValue;
                        }
                        else if (int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        {
                            target[childPath] = number;
                        }
                        else
                        {
                            target[childPath] = scalar.Value ?? string.Empty;
                        }

                        break;
                }
            }
        }
    }

    /// <summary>
    /// Represents a source-editor navigation message payload.
    /// </summary>
    private sealed record SourceEditorMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SourceEditorMessage"/> record.
        /// </summary>
        /// <param name="text">The editor text payload.</param>
        /// <param name="line">The associated line number.</param>
        public SourceEditorMessage(string text, int line)
        {
            Text = text;
            Line = line;
        }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the source editor text payload.
        /// </summary>
        public string Text { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="int"/> indicating the associated line number.
        /// </summary>
        public int Line { get; init; }
    }

    /// <summary>
    /// Represents a source-editor interop payload transported through WebView messaging.
    /// </summary>
    private sealed record SourceEditorInteropPayload
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SourceEditorInteropPayload"/> record.
        /// </summary>
        /// <param name="type">The payload type discriminator.</param>
        /// <param name="text">The editor text payload.</param>
        public SourceEditorInteropPayload(string type, string text)
        {
            Type = type;
            Text = text;
        }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the payload type discriminator.
        /// </summary>
        [property: JsonPropertyName("type")]
        public string Type { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the payload text.
        /// </summary>
        [property: JsonPropertyName("text")]
        public string Text { get; init; }
    }

    /// <summary>
    /// Represents a rendered row in the project tree UI.
    /// </summary>
    private sealed record ProjectTreeRow
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectTreeRow"/> record.
        /// </summary>
        /// <param name="Item">The backing project file item.</param>
        /// <param name="IsExpanded">A value indicating whether the row is expanded.</param>
        public ProjectTreeRow(ProjectFileItem Item, bool IsExpanded)
        {
            this.Item = Item;
            this.IsExpanded = IsExpanded;
        }

        /// <summary>
        /// Gets or sets a <see cref="ProjectFileItem"/> representing the backing tree item.
        /// </summary>
        public ProjectFileItem Item { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether the row is currently expanded.
        /// </summary>
        public bool IsExpanded { get; init; }

        /// <summary>
        /// Gets a <see cref="string"/> representing the display name of the item.
        /// </summary>
        public string Name => Item.Name;

        /// <summary>
        /// Gets a <see cref="string"/> representing the project-relative path.
        /// </summary>
        public string RelativePath => Item.RelativePath;

        /// <summary>
        /// Gets a value indicating whether the row represents a directory.
        /// </summary>
        public bool IsDirectory => Item.IsDirectory;

        /// <summary>
        /// Gets a <see cref="string"/> representing the logical item kind.
        /// </summary>
        public string Kind => Item.Kind;

        /// <summary>
        /// Gets a <see cref="string"/> representing the parent directory path.
        /// </summary>
        public string ParentPath => Item.ParentPath;

        /// <summary>
        /// Gets a <see cref="string"/> representing the logical icon name.
        /// </summary>
        public string Icon => Item.Icon;

        /// <summary>
        /// Gets a <see cref="string"/> representing the Material icon glyph.
        /// </summary>
        public string IconGlyph => Item.Icon switch
        {
            "book" => "menu_book",
            "map" => "map",
            "gear" => "settings",
            "bestiary" => "pets",
            "bag" => "inventory_2",
            "people" => "groups",
            "spark" => "auto_awesome",
            "template" => "dashboard_customize",
            "cert" => "workspace_premium",
            "cite" => "format_quote",
            "cover" => "title",
            "snippet-md" => "edit_note",
            "page" => "article",
            "code" => "data_object",
            "asset" => "image",
            "folder" => "folder",
            _ => "description",
        };

        /// <summary>
        /// Gets a <see cref="string"/> representing indentation metadata for tree rendering.
        /// </summary>
        public string TreeIndent => Item.TreeIndent;

        /// <summary>
        /// Gets a <see cref="string"/> representing the expand/collapse glyph for directory rows.
        /// </summary>
        public string ExpandGlyph => IsDirectory ? IsExpanded ? "expand_more" : "chevron_right" : string.Empty;
    }
}
