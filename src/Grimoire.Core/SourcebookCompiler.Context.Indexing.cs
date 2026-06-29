using Microsoft.Extensions.Configuration;
using PuppeteerSharp.Media;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Grimoire.Core;

/// <summary>
/// Loads index, settings, and entry data used by sourcebook compilation.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Loads index topics from chapters and referenceable material files.
    /// </summary>
    private async Task<List<IndexTopic>> LoadIndexTopicsAsync(
        string sourceRoot,
        bool appendUnreferencedMaterials,
        bool generateReferenceDictionary,
        IReadOnlyCollection<string> shadowReferences,
        CancellationToken cancellationToken)
    {
        List<IndexTopic> topics = [];
        HashSet<string> indexCandidatePaths = new(StringComparer.OrdinalIgnoreCase);
        string contentRoot = Path.Combine(sourceRoot, "content");
        if (Directory.Exists(contentRoot))
        {
            foreach (string filePath in Directory.GetFiles(contentRoot, "*.md", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                indexCandidatePaths.Add(Path.GetFullPath(filePath));
            }

            foreach (string filePath in Directory.GetFiles(contentRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                indexCandidatePaths.Add(Path.GetFullPath(filePath));
            }
        }

        HashSet<string> referencedMaterialPathsForIndex = await LoadReferencedMaterialPathsAsync(sourceRoot, includeMacros: true, cancellationToken).ConfigureAwait(false);
        foreach (string path in referencedMaterialPathsForIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            indexCandidatePaths.Add(path);
        }

        Dictionary<string, HashSet<string>> referencedMaterialTargetIds = await LoadReferencedMaterialTargetIdsAsync(
            sourceRoot,
            includeMacros: true,
            cancellationToken).ConfigureAwait(false);

        HashSet<string> unreferencedMaterialPaths = new(StringComparer.OrdinalIgnoreCase);
        if (appendUnreferencedMaterials)
        {
            HashSet<string> referenceableMaterialPaths = LoadReferenceableMaterialPaths(sourceRoot);
            PreviewReferenceableMaterialsFound(referenceableMaterialPaths.Count);
            HashSet<string> referencedMaterialPaths = await LoadReferencedMaterialPathsAsync(sourceRoot, includeMacros: true, cancellationToken).ConfigureAwait(false);
            unreferencedMaterialPaths = new(
                referenceableMaterialPaths.Where(path => !referencedMaterialPaths.Contains(path)),
                StringComparer.OrdinalIgnoreCase);

            foreach (string path in unreferencedMaterialPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                indexCandidatePaths.Add(path);
            }
        }

        HashSet<string> referenceDictionaryPaths = new(StringComparer.OrdinalIgnoreCase);
        if (generateReferenceDictionary)
        {
            HashSet<string> referenceableMaterialPaths = LoadReferenceableMaterialPaths(sourceRoot);
            PreviewReferenceableMaterialsFound(referenceableMaterialPaths.Count);
            foreach (string materialPath in referenceableMaterialPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (referencedMaterialTargetIds.ContainsKey(materialPath))
                {
                    referenceDictionaryPaths.Add(materialPath);
                    indexCandidatePaths.Add(materialPath);
                }
            }

            HashSet<string> shadowReferencePaths = LoadShadowReferenceMaterialPaths(sourceRoot, shadowReferences);
            foreach (string shadowPath in shadowReferencePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                referenceDictionaryPaths.Add(shadowPath);
                indexCandidatePaths.Add(shadowPath);
            }
        }

        string[] orderedIndexCandidatePaths = [.. indexCandidatePaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)];
        PreviewIndexCandidatesFound(orderedIndexCandidatePaths.Length);
        int indexedCandidate = 0;
        foreach (string filePath in orderedIndexCandidatePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            indexedCandidate++;
            string extension = Path.GetExtension(filePath);
            if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceRoot, filePath);
            PreviewIndexingTopic(indexedCandidate, orderedIndexCandidatePaths.Length, relativePath);
            string rootFileName = Path.GetFileName(relativePath);
            if (string.Equals(rootFileName, "TEMPLATE.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalizedFilePath = Path.GetFullPath(filePath);
            if (string.Equals(rootFileName, "README.md", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rootFileName, "TITLE.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string topicTitle = Path.GetFileNameWithoutExtension(filePath);
            if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
            {
                string raw = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                ParsedMarkdown parsed = ParseMarkdownDocument(raw);
                topicTitle = FirstNonEmpty(GetValue(parsed.FrontMatter, "title"), GetValue(parsed.FrontMatter, "name"), ResolveSectionTitle(filePath, parsed));
            }
            else
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false));
                JsonElement root = json.RootElement;
                string? jsonTitle = ResolvePreferredJsonTitle(root, filePath);
                if (!string.IsNullOrWhiteSpace(jsonTitle))
                {
                    topicTitle = jsonTitle;
                }
                else if (TryParseDndBeyondNumberedFileInfo(filePath, out _, out string characterName, out _))
                {
                    topicTitle = NormalizeEntityTitle(characterName);
                }
            }

            if (string.Equals(topicTitle, "README", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(topicTitle, "TITLE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetId;
            List<string> targetIds = [];
            if (relativePath.StartsWith($"content{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                targetId = BuildSectionId(relativePath);
                targetIds.Add(targetId);
            }
            else
            {
                targetId = BuildMaterialAnchorId(sourceRoot, normalizedFilePath);
                if (referencedMaterialTargetIds.TryGetValue(normalizedFilePath, out HashSet<string>? referenceTargets))
                {
                    targetIds.AddRange(referenceTargets.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase));
                }

                if (appendUnreferencedMaterials && unreferencedMaterialPaths.Contains(normalizedFilePath))
                {
                    targetIds.Add(targetId);
                }

                if (generateReferenceDictionary && referenceDictionaryPaths.Contains(normalizedFilePath))
                {
                    targetIds.Add(BuildReferenceDictionaryMaterialAnchorId(sourceRoot, normalizedFilePath));
                }

                if (targetIds.Count == 0)
                {
                    targetIds.Add(targetId);
                }
            }

            topics.Add(new IndexTopic(topicTitle, relativePath, $"idx-{BuildSectionId(relativePath)}", [.. targetIds.Distinct(StringComparer.OrdinalIgnoreCase)]));
        }

        topics.Sort(static (left, right) => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));
        return topics;
    }

    /// <summary>
    /// Loads and renders bibliography markdown into HTML.
    /// </summary>
    private async Task<string?> LoadBibliographyHtmlAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        string sourcesPath = Path.Combine(sourceRoot, "SOURCES.md");
        if (!File.Exists(sourcesPath))
        {
            return null;
        }

        string raw = await File.ReadAllTextAsync(sourcesPath, cancellationToken).ConfigureAwait(false);
        ParsedMarkdown parsed = ParseMarkdownDocument(raw);
        string processed = ProcessInlineTokens(parsed.Body, sourcesPath, sourceRoot, currentPageTitleOverride: "Bibliography", contentPageTitleOverride: "Bibliography");
        return RenderMarkdownToHtml(processed);
    }

    /// <summary>
    /// Loads parsed front matter from a markdown file when it exists.
    /// </summary>
    private static async Task<Dictionary<string, string>> LoadFrontMatterFromFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return ParseMarkdownDocument(raw).FrontMatter;
    }

    /// <summary>
    /// Loads YAML settings and flattens nested values into dotted keys.
    /// </summary>
    private static Dictionary<string, string> LoadYamlSettings(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        Dictionary<string, string> result = [];
        object loaded = YamlDeserializer.Deserialize<object>(File.ReadAllText(path));
        FlattenYamlSettings(loaded, prefix: null, result);
        return result;
    }

    /// <summary>
    /// Flattens nested YAML values into a key-value map.
    /// </summary>
    private static void FlattenYamlSettings(object? value, string? prefix, Dictionary<string, string> output)
    {
        if (value is Dictionary<object, object> map)
        {
            foreach ((object mapKey, object mapValue) in map)
            {
                string normalizedKey = Convert.ToString(mapKey, CultureInfo.InvariantCulture) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(normalizedKey))
                {
                    continue;
                }

                string childPrefix = string.IsNullOrWhiteSpace(prefix) ? normalizedKey : $"{prefix}.{normalizedKey}";
                FlattenYamlSettings(mapValue, childPrefix, output);
            }

            return;
        }

        if (value is System.Collections.IEnumerable list and not string)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return;
            }

            string serialized = string.Join(
                ", ",
                list.Cast<object?>()
                    .Select(static item => Convert.ToString(item, CultureInfo.InvariantCulture)?.Trim())
                    .Where(static item => !string.IsNullOrWhiteSpace(item)));
            output[prefix] = serialized;
            return;
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return;
        }

        output[prefix] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Loads a string list value from YAML using a dotted key path.
    /// </summary>
    private static string[] LoadYamlStringList(string path, string key)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        object loaded = YamlDeserializer.Deserialize<object>(File.ReadAllText(path));
        if (!TryResolveYamlValue(loaded, key, out object? value) || value is null)
        {
            return [];
        }

        if (value is System.Collections.IEnumerable list and not string)
        {
            return
            [
                .. list
                .Cast<object?>()
                .Select(static item => Convert.ToString(item, CultureInfo.InvariantCulture)?.Trim())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!)
                .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
        }

        string? scalar = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return [];
        }

        return [scalar];
    }

    /// <summary>
    /// Tries to resolve a value from a YAML object graph using dotted key notation.
    /// </summary>
    private static bool TryResolveYamlValue(object? root, string keyPath, out object? value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        string[] segments = keyPath.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (string segment in segments)
        {
            if (value is not Dictionary<object, object> map)
            {
                value = null;
                return false;
            }

            bool found = false;
            foreach ((object mapKey, object mapValue) in map)
            {
                string normalized = Convert.ToString(mapKey, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(normalized, segment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = mapValue;
                found = true;
                break;
            }

            if (!found)
            {
                value = null;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Loads effective render options for the requested export target.
    /// </summary>
    private static RenderOptions LoadRenderOptions(string sourceRoot, ExportTarget target)
    {
        Dictionary<string, string> globalSettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "global.yml"));
        Dictionary<string, string> htmlSettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "html.yml"));
        Dictionary<string, string> pdfSettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "pdf.yml"));
        Dictionary<string, string> foundrySettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "foundry.yml"));
        string[] globalShadowReferences = LoadYamlStringList(Path.Combine(sourceRoot, "settings", "global.yml"), CompilerDictionaryShadowReferencesSettingKey);
        string[] htmlShadowReferences = LoadYamlStringList(Path.Combine(sourceRoot, "settings", "html.yml"), CompilerDictionaryShadowReferencesSettingKey);
        string[] pdfShadowReferences = LoadYamlStringList(Path.Combine(sourceRoot, "settings", "pdf.yml"), CompilerDictionaryShadowReferencesSettingKey);
        string[] foundryShadowReferences = LoadYamlStringList(Path.Combine(sourceRoot, "settings", "foundry.yml"), CompilerDictionaryShadowReferencesSettingKey);

        bool includeUnreferencedSnippetsInAppendix = target switch
        {
            ExportTarget.Pdf => FirstNonNullBoolean(GetBooleanValue(pdfSettings, CompilerDictionaryUnreferencedSettingKey), GetBooleanValue(globalSettings, CompilerDictionaryUnreferencedSettingKey)) ?? false,
            ExportTarget.FoundryDb => FirstNonNullBoolean(GetBooleanValue(foundrySettings, CompilerDictionaryUnreferencedSettingKey), GetBooleanValue(globalSettings, CompilerDictionaryUnreferencedSettingKey)) ?? false,
            _ => FirstNonNullBoolean(GetBooleanValue(htmlSettings, CompilerDictionaryUnreferencedSettingKey), GetBooleanValue(globalSettings, CompilerDictionaryUnreferencedSettingKey)) ?? false,
        };

        bool generateReferenceDictionary = target switch
        {
            ExportTarget.Pdf => FirstNonNullBoolean(GetBooleanValue(pdfSettings, CompilerDictionaryEnabledSettingKey), GetBooleanValue(globalSettings, CompilerDictionaryEnabledSettingKey)) ?? false,
            ExportTarget.FoundryDb => FirstNonNullBoolean(GetBooleanValue(foundrySettings, CompilerDictionaryEnabledSettingKey), GetBooleanValue(globalSettings, CompilerDictionaryEnabledSettingKey)) ?? false,
            _ => FirstNonNullBoolean(GetBooleanValue(htmlSettings, CompilerDictionaryEnabledSettingKey), GetBooleanValue(globalSettings, CompilerDictionaryEnabledSettingKey)) ?? false,
        };

        bool includePageLevelToc = target switch
        {
            ExportTarget.Pdf => FirstNonNullBoolean(GetBooleanValue(pdfSettings, CompilerScreenPageLevelTocSettingKey), GetBooleanValue(globalSettings, CompilerScreenPageLevelTocSettingKey)) ?? true,
            ExportTarget.FoundryDb => FirstNonNullBoolean(GetBooleanValue(foundrySettings, CompilerScreenPageLevelTocSettingKey), GetBooleanValue(globalSettings, CompilerScreenPageLevelTocSettingKey)) ?? true,
            _ => FirstNonNullBoolean(GetBooleanValue(htmlSettings, CompilerScreenPageLevelTocSettingKey), GetBooleanValue(globalSettings, CompilerScreenPageLevelTocSettingKey)) ?? true,
        };

        int screenAppendixColumns = target switch
        {
            ExportTarget.Pdf => FirstNonNullInteger(GetIntValue(pdfSettings, CompilerScreenAppendixColumnsSettingKey), GetIntValue(globalSettings, CompilerScreenAppendixColumnsSettingKey)) ?? 1,
            ExportTarget.FoundryDb => FirstNonNullInteger(GetIntValue(foundrySettings, CompilerScreenAppendixColumnsSettingKey), GetIntValue(globalSettings, CompilerScreenAppendixColumnsSettingKey)) ?? 1,
            _ => FirstNonNullInteger(GetIntValue(htmlSettings, CompilerScreenAppendixColumnsSettingKey), GetIntValue(globalSettings, CompilerScreenAppendixColumnsSettingKey)) ?? 1,
        };

        int printAppendixColumns = target switch
        {
            ExportTarget.Pdf => FirstNonNullInteger(GetIntValue(pdfSettings, CompilerPrintAppendixColumnsSettingKey), GetIntValue(globalSettings, CompilerPrintAppendixColumnsSettingKey)) ?? 1,
            ExportTarget.FoundryDb => FirstNonNullInteger(GetIntValue(foundrySettings, CompilerPrintAppendixColumnsSettingKey), GetIntValue(globalSettings, CompilerPrintAppendixColumnsSettingKey)) ?? 2,
            _ => FirstNonNullInteger(GetIntValue(htmlSettings, CompilerPrintAppendixColumnsSettingKey), GetIntValue(globalSettings, CompilerPrintAppendixColumnsSettingKey)) ?? 2,
        };

        bool autoLinkEntityMentions = target switch
        {
            ExportTarget.Pdf => FirstNonNullBoolean(GetBooleanValue(pdfSettings, CompilerAutoLinkSettingKey), GetBooleanValue(globalSettings, CompilerAutoLinkSettingKey)) ?? false,
            ExportTarget.FoundryDb => FirstNonNullBoolean(GetBooleanValue(foundrySettings, CompilerAutoLinkSettingKey), GetBooleanValue(globalSettings, CompilerAutoLinkSettingKey)) ?? false,
            _ => FirstNonNullBoolean(GetBooleanValue(htmlSettings, CompilerAutoLinkSettingKey), GetBooleanValue(globalSettings, CompilerAutoLinkSettingKey)) ?? false,
        };

        string[] shadowReferences = target switch
        {
            ExportTarget.Pdf => [.. globalShadowReferences.Concat(pdfShadowReferences).Distinct(StringComparer.OrdinalIgnoreCase)],
            ExportTarget.FoundryDb => [.. globalShadowReferences.Concat(foundryShadowReferences).Distinct(StringComparer.OrdinalIgnoreCase)],
            _ => [.. globalShadowReferences.Concat(htmlShadowReferences).Distinct(StringComparer.OrdinalIgnoreCase)],
        };

        string? configuredPageSize = FirstNonEmptyOrNull(
            GetValue(pdfSettings, CompilerPrintPageSizeSettingKey),
            GetValue(globalSettings, CompilerPrintPageSizeSettingKey));
        PaperFormat pdfPageFormat = ParsePaperFormat(configuredPageSize);

        return new RenderOptions(
            includeUnreferencedSnippetsInAppendix,
            generateReferenceDictionary,
            includePageLevelToc,
            Math.Max(1, screenAppendixColumns),
            Math.Max(1, printAppendixColumns),
            autoLinkEntityMentions,
            [.. shadowReferences],
            pdfPageFormat);
    }

    /// <summary>
    /// Loads compile-time project substitution settings for the export target.
    /// </summary>
    private ImmutableDictionary<string, string> LoadProjectSubstitutionSettings(string sourceRoot, ExportTarget target)
    {
        LoadingCompileTimeSubstitutionSettings(target);
        Dictionary<string, string> combined = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in LoadYamlSettings(Path.Combine(sourceRoot, "settings", "global.yml")))
        {
            combined[key] = value;
        }

        string targetSettingsPath = target switch
        {
            ExportTarget.Pdf => Path.Combine(sourceRoot, "settings", "pdf.yml"),
            ExportTarget.FoundryDb => Path.Combine(sourceRoot, "settings", "foundry.yml"),
            _ => Path.Combine(sourceRoot, "settings", "html.yml"),
        };
        foreach ((string key, string value) in LoadYamlSettings(targetSettingsPath))
        {
            combined[key] = value;
        }

        ApplyingConfigurationProviders(combined.Count);
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(combined.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .AddJsonFile(Path.Combine(sourceRoot, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        foreach ((string key, string? value) in configuration.AsEnumerable())
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
            {
                continue;
            }

            string normalizedKey = key.Replace(':', '.');
            combined[normalizedKey] = value;
        }

        ResolvedCompileTimeSubstitutionSettings(combined.Count);
        return ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, combined);
    }

    /// <summary>
    /// Loads trimmed markdown body text from a file when available.
    /// </summary>
    private static async Task<string?> LoadBodyTextAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        string body = ParseMarkdownDocument(raw).Body.Trim();
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    /// <summary>
    /// Gets a non-empty setting value by key using case-insensitive lookup.
    /// </summary>
    private static string? GetValue(Dictionary<string, string> map, string key)
    {
        if (map.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        foreach ((string mapKey, string mapValue) in map)
        {
            if (string.Equals(mapKey, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(mapValue))
            {
                return mapValue.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a boolean setting value from the supplied map.
    /// </summary>
    private static bool? GetBooleanValue(Dictionary<string, string> map, string key)
    {
        string? value = GetValue(map, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out bool parsedBool))
        {
            return parsedBool;
        }

        if (string.Equals(value, "1", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.Ordinal))
        {
            return false;
        }

        return null;
    }

    /// <summary>
    /// Gets the first non-null boolean from the provided values.
    /// </summary>
    private static bool? FirstNonNullBoolean(params bool?[] values)
    {
        foreach (bool? value in values)
        {
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses an integer setting value from the supplied map.
    /// </summary>
    private static int? GetIntValue(Dictionary<string, string> map, string key)
    {
        string? value = GetValue(map, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt)
            ? parsedInt
            : null;
    }

    /// <summary>
    /// Gets the first non-null integer from the provided values.
    /// </summary>
    private static int? FirstNonNullInteger(params int?[] values)
    {
        foreach (int? value in values)
        {
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the first non-empty string from the provided values.
    /// </summary>
    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets the first non-empty string from the provided values, or <see langword="null"/>.
    /// </summary>
    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Loads Foundry journal entries from chapter markdown files.
    /// </summary>
    private async Task<List<FoundryEntry>> LoadFoundryEntriesAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        List<FoundryEntry> entries = [];
        string contentRoot = Path.Combine(sourceRoot, "content");
        if (!Directory.Exists(contentRoot))
        {
            return entries;
        }

        string[] chapterFiles = Directory.GetFiles(contentRoot, "*.md", SearchOption.TopDirectoryOnly);
        Array.Sort(chapterFiles, StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in chapterFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string raw = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            ParsedMarkdown parsed = ParseMarkdownDocument(raw);
            string title = ResolveSectionTitle(filePath, parsed);
            string processed = ProcessInlineTokens(parsed.Body, filePath, sourceRoot, currentPageTitleOverride: title, contentPageTitleOverride: title);
            string html = RenderMarkdownToHtml(processed);
            entries.Add(new FoundryEntry(BuildSectionId(filePath), title, "journal", html, Path.GetRelativePath(sourceRoot, filePath)));
        }

        return entries;
    }
}
