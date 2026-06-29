using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Resolves, renders, and transforms material includes and template-backed sources.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Determines whether an include path targets a supported structured material file.
    /// </summary>
    private static bool IsStructuredInclude(string includePath)
    {
        string extension = Path.GetExtension(includePath);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses an include path and extracts its normalized path and inline flag.
    /// </summary>
    private static bool TryParseIncludePath(string includePath, out string path, out bool inline)
    {
        path = includePath;
        inline = false;
        if (string.IsNullOrWhiteSpace(includePath))
        {
            return false;
        }

        string working = includePath.Trim();
        if (ContainsInlineQueryFlag(working))
        {
            inline = true;
        }

        int fragmentStart = working.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
        {
            working = working[..fragmentStart];
        }

        int queryStart = working.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            path = working;
            return true;
        }

        path = working[..queryStart];
        string query = working[(queryStart + 1)..];
        foreach (string token in query.Split(['&', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string decodedToken = Uri.UnescapeDataString(token);
            string optionName;
            string optionValue;
            int equals = decodedToken.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                optionName = decodedToken.Trim();
                optionValue = string.Empty;
            }
            else
            {
                optionName = decodedToken[..equals].Trim();
                optionValue = decodedToken[(equals + 1)..].Trim();
            }

            if (!string.Equals(optionName, "inline", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(optionValue) ||
                string.Equals(optionValue, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(optionValue, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(optionValue, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(optionValue, "on", StringComparison.OrdinalIgnoreCase))
            {
                inline = true;
            }

            break;
        }

        return true;
    }

    /// <summary>
    /// Determines whether an include path contains the inline query flag.
    /// </summary>
    private static bool ContainsInlineQueryFlag(string includePath)
    {
        if (string.IsNullOrWhiteSpace(includePath))
        {
            return false;
        }

        return includePath.Contains("?inline", StringComparison.OrdinalIgnoreCase) ||
               includePath.Contains("&inline", StringComparison.OrdinalIgnoreCase) ||
               includePath.Contains(";inline", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a relative reference path against the current file path.
    /// </summary>
    private static string ResolveReferencePath(string currentFilePath, string relativePath)
    {
        string currentDirectory = Path.GetDirectoryName(currentFilePath) ?? throw new InvalidOperationException("Current file has no directory.");
        string combined = Path.Combine(currentDirectory, relativePath);
        return Path.GetFullPath(combined);
    }

    /// <summary>
    /// Resolves an entity material path by its display name.
    /// </summary>
    private string? ResolveEntityPathByName(string sourceRoot, string entityName)
    {
        EnsureEntityLookupByName(sourceRoot);
        return _entityLookupByName.GetValueOrDefault(entityName);
    }

    /// <summary>
    /// Ensures the entity-name lookup cache is populated for the current source root.
    /// </summary>
    private void EnsureEntityLookupByName(string sourceRoot)
    {
        string normalizedSourceRoot = Path.GetFullPath(sourceRoot);
        if (string.Equals(_entityLookupSourceRoot, normalizedSourceRoot, StringComparison.OrdinalIgnoreCase) &&
            _entityLookupByName.Count > 0)
        {
            return;
        }

        _entityLookupByName.Clear();
        _entityLookupSourceRoot = normalizedSourceRoot;

        foreach (string materialPath in LoadReferenceableMaterialPaths(normalizedSourceRoot))
        {
            string title = ResolveMaterialTitle(materialPath, null);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (!_entityLookupByName.TryGetValue(title, out string? existingPath) ||
                ShouldPreferMentionCandidate(existingPath, materialPath))
            {
                _entityLookupByName[title] = materialPath;
            }
        }
    }

    /// <summary>
    /// Resolves a macro reference value from markdown or JSON material content.
    /// </summary>
    private static string ResolveReferenceValue(string referencePath, string? property)
    {
        (string propertyPath, string? formatHint) = ParseTemplateToken(property ?? string.Empty);
        if (referencePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            string markdown = File.ReadAllText(referencePath);
            ParsedMarkdown parsed = ParseMarkdownDocument(markdown);
            if (string.IsNullOrEmpty(property))
            {
                return parsed.FrontMatter.TryGetValue("name", out string? name)
                    ? name
                    : Path.GetFileNameWithoutExtension(referencePath);
            }

            if (string.Equals(propertyPath, "content", StringComparison.OrdinalIgnoreCase))
            {
                return parsed.Body;
            }

            return parsed.FrontMatter.TryGetValue(propertyPath, out string? propertyValue)
                ? propertyValue
                : throw new InvalidOperationException($"Property '{property}' was not found in {referencePath}.");
        }

        if (!referencePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported macro reference '{referencePath}'.");

        {
            using var document = JsonDocument.Parse(File.ReadAllText(referencePath));
            JsonElement root = document.RootElement;
            if (!string.IsNullOrEmpty(property))
                return TryResolveJsonPath(root, propertyPath, out JsonElement propertyElement)
                    ? JsonValueFormatter.ToDisplayString(propertyElement, propertyPath, formatHint)
                    : throw new InvalidOperationException($"Property '{property}' was not found in {referencePath}.");

            if (root.TryGetProperty("name", out JsonElement name))
            {
                return name.ToString();
            }

            return TryParseDndBeyondNumberedFileInfo(referencePath, out _, out string characterName, out _)
                ? characterName
                : Path.GetFileNameWithoutExtension(referencePath);
        }

    }

    /// <summary>
    /// Resolves a material title, using an override or cached source-derived value.
    /// </summary>
    private string ResolveMaterialTitle(string referencePath, string? overrideTitle)
    {
        if (!string.IsNullOrWhiteSpace(overrideTitle))
        {
            return NormalizeEntityTitle(overrideTitle.Trim());
        }

        string normalizedPath = Path.GetFullPath(referencePath);
        long writeTimeTicks = File.Exists(normalizedPath)
            ? File.GetLastWriteTimeUtc(normalizedPath).Ticks
            : 0;
        if (_materialTitleCache.TryGetValue(normalizedPath, out MaterialTitleCacheEntry? cachedTitle) &&
            cachedTitle.SourceWriteUtcTicks == writeTimeTicks)
        {
            return cachedTitle.Title;
        }

        string title = ResolveMaterialTitleUncached(normalizedPath);
        _materialTitleCache[normalizedPath] = new MaterialTitleCacheEntry(writeTimeTicks, title);
        return title;
    }

    /// <summary>
    /// Resolves a material title directly from source content without cache reuse.
    /// </summary>
    private static string ResolveMaterialTitleUncached(string referencePath)
    {
        if (referencePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            ParsedMarkdown parsed = ParseMarkdownDocument(File.ReadAllText(referencePath));
            return FirstNonEmpty(GetValue(parsed.FrontMatter, "title"), GetValue(parsed.FrontMatter, "name"), ResolveSectionTitle(referencePath, parsed));
        }

        // ReSharper disable once InvertIf
        if (referencePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(referencePath));
            JsonElement root = document.RootElement;
            string? jsonTitle = ResolvePreferredJsonTitle(root, referencePath);
            if (!string.IsNullOrWhiteSpace(jsonTitle))
            {
                return jsonTitle;
            }
        }

        return NormalizeEntityTitle(TryParseDndBeyondNumberedFileInfo(referencePath, out _, out string characterNameFromFile, out _)
            ? characterNameFromFile
            : Path.GetFileNameWithoutExtension(referencePath));
    }

    /// <summary>
    /// Renders material body markdown asynchronously for a referenced file.
    /// </summary>
    private async Task<string> RenderMaterialBodyMarkdownAsync(
        string referencePath,
        string sourceRoot,
        string? mentionTargetIdOverride,
        string? currentPageTitleOverride,
        string? contentPageTitleOverride,
        CancellationToken cancellationToken)
    {
        if (referencePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            string raw = await File.ReadAllTextAsync(referencePath, cancellationToken).ConfigureAwait(false);
            RenderedTemplateMarkdown markdownRendered = RenderMarkdownWithTemplate(referencePath, raw);
            return ProcessInlineTokens(
                markdownRendered.TemplateApplied ? StripLeadingHeading(markdownRendered.Markdown) : markdownRendered.Markdown,
                referencePath,
                sourceRoot,
                mentionTargetIdOverride,
                currentPageTitleOverride,
                contentPageTitleOverride);
        }

        RenderedTemplateMarkdown jsonRendered = RenderJsonWithTemplateResult(referencePath);
        return ProcessInlineTokens(
            jsonRendered.TemplateApplied ? StripLeadingHeading(jsonRendered.Markdown) : jsonRendered.Markdown,
            referencePath,
            sourceRoot,
            mentionTargetIdOverride,
            currentPageTitleOverride,
            contentPageTitleOverride);
    }

    /// <summary>
    /// Renders material body markdown synchronously for a referenced file.
    /// </summary>
    private string RenderMaterialBodyMarkdown(
        string referencePath,
        string sourceRoot,
        string? mentionTargetIdOverride,
        string? currentPageTitleOverride,
        string? contentPageTitleOverride)
    {
        if (referencePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            RenderedTemplateMarkdown markdownRendered = RenderMarkdownWithTemplate(referencePath, File.ReadAllText(referencePath));
            return ProcessInlineTokens(
                markdownRendered.TemplateApplied ? StripLeadingHeading(markdownRendered.Markdown) : markdownRendered.Markdown,
                referencePath,
                sourceRoot,
                mentionTargetIdOverride,
                currentPageTitleOverride,
                contentPageTitleOverride);
        }

        RenderedTemplateMarkdown jsonRendered = RenderJsonWithTemplateResult(referencePath);
        return ProcessInlineTokens(
            jsonRendered.TemplateApplied ? StripLeadingHeading(jsonRendered.Markdown) : jsonRendered.Markdown,
            referencePath,
            sourceRoot,
            mentionTargetIdOverride,
            currentPageTitleOverride,
            contentPageTitleOverride);
    }

    /// <summary>
    /// Removes the first markdown heading from content when present.
    /// </summary>
    private static string StripLeadingHeading(string markdown)
    {
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith('#'))
        {
            return markdown;
        }

        int firstNewLine = normalized.IndexOf('\n', StringComparison.Ordinal);
        if (firstNewLine < 0)
        {
            return string.Empty;
        }

        string withoutHeading = normalized[(firstNewLine + 1)..].TrimStart();
        return withoutHeading;
    }

    /// <summary>
    /// Builds a stable anchor identifier for a material file.
    /// </summary>
    private static string BuildMaterialAnchorId(string sourceRoot, string referencePath)
    {
        string relativePath = Path.GetRelativePath(sourceRoot, referencePath);
        return $"ref-{BuildSectionId(relativePath)}";
    }

    /// <summary>
    /// Builds a reference-dictionary anchor identifier for a material file.
    /// </summary>
    private static string BuildReferenceDictionaryMaterialAnchorId(string sourceRoot, string referencePath)
    {
        return $"dict-{BuildMaterialAnchorId(sourceRoot, referencePath)}";
    }

    /// <summary>
    /// Normalizes dashed or underscored tokens into space-separated text.
    /// </summary>
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
    /// Tries to parse a D&amp;D Beyond numbered entity file name.
    /// </summary>
    private static bool TryParseDndBeyondNumberedFileInfo(string path, out string id, out string name, out string entityDirectory)
    {
        id = string.Empty;
        name = string.Empty;
        entityDirectory = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
        if (!string.Equals(entityDirectory, "players", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entityDirectory, "spells", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(path);
        Match match = DndBeyondCharacterFileRegex.Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        id = match.Groups["id"].Value;
        name = NormalizeEntityTitle(NormalizeDashedToken(match.Groups["name"].Value));
        return !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name);
    }

    /// <summary>
    /// Builds HTML for an included material block or inline anchor.
    /// </summary>
    private string BuildIncludedMaterialHtml(string title, string referencePath, string sourceRoot, bool inline, string? mentionTargetId, string contentPageTitle)
    {
        string resolvedTitle = ResolveMaterialTitle(referencePath, title);
        string anchorId = BuildMaterialAnchorId(sourceRoot, referencePath);
        string? inlineMentionTargetId = inline ? anchorId : mentionTargetId;
        string bodyMarkdown = RenderMaterialBodyMarkdown(referencePath, sourceRoot, inlineMentionTargetId, resolvedTitle, contentPageTitle);
        string bodyHtml = RenderMarkdownToHtml(bodyMarkdown);
        string heroImageHtml = BuildMaterialHeroImageHtml(referencePath, sourceRoot);
        return inline
            ? $"<div id=\"{EscapeHtml(anchorId)}\" class=\"material-inline-anchor\"><h4>{EscapeHtml(resolvedTitle)}</h4>{heroImageHtml}<div class=\"material-content\">{bodyHtml}</div></div>"
            : $"<aside id=\"{EscapeHtml(anchorId)}\" class=\"infobox\">{heroImageHtml}<header>{EscapeHtml(resolvedTitle)}</header><div class=\"infobox-content\">{bodyHtml}</div></aside>";
    }

    /// <summary>
    /// Builds HTML for a material hero image when one can be resolved.
    /// </summary>
    private string BuildMaterialHeroImageHtml(string referencePath, string sourceRoot)
    {
        string? imagePath = ResolveMaterialHeroImagePath(referencePath);
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        string resolvedImagePath = RewriteAssetPathForOutput(imagePath, referencePath, sourceRoot);
        return $"<img class=\"material-hero\" src=\"{EscapeHtml(resolvedImagePath)}\" alt=\"\" />";
    }

    /// <summary>
    /// Rewrites a local asset path for output or preview rendering.
    /// </summary>
    private string RewriteAssetPathForOutput(string assetPath, string currentFilePath, string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(assetPath)
            || !TryResolveLocalAssetPath(assetPath, currentFilePath, sourceRoot, out string absoluteAssetPath, out string suffix))
        {
            return assetPath;
        }

        if (_assetRewriteState is null)
        {
            return TryBuildEmbeddedPreviewAssetDataUri(absoluteAssetPath, out string embeddedAssetUri)
                ? embeddedAssetUri
                : string.Concat(new Uri(absoluteAssetPath, UriKind.Absolute).AbsoluteUri, suffix);
        }

        if (!string.Equals(Path.GetFullPath(sourceRoot), _assetRewriteState.SourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return assetPath;
        }

        string relativeAssetPath = Path.GetRelativePath(sourceRoot, absoluteAssetPath);
        if (relativeAssetPath.StartsWith("..", StringComparison.Ordinal))
        {
            return assetPath;
        }

        string webPath = $"assets/media/{relativeAssetPath.Replace('\\', '/')}";
        _assetRewriteState.RegisteredAssets[absoluteAssetPath] = webPath;
        return string.Concat(webPath, suffix);
    }

    /// <summary>
    /// Tries to build an embedded data URI for a small preview asset.
    /// </summary>
    private static bool TryBuildEmbeddedPreviewAssetDataUri(string absoluteAssetPath, out string dataUri)
    {
        dataUri = string.Empty;
        string extension = Path.GetExtension(absoluteAssetPath);
        string? mimeType = extension.ToUpperInvariant() switch
        {
            ".PNG" => "image/png",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".GIF" => "image/gif",
            ".WEBP" => "image/webp",
            ".SVG" => "image/svg+xml",
            _ => null,
        };
        if (mimeType is null)
        {
            return false;
        }

        FileInfo fileInfo = new(absoluteAssetPath);
        if (!fileInfo.Exists || fileInfo.Length <= 0 || fileInfo.Length > MaxEmbeddedPreviewAssetBytes)
        {
            return false;
        }

        byte[] bytes = File.ReadAllBytes(absoluteAssetPath);
        dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        return true;
    }

    /// <summary>
    /// Tries to resolve a local asset path and preserve its query or fragment suffix.
    /// </summary>
    private static bool TryResolveLocalAssetPath(string assetPath, string currentFilePath, string sourceRoot, out string absoluteAssetPath, out string suffix)
    {
        absoluteAssetPath = string.Empty;
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        (string pathWithoutSuffix, string parsedSuffix) = SplitAssetPathAndSuffix(assetPath);
        if (string.IsNullOrWhiteSpace(pathWithoutSuffix))
        {
            return false;
        }

        if (pathWithoutSuffix.StartsWith('#') ||
            pathWithoutSuffix.StartsWith("//", StringComparison.Ordinal) ||
            pathWithoutSuffix.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            pathWithoutSuffix.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            pathWithoutSuffix.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Uri.TryCreate(pathWithoutSuffix, UriKind.Absolute, out Uri? absoluteUri) && !absoluteUri.IsFile)
        {
            return false;
        }

        if (Uri.TryCreate(pathWithoutSuffix, UriKind.Absolute, out Uri? fileUri) && fileUri.IsFile)
        {
            absoluteAssetPath = Path.GetFullPath(fileUri.LocalPath);
        }
        else if (Path.IsPathRooted(pathWithoutSuffix))
        {
            string rootRelativeCandidate = pathWithoutSuffix.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string mappedPath = Path.GetFullPath(Path.Combine(sourceRoot, rootRelativeCandidate));
            absoluteAssetPath = File.Exists(mappedPath) ? mappedPath : Path.GetFullPath(pathWithoutSuffix);
        }
        else
        {
            absoluteAssetPath = ResolveReferencePath(currentFilePath, pathWithoutSuffix);
        }

        if (!File.Exists(absoluteAssetPath))
        {
            return false;
        }

        suffix = parsedSuffix;
        return true;
    }

    /// <summary>
    /// Splits an asset path into its base path and trailing query or fragment suffix.
    /// </summary>
    private static (string PathWithoutSuffix, string Suffix) SplitAssetPathAndSuffix(string value)
    {
        int queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        int hashIndex = value.IndexOf('#', StringComparison.Ordinal);
        int splitIndex = queryIndex switch
        {
            < 0 => hashIndex,
            _ when hashIndex < 0 => queryIndex,
            _ => Math.Min(queryIndex, hashIndex),
        };
        if (splitIndex < 0)
        {
            return (value, string.Empty);
        }

        return (value[..splitIndex], value[splitIndex..]);
    }

    /// <summary>
    /// Copies assets registered during rendering into the output media directory.
    /// </summary>
    private void CopyRegisteredAssets()
    {
        AssetRewriteState? state = _assetRewriteState;
        if (state is null || state.RegisteredAssets.Count == 0)
        {
            return;
        }

        foreach ((string sourceAssetPath, string webPath) in state.RegisteredAssets)
        {
            string destination = Path.Combine(state.OutputRoot, webPath.Replace('/', Path.DirectorySeparatorChar));
            string? parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(sourceAssetPath, destination, overwrite: true);
        }
    }

    /// <summary>
    /// Resolves a hero image path from markdown front matter or JSON payload data.
    /// </summary>
    private static string? ResolveMaterialHeroImagePath(string referencePath)
    {
        if (referencePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            ParsedMarkdown parsed = ParseMarkdownDocument(File.ReadAllText(referencePath));
            return FirstNonEmptyOrNull(GetValue(parsed.FrontMatter, "jumbotron"), GetValue(parsed.FrontMatter, "image"));
        }

        if (!referencePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(referencePath));
        JsonElement root = document.RootElement;
        return TryResolveFirstNonEmptyJsonStringPath(
            root,
            "largeAvatarUrl",
            "basicAvatarUrl",
            "avatarUrl",
            "ddb.character.decorations.avatarUrl",
            "ddb.decorations.avatarUrl",
            "jumbotron",
            "image");
    }

    /// <summary>
    /// Tries to resolve the first non-empty JSON string value from candidate paths.
    /// </summary>
    private static string? TryResolveFirstNonEmptyJsonStringPath(JsonElement root, params string[] paths)
    {
        foreach (string path in paths)
        {
            if (!TryResolveJsonPath(root, path, out JsonElement valueElement))
            {
                continue;
            }

            string value = valueElement.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Renders JSON material markdown, applying template rules when configured.
    /// </summary>
    private static string RenderJsonWithTemplate(string jsonPath)
    {
        return RenderJsonWithTemplateResult(jsonPath).Markdown;
    }

    /// <summary>
    /// Renders JSON material markdown and reports whether a template was applied.
    /// </summary>
    private static RenderedTemplateMarkdown RenderJsonWithTemplateResult(string jsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement root = document.RootElement;

        if (TryResolveJsonPath(root, "template", out JsonElement templateValue))
        {
            switch (templateValue.ValueKind)
            {
                case JsonValueKind.Null:
                    return RenderWithoutTemplate(root);
                case JsonValueKind.String:
                {
                    string configuredTemplate = templateValue.GetString() ?? string.Empty;
                    if (IsTemplateDisabled(configuredTemplate))
                    {
                        return RenderWithoutTemplate(root);
                    }

                    string configuredTemplatePath = ResolveEntityTemplatePath(jsonPath, configuredTemplate);
                    if (!File.Exists(configuredTemplatePath))
                    {
                        throw new InvalidOperationException($"Template '{configuredTemplate}' was not found for '{jsonPath}'.");
                    }

                    string? resolvedTemplate = LoadTemplateBodyIfEnabled(configuredTemplatePath);
                    return string.IsNullOrWhiteSpace(resolvedTemplate) ? RenderWithoutTemplate(root) : new(ApplyTemplate(resolvedTemplate, root, jsonPath), TemplateApplied: true);
                }
                default:
                    throw new InvalidOperationException($"Template override for '{jsonPath}' must be a string or null.");
            }
        }

        string localTemplatePath = ResolveEntityTemplatePath(jsonPath, "./TEMPLATE.md");
        if (!File.Exists(localTemplatePath)) return RenderWithoutTemplate(root);
        string? localTemplate = LoadTemplateBodyIfEnabled(localTemplatePath);
        return !string.IsNullOrWhiteSpace(localTemplate)
            ? new(ApplyTemplate(localTemplate, root, jsonPath), TemplateApplied: true)
            : RenderWithoutTemplate(root);
    }

    /// <summary>
    /// Renders markdown material content and applies configured templates when enabled.
    /// </summary>
    private static RenderedTemplateMarkdown RenderMarkdownWithTemplate(string markdownPath, string rawMarkdown)
    {
        ParsedMarkdown parsed = ParseMarkdownDocument(rawMarkdown);
        if (TryGetFrontMatterValue(parsed.FrontMatter, "template", out string configuredTemplate))
        {
            if (IsTemplateDisabled(configuredTemplate))
            {
                return new(parsed.Body, TemplateApplied: false);
            }

            string configuredTemplatePath = ResolveEntityTemplatePath(markdownPath, configuredTemplate);
            if (!File.Exists(configuredTemplatePath))
            {
                throw new InvalidOperationException($"Template '{configuredTemplate}' was not found for '{markdownPath}'.");
            }

            string? template = LoadTemplateBodyIfEnabled(configuredTemplatePath);
            if (string.IsNullOrWhiteSpace(template))
            {
                return new(parsed.Body, TemplateApplied: false);
            }

            JsonElement model = BuildMarkdownTemplateModel(parsed);
            return new(ApplyTemplate(template, model, markdownPath), TemplateApplied: true);
        }

        string localTemplatePath = ResolveEntityTemplatePath(markdownPath, "./TEMPLATE.md");
        if (!File.Exists(localTemplatePath)) return new(parsed.Body, TemplateApplied: false);
        {
            string? template = LoadTemplateBodyIfEnabled(localTemplatePath);
            if (string.IsNullOrWhiteSpace(template))
            {
                return new(parsed.Body, TemplateApplied: false);
            }

            JsonElement model = BuildMarkdownTemplateModel(parsed);
            return new(ApplyTemplate(template, model, markdownPath), TemplateApplied: true);
        }
    }

    /// <summary>
    /// Builds a template model from markdown front matter and body content.
    /// </summary>
    private static JsonElement BuildMarkdownTemplateModel(ParsedMarkdown parsed)
    {
        Dictionary<string, string> model = new(parsed.FrontMatter, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(parsed.Body))
        {
            model["content"] = parsed.Body;
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(model));
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Tries to read a front-matter value using case-insensitive key matching.
    /// </summary>
    private static bool TryGetFrontMatterValue(Dictionary<string, string> frontMatter, string key, out string value)
    {
        foreach ((string frontMatterKey, string frontMatterValue) in frontMatter)
        {
            if (!string.Equals(frontMatterKey, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = frontMatterValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Determines whether a template setting explicitly disables template usage.
    /// </summary>
    private static bool IsTemplateDisabled(string templateValue)
    {
        string normalized = templateValue.Trim();
        return normalized.Length == 0 || string.Equals(normalized, "~", StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves an entity template path relative to the entity file when needed.
    /// </summary>
    private static string ResolveEntityTemplatePath(string entityPath, string templatePath)
    {
        string normalizedTemplatePath = templatePath.Trim();
        if (Path.IsPathRooted(normalizedTemplatePath))
        {
            return Path.GetFullPath(normalizedTemplatePath);
        }

        string entityDirectory = Path.GetDirectoryName(entityPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(entityDirectory, normalizedTemplatePath));
    }

    /// <summary>
    /// Loads template body content when the template is enabled.
    /// </summary>
    private static string? LoadTemplateBodyIfEnabled(string templatePath)
    {
        string rawTemplate = File.ReadAllText(templatePath);
        if (string.IsNullOrWhiteSpace(rawTemplate))
        {
            return null;
        }

        ParsedMarkdown parsedTemplate = ParseMarkdownDocument(rawTemplate);
        if (TryGetFrontMatterValue(parsedTemplate.FrontMatter, "enabled", out string enabledValue) &&
            IsTemplateEnabledFalse(enabledValue))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(parsedTemplate.Body)
            ? null
            : parsedTemplate.Body;
    }

    /// <summary>
    /// Determines whether a template enabled flag represents a disabled state.
    /// </summary>
    private static bool IsTemplateEnabledFalse(string enabledValue)
    {
        string normalized = enabledValue.Trim();
        return string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "0", StringComparison.Ordinal) ||
               string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renders fallback markdown when no template should be applied.
    /// </summary>
    private static RenderedTemplateMarkdown RenderWithoutTemplate(JsonElement root)
    {
        if (root.TryGetProperty("content", out JsonElement content))
        {
            return new(content.ToString(), TemplateApplied: false);
        }

        return new("No content.", TemplateApplied: false);
    }
}
