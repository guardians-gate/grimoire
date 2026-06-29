using System.Globalization;
using System.Collections;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Grimoire.Core;

/// <summary>
/// Implements advanced parsing, rendering, and matching helpers for project search.
/// </summary>
public sealed partial class ProjectSearchService
{
    /// <summary>
    /// Enumerates cross-reference edges discovered in a single line of source content.
    /// </summary>
    /// <param name="rootPath">The absolute root path.</param>
    /// <param name="sourcePath">The absolute source file path.</param>
    /// <param name="sourceRelativePath">The source file path relative to <paramref name="rootPath"/>.</param>
    /// <param name="line">The line content to scan.</param>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <returns>A list of discovered reference edges for the line.</returns>
    private static List<ProjectReferenceEdge> EnumerateCrossReferenceEdgesForLine(
        string rootPath,
        string sourcePath,
        string sourceRelativePath,
        ReadOnlySpan<char> line,
        int lineNumber)
    {
        string lineText = line.ToString();
        List<ProjectReferenceEdge> edges = [];
        foreach (Match match in MarkdownIncludeRegex.Matches(lineText))
        {
            string? targetPath = ResolveReferenceTargetPath(rootPath, sourcePath, match.Groups["path"].Value);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                continue;
            }

            edges.Add(new(sourceRelativePath, targetPath, "include", lineNumber));
        }

        foreach (Match match in MacroReferenceRegex.Matches(lineText))
        {
            string? targetPath = ResolveReferenceTargetPath(rootPath, sourcePath, match.Groups["path"].Value);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                continue;
            }

            edges.Add(new(sourceRelativePath, targetPath, "macro", lineNumber));
        }

        return edges;
    }

    /// <summary>
    /// Resolves a raw include or macro path to a normalized project-relative target.
    /// </summary>
    /// <param name="rootPath">The absolute project root path.</param>
    /// <param name="sourcePath">The absolute source file path containing the reference.</param>
    /// <param name="rawReferencePath">The raw reference value extracted from source text.</param>
    /// <returns>The normalized target path when valid; otherwise, <see langword="null"/>.</returns>
    private static string? ResolveReferenceTargetPath(string rootPath, string sourcePath, string rawReferencePath)
    {
        if (string.IsNullOrWhiteSpace(rawReferencePath))
        {
            return null;
        }

        string cleaned = rawReferencePath.Trim();
        int queryIndex = cleaned.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            cleaned = cleaned[..queryIndex];
        }

        int fragmentIndex = cleaned.IndexOf('#', StringComparison.Ordinal);
        if (fragmentIndex >= 0)
        {
            cleaned = cleaned[..fragmentIndex];
        }

        if (string.IsNullOrWhiteSpace(cleaned) || Uri.TryCreate(cleaned, UriKind.Absolute, out _))
        {
            return null;
        }

        string baseDirectory = Path.GetDirectoryName(sourcePath) ?? rootPath;
        string resolved = Path.GetFullPath(Path.Combine(baseDirectory, cleaned));
        if (!IsPathWithinRoot(rootPath, resolved))
        {
            return null;
        }

        string extension = Path.GetExtension(resolved);
        if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return NormalizePath(Path.GetRelativePath(rootPath, resolved));
    }

    /// <summary>
    /// Resolves a normalized file-kind token for a path.
    /// </summary>
    /// <param name="filePath">The file path to classify.</param>
    /// <returns><c>json</c> for JSON files; otherwise, <c>markdown</c>.</returns>
    private static string ResolveFileKind(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "markdown";
    }

    /// <summary>
    /// Enumerates searchable markdown and JSON files rooted at the project path.
    /// </summary>
    /// <param name="rootPath">The absolute root path to search.</param>
    /// <returns>A deterministic sequence of candidate file paths.</returns>
    private static IEnumerable<string> EnumerateCandidateFiles(string rootPath)
    {
        string[] rootFiles = Directory.GetFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly);
        Array.Sort(rootFiles, StringComparer.OrdinalIgnoreCase);
        foreach (string file in rootFiles)
        {
            if (IsSearchableEntityFile(file))
            {
                yield return Path.GetFullPath(file);
            }
        }

        string[] subdirectories = Directory.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);
        Array.Sort(subdirectories, StringComparer.OrdinalIgnoreCase);
        foreach (string directory in subdirectories)
        {
            string[] files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (IsSearchableEntityFile(file))
                {
                    yield return Path.GetFullPath(file);
                }
            }
        }
    }

    /// <summary>
    /// Determines whether a path is eligible for search indexing.
    /// </summary>
    /// <param name="path">The file path to evaluate.</param>
    /// <returns><see langword="true"/> when the file is searchable; otherwise, <see langword="false"/>.</returns>
    private static bool IsSearchableEntityFile(string path)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(Path.GetFileName(path), "TEMPLATE.md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes top-level subdirectory filters into a case-insensitive set.
    /// </summary>
    /// <param name="filters">The raw filter values.</param>
    /// <returns>A normalized filter set, or <see langword="null"/> when no filters exist.</returns>
    private static HashSet<string>? BuildFilter(IReadOnlyCollection<string>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return null;
        }

        HashSet<string> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawFilter in filters)
        {
            if (string.IsNullOrWhiteSpace(rawFilter))
            {
                continue;
            }

            string[] segments = rawFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string segment in segments)
            {
                string value = segment.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    normalized.Add(value);
                }
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    /// <summary>
    /// Extracts the first path segment from a normalized relative path.
    /// </summary>
    /// <param name="relativePath">The relative path to inspect.</param>
    /// <returns>The first path segment, or <see langword="null"/> when unavailable.</returns>
    private static string? GetTopLevelDirectory(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        int separatorIndex = relativePath.IndexOf('/', StringComparison.Ordinal);
        return separatorIndex < 0 ? null : relativePath[..separatorIndex];
    }

    /// <summary>
    /// Loads a JSON model root from a JSON file or markdown front matter.
    /// </summary>
    /// <param name="filePath">The entity file path.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A cloned JSON element representing the entity root.</returns>
    private static async Task<JsonElement> LoadEntityRootAsync(string filePath, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(filePath);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192, FileOptions.SequentialScan);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.Clone();
        }

        string markdown = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        (Dictionary<string, object?> frontMatter, string body) = ParseMarkdown(markdown);
        Dictionary<string, object?> model = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in frontMatter)
        {
            model[key] = value;
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            model["content"] = body.Trim();
        }

        string serialized = JsonSerializer.Serialize(model);
        using var markdownDocument = JsonDocument.Parse(serialized);
        return markdownDocument.RootElement.Clone();
    }

    /// <summary>
    /// Resolves the nearest <c>cliFormat</c> template from ancestor <c>TEMPLATE.md</c> files.
    /// </summary>
    /// <param name="rootPath">The absolute project root path.</param>
    /// <param name="directory">The starting directory for template lookup.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>The trimmed CLI format template when present; otherwise, <see langword="null"/>.</returns>
    private static async Task<string?> LoadCliFormatAsync(string rootPath, string directory, CancellationToken cancellationToken)
    {
        string current = directory;
        while (IsPathWithinRoot(rootPath, current))
        {
            string templatePath = Path.Combine(current, "TEMPLATE.md");
            if (File.Exists(templatePath))
            {
                string template = await File.ReadAllTextAsync(templatePath, cancellationToken).ConfigureAwait(false);
                (Dictionary<string, object?> frontMatter, _) = ParseMarkdown(template);
                if (!frontMatter.TryGetValue("cliFormat", out object? format) || format is null)
                {
                    return null;
                }

                string value = Convert.ToString(format, CultureInfo.InvariantCulture) ?? string.Empty;
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>
    /// Resolves the display name for a catalog entity.
    /// </summary>
    /// <param name="root">The entity JSON root.</param>
    /// <param name="filePath">The source file path.</param>
    /// <returns>A normalized display name.</returns>
    private static string ResolveDisplayName(JsonElement root, string filePath)
    {
        if (TryResolveJsonPath(root, "name", out JsonElement name) && !string.IsNullOrWhiteSpace(name.ToString()))
        {
            return JsonValueFormatter.NormalizeEntityText(name.ToString(), "name");
        }

        if (TryResolveJsonPath(root, "title", out JsonElement title) && !string.IsNullOrWhiteSpace(title.ToString()))
        {
            return JsonValueFormatter.NormalizeEntityText(title.ToString(), "title");
        }

        if (TryResolveJsonPath(root, "definition.name", out JsonElement definitionName) && !string.IsNullOrWhiteSpace(definitionName.ToString()))
        {
            return JsonValueFormatter.NormalizeEntityText(definitionName.ToString(), "name");
        }

        if (TryResolveJsonPath(root, "ddb.character.name", out JsonElement characterName) && !string.IsNullOrWhiteSpace(characterName.ToString()))
        {
            return JsonValueFormatter.NormalizeEntityText(characterName.ToString(), "name");
        }

        return TryParseDndBeyondNumberedFileName(filePath, out string parsedName)
            ? parsedName
            : JsonValueFormatter.NormalizeEntityText(Path.GetFileNameWithoutExtension(filePath), "name");
    }

    /// <summary>
    /// Renders a CLI template against an entity model.
    /// </summary>
    /// <param name="cliFormat">The CLI format template.</param>
    /// <param name="root">The entity JSON root.</param>
    /// <returns>The rendered output.</returns>
    private static string RenderCliFormat(string cliFormat, JsonElement root) => ReplaceTemplateTokens(cliFormat, token =>
    {
        (string path, string? formatHint) = ParseTemplateToken(token);
        bool hasFallback = TryParseFallbackFormat(formatHint, out string fallbackTemplate);
        if (hasFallback)
        {
            formatHint = null;
        }

        if (!TryResolveJsonPath(root, path, out JsonElement value))
        {
            return hasFallback
                ? RenderCliFormat(fallbackTemplate, root)
                : string.Empty;
        }

        string formatted = ValueToInlineString(value, path, formatHint);
        if (!string.IsNullOrWhiteSpace(formatted))
            return formatted;

        return hasFallback
            ? RenderCliFormat(fallbackTemplate, root)
            : string.Empty;
    });

    /// <summary>
    /// Replaces <c>{{token}}</c> placeholders with resolver output.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="resolver">The token resolver callback.</param>
    /// <returns>The rendered template output.</returns>
    private static string ReplaceTemplateTokens(string template, Func<string, string> resolver)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        StringBuilder output = new(template.Length);
        int cursor = 0;
        while (cursor < template.Length)
        {
            int start = template.IndexOf("{{", cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                output.Append(template, cursor, template.Length - cursor);
                break;
            }

            output.Append(template, cursor, start - cursor);
            int end = FindTemplateTokenEnd(template, start + 2);
            if (end < 0)
            {
                output.Append(template, start, template.Length - start);
                break;
            }

            string token = template.Substring(start + 2, end - start - 2).Trim();
            output.Append(resolver(token));
            cursor = end + 2;
        }

        return output.ToString();
    }

    /// <summary>
    /// Finds the closing index for a nested template token.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="searchStart">The index to start scanning from.</param>
    /// <returns>The index of the terminating brace pair, or <c>-1</c> when not found.</returns>
    private static int FindTemplateTokenEnd(string template, int searchStart)
    {
        int depth = 1;
        for (int index = searchStart; index < template.Length - 1; index++)
        {
            if (template[index] == '{' && template[index + 1] == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (template[index] == '}' && template[index + 1] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }

                index++;
            }
        }

        return -1;
    }

    /// <summary>
    /// Renders top-level JSON properties into a compact bullet list.
    /// </summary>
    /// <param name="root">The entity JSON root.</param>
    /// <returns>The rendered property table.</returns>
    private static string RenderPropertyTable(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ValueToInlineString(root, null, null);
        }

        StringBuilder builder = new();
        foreach (JsonProperty property in root.EnumerateObject().OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase))
        {
            string formatted = ValueToInlineString(property.Value, property.Name, null);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                continue;
            }

            builder.Append("- ")
                .Append(property.Name)
                .Append(": ")
                .AppendLine(formatted);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Converts a JSON value to a truncated inline display string.
    /// </summary>
    /// <param name="value">The JSON value to format.</param>
    /// <param name="keyPath">The logical key path for formatting hints.</param>
    /// <param name="formatHint">An optional explicit format hint.</param>
    /// <returns>A formatted inline value.</returns>
    private static string ValueToInlineString(JsonElement value, string? keyPath, string? formatHint)
    {
        string formatted = JsonValueFormatter.ToDisplayString(value, keyPath, formatHint);
        return Truncate(formatted, 240);
    }

    /// <summary>
    /// Parses a template token into path and optional format components.
    /// </summary>
    /// <param name="token">The token text to parse.</param>
    /// <returns>A tuple containing the path and optional format hint.</returns>
    private static (string Path, string? FormatHint) ParseTemplateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (string.Empty, null);
        }

        int separator = token.IndexOf("::", StringComparison.Ordinal);
        int separatorLength = 2;
        if (separator < 0)
        {
            separator = token.IndexOf('|', StringComparison.Ordinal);
            separatorLength = 1;
        }

        if (separator < 0)
        {
            return (token.Trim(), null);
        }

        string path = token[..separator].Trim();
        string format = token[(separator + separatorLength)..].Trim();
        return (path, string.IsNullOrWhiteSpace(format) ? null : format);
    }

    /// <summary>
    /// Parses fallback template syntax from a format hint.
    /// </summary>
    /// <param name="formatHint">The format hint to inspect.</param>
    /// <param name="fallbackTemplate">When this method returns, contains the fallback template.</param>
    /// <returns><see langword="true"/> when fallback syntax is present; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFallbackFormat(string? formatHint, out string fallbackTemplate)
    {
        fallbackTemplate = string.Empty;
        if (string.IsNullOrWhiteSpace(formatHint) || formatHint[0] != '-')
        {
            return false;
        }

        fallbackTemplate = formatHint[1..].Trim();
        return true;
    }

    /// <summary>
    /// Truncates text to a maximum length with ellipsis suffix.
    /// </summary>
    /// <param name="value">The value to truncate.</param>
    /// <param name="maxLength">The maximum length to keep.</param>
    /// <returns>The truncated or original value.</returns>
    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    /// <summary>
    /// Resolves a dot-notation JSON path against a root element.
    /// </summary>
    /// <param name="root">The JSON root element.</param>
    /// <param name="keyPath">The dot-notation key path.</param>
    /// <param name="value">When this method returns, contains the resolved value.</param>
    /// <returns><see langword="true"/> when the path resolves; otherwise, <see langword="false"/>.</returns>
    private static bool TryResolveJsonPath(JsonElement root, string keyPath, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        string[] segments = keyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (string segment in segments)
        {
            if (!TryResolveJsonPathSegment(value, segment, out JsonElement resolved))
            {
                value = default;
                return false;
            }

            value = resolved;
        }

        return true;
    }

    /// <summary>
    /// Resolves a single JSON path segment, including optional array indexers.
    /// </summary>
    /// <param name="current">The current JSON element.</param>
    /// <param name="segment">The segment to resolve.</param>
    /// <param name="value">When this method returns, contains the resolved value.</param>
    /// <returns><see langword="true"/> when the segment resolves; otherwise, <see langword="false"/>.</returns>
    private static bool TryResolveJsonPathSegment(JsonElement current, string segment, out JsonElement value)
    {
        value = current;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        int bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
        string propertyName = bracketIndex >= 0 ? segment[..bracketIndex] : segment;

        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out JsonElement nested))
            {
                return false;
            }

            value = nested;
        }

        int cursor = bracketIndex;
        while (cursor >= 0)
        {
            int closeOffset = segment[(cursor + 1)..].IndexOf(']', StringComparison.Ordinal);
            if (closeOffset < 0)
            {
                return false;
            }

            int closeIndex = cursor + 1 + closeOffset;
            string rawIndex = segment[(cursor + 1)..closeIndex];
            if (!int.TryParse(rawIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                return false;
            }

            if (value.ValueKind != JsonValueKind.Array || index < 0 || index >= value.GetArrayLength())
            {
                return false;
            }

            value = value[index];
            int nextOpenOffset = segment[(closeIndex + 1)..].IndexOf('[', StringComparison.Ordinal);
            cursor = nextOpenOffset < 0 ? -1 : closeIndex + 1 + nextOpenOffset;
        }

        return true;
    }

    /// <summary>
    /// Parses markdown content into front matter and body segments.
    /// </summary>
    /// <param name="raw">The raw markdown text.</param>
    /// <returns>A tuple containing normalized front matter and body text.</returns>
    private static (Dictionary<string, object?> FrontMatter, string Body) ParseMarkdown(string raw)
    {
        Match match = MarkdownFrontMatterRegex.Match(raw);
        if (!match.Success)
        {
            return (new(StringComparer.OrdinalIgnoreCase), raw);
        }

        string yamlSegment = match.Groups["yaml"].Value.Trim();
        string body = match.Groups["body"].Value;
        Dictionary<string, object?> frontMatter = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(yamlSegment))
        {
            return (frontMatter, body);
        }

        object deserialized = YamlDeserializer.Deserialize<object>(yamlSegment);
        if (deserialized is not Dictionary<object, object> map) return (frontMatter, body);

        foreach ((object key, object value) in map)
        {
            string normalizedKey = Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedKey))
            {
                frontMatter[normalizedKey] = NormalizeYamlValue(value);
            }
        }

        return (frontMatter, body);
    }

    /// <summary>
    /// Recursively normalizes YAML values into serializable .NET structures.
    /// </summary>
    /// <param name="value">The raw YAML value.</param>
    /// <returns>A normalized value suitable for JSON serialization.</returns>
    private static object? NormalizeYamlValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;

            case Dictionary<object, object> map:
            {
                Dictionary<string, object?> normalized = new(StringComparer.OrdinalIgnoreCase);
                foreach ((object key, object nestedValue) in map)
                {
                    string nestedKey = Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(nestedKey))
                    {
                        normalized[nestedKey] = NormalizeYamlValue(nestedValue);
                    }
                }

                return normalized;
            }

            case IEnumerable list and not string:
            {
                List<object?> normalizedList = [];
                normalizedList.AddRange(
                    from object? item in list
                    select NormalizeYamlValue(item));
                return normalizedList;
            }

            default:
                return value;
        }
    }

    /// <summary>
    /// Normalizes path separators to forward slashes.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Determines whether a path remains within a specified root directory.
    /// </summary>
    /// <param name="rootPath">The root directory path.</param>
    /// <param name="path">The path to validate.</param>
    /// <returns><see langword="true"/> when the path is under the root; otherwise, <see langword="false"/>.</returns>
    private static bool IsPathWithinRoot(string rootPath, string path)
    {
        string relative = Path.GetRelativePath(rootPath, path);
        return !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// Attempts to parse D&amp;D Beyond numbered file names into display names.
    /// </summary>
    /// <param name="filePath">The source file path.</param>
    /// <param name="name">When this method returns, contains the parsed display name.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseDndBeyondNumberedFileName(string filePath, out string name)
    {
        name = string.Empty;
        string? directory = Path.GetFileName(Path.GetDirectoryName(filePath));
        if (!string.Equals(directory, "players", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(directory, "spells", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Match match = DndBeyondCharacterFileRegex.Match(Path.GetFileNameWithoutExtension(filePath));
        if (!match.Success)
        {
            return false;
        }

        name = NormalizeDashedToken(match.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        name = JsonValueFormatter.NormalizeEntityText(name, "name");
        return true;
    }

    /// <summary>
    /// Replaces dash and underscore separators with spaces.
    /// </summary>
    /// <param name="value">The token value to normalize.</param>
    /// <returns>The normalized token text.</returns>
    private static string NormalizeDashedToken(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan().Trim();
        StringBuilder builder = new(span.Length);
        foreach (char character in span)
        {
            builder.Append(character is '-' or '_' ? ' ' : character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether a catalog entry matches a query pattern.
    /// </summary>
    /// <param name="name">The entity name.</param>
    /// <param name="relativePath">The entity relative path.</param>
    /// <param name="details">The rendered details string.</param>
    /// <param name="includedBy">The includer path collection.</param>
    /// <param name="queryPattern">The query pattern to evaluate.</param>
    /// <returns><see langword="true"/> when any indexed field matches; otherwise, <see langword="false"/>.</returns>
    private static bool CatalogEntryMatchesQuery(
        string name,
        string relativePath,
        string details,
        ImmutableArray<string> includedBy,
        BoyerMoorePattern queryPattern)
    {
        if (BoyerMooreContains(name, queryPattern) ||
            BoyerMooreContains(relativePath, queryPattern) ||
            BoyerMooreContains(details, queryPattern))
        {
            return true;
        }

        return Enumerable.Any(includedBy,
            includePath => BoyerMooreContains(includePath, queryPattern));
    }

    /// <summary>
    /// Logs the start of catalog search.
    /// </summary>
    /// <param name="rootPath">The absolute catalog root path.</param>
    [LoggerMessage(EventId = 2080, Level = LogLevel.Debug, Message = "Project catalog search started at {rootPath}.")]
    private partial void CatalogSearchStarted(string rootPath);

    /// <summary>
    /// Logs catalog search completion.
    /// </summary>
    /// <param name="resultCount">The number of catalog entries returned.</param>
    [LoggerMessage(EventId = 2081, Level = LogLevel.Debug, Message = "Project catalog search completed with {resultCount} entries.")]
    private partial void CatalogSearchCompleted(int resultCount);

    /// <summary>
    /// Logs the start of advanced search.
    /// </summary>
    /// <param name="mode">The advanced search mode.</param>
    /// <param name="rootPath">The absolute catalog root path.</param>
    /// <param name="requestedLimit">The requested match limit.</param>
    [LoggerMessage(EventId = 2082, Level = LogLevel.Debug, Message = "Project advanced search started at {rootPath} with mode={mode} requestedLimit={requestedLimit}.")]
    private partial void AdvancedSearchStarted(ProjectSearchMode mode, string rootPath, int requestedLimit);

    /// <summary>
    /// Logs advanced search completion.
    /// </summary>
    /// <param name="mode">The advanced search mode.</param>
    /// <param name="resultCount">The number of matches returned.</param>
    [LoggerMessage(EventId = 2083, Level = LogLevel.Debug, Message = "Project advanced search completed for mode={mode} with {resultCount} matches.")]
    private partial void AdvancedSearchCompleted(ProjectSearchMode mode, int resultCount);

    /// <summary>
    /// Logs file-level iteration progress within a search phase.
    /// </summary>
    /// <param name="phase">The current search phase.</param>
    /// <param name="index">The one-based file index.</param>
    /// <param name="total">The total file count in the phase.</param>
    /// <param name="relativePath">The relative path of the processed file.</param>
    [LoggerMessage(EventId = 2084, Level = LogLevel.Debug, Message = "Project search iteration ({phase}) file {index}/{total}: {relativePath}.")]
    private partial void ProcessingSearchFile(string phase, int index, int total, string relativePath);

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown include references.
    /// </summary>
    [GeneratedRegex(@"!\[[^\]]*\]\((?<path>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownIncludeRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing macro reference placeholders.
    /// </summary>
    [GeneratedRegex(@"\$\{(?<path>[^}!]+)(?:![^}]*)?\}", RegexOptions.Compiled)]
    private static partial Regex MacroReferenceRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown front matter boundaries.
    /// </summary>
    [GeneratedRegex(@"\A---\r?\n(?<yaml>[\s\S]*?)\r?\n---\r?\n(?<body>[\s\S]*)\z", RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex MarkdownFrontMatterRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing numbered D&amp;D Beyond file names.
    /// </summary>
    [GeneratedRegex(@"^(?<id>\d+)-(?<name>.+)$", RegexOptions.Compiled)]
    private static partial Regex DndBeyondCharacterFileRegex { get; }

    /// <summary>
    /// Represents a callback that receives a scanned line and line number.
    /// </summary>
    /// <param name="line">The scanned line content.</param>
    /// <param name="lineNumber">The one-based line number.</param>
    /// <returns><see langword="true"/> to continue scanning; otherwise, <see langword="false"/>.</returns>
    private delegate bool LineVisitor(ReadOnlySpan<char> line, int lineNumber);

    /// <summary>
    /// Represents a callback that receives a pattern match window and position.
    /// </summary>
    /// <param name="window">The scanned character window.</param>
    /// <param name="matchIndex">The zero-based match index in the window.</param>
    /// <param name="lineNumber">The one-based line number for the match.</param>
    /// <returns><see langword="true"/> to continue scanning; otherwise, <see langword="false"/>.</returns>
    private delegate bool PatternMatchVisitor(ReadOnlySpan<char> window, int matchIndex, int lineNumber);

    /// <summary>
    /// Represents a directed cross-reference edge between content files.
    /// </summary>
    private sealed record ProjectReferenceEdge
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectReferenceEdge"/> record.
        /// </summary>
        /// <param name="sourcePath">The relative source path.</param>
        /// <param name="targetPath">The relative target path.</param>
        /// <param name="referenceType">The normalized reference type.</param>
        /// <param name="lineNumber">The one-based source line number.</param>
        public ProjectReferenceEdge(string sourcePath, string targetPath, string referenceType, int lineNumber)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
            ReferenceType = referenceType;
            LineNumber = lineNumber;
        }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the relative source path.
        /// </summary>
        public string SourcePath { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the relative target path.
        /// </summary>
        public string TargetPath { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the cross-reference type.
        /// </summary>
        public string ReferenceType { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="int"/> indicating the one-based source line number.
        /// </summary>
        public int LineNumber { get; init; }
    }

    /// <summary>
    /// Represents a compiled Boyer-Moore search pattern.
    /// </summary>
    private sealed record BoyerMoorePattern
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BoyerMoorePattern"/> record.
        /// </summary>
        /// <param name="needle">The upper-case needle string.</param>
        /// <param name="shifts">The bad-character shift table.</param>
        public BoyerMoorePattern(string needle, int[] shifts)
        {
            Needle = needle;
            Shifts = shifts;
        }

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the upper-case needle text.
        /// </summary>
        public string Needle { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="Int32"/>[] representing bad-character shifts by code point.
        /// </summary>
        public int[] Shifts { get; init; }
    }
}
