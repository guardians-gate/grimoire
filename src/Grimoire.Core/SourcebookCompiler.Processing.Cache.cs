using Blake2Fast;
using Blake2Fast.Implementation;
using System.Diagnostics;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Provides mention-link helpers and preview-render caching utilities.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Tries to match a mention candidate at the current markdown position.
    /// </summary>
    private static bool TryMatchMention(
        string markdown,
        int position,
        IReadOnlyList<KeyValuePair<string, string>> candidates,
        string? currentEntityTitle,
        Regex? excludeLinksRegex,
        out string matchedText,
        out string matchedName,
        out string href)
    {
        foreach ((string name, string candidateHref) in candidates)
        {
            if (!string.IsNullOrWhiteSpace(currentEntityTitle) &&
                string.Equals(name, currentEntityTitle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (position + name.Length > markdown.Length ||
                !IsMentionBoundaryAfter(markdown, position + name.Length) ||
                string.Compare(markdown, position, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            string candidateText = markdown.Substring(position, name.Length);
            if (excludeLinksRegex is not null && excludeLinksRegex.IsMatch(candidateText))
            {
                continue;
            }

            matchedText = candidateText;
            matchedName = name;
            href = candidateHref;
            return true;
        }

        matchedText = string.Empty;
        matchedName = string.Empty;
        href = string.Empty;
        return false;
    }

    /// <summary>
    /// Determines whether a position has a valid mention boundary before it.
    /// </summary>
    private static bool IsMentionBoundaryBefore(string markdown, int position)
    {
        return position <= 0 || !char.IsLetterOrDigit(markdown[position - 1]);
    }

    /// <summary>
    /// Determines whether a position has a valid mention boundary after it.
    /// </summary>
    private static bool IsMentionBoundaryAfter(string markdown, int position)
    {
        return position >= markdown.Length || !char.IsLetterOrDigit(markdown[position]);
    }

    /// <summary>
    /// Determines whether preview auto-link progress should be logged for an item.
    /// </summary>
    private static bool ShouldLogPreviewAutoLinkProgress(int index, int total)
    {
        if (total <= 0)
        {
            return false;
        }

        int interval = Math.Max(1, total / 100);
        return index == 1 || index == total || index % interval == 0;
    }

    /// <summary>
    /// Protects markdown heading segments so they are skipped during link replacement.
    /// </summary>
    private static string ProtectHeadingSegments(string markdown, List<string> protectedSegments)
    {
        string prepared = MarkdownAtxHeadingLineRegex.Replace(markdown, match =>
        {
            protectedSegments.Add(match.Value);
            return $"@@GRIMOIRE{protectedSegments.Count - 1}@@";
        });

        return MarkdownSetextHeadingBlockRegex.Replace(prepared, match =>
        {
            protectedSegments.Add(match.Value);
            return $"@@GRIMOIRE{protectedSegments.Count - 1}@@";
        });
    }

    /// <summary>
    /// Resolves and caches the exclude-links regular expression from markdown front matter.
    /// </summary>
    private Regex? ResolveExcludeLinksRegex(string currentFilePath)
    {
        if (!currentFilePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_excludeLinksRegexCache.TryGetValue(currentFilePath, out Regex? cached))
        {
            return cached;
        }

        string raw = File.ReadAllText(currentFilePath);
        ParsedMarkdown parsed = ParseMarkdownDocument(raw);
        string? pattern = GetValue(parsed.FrontMatter, "excludeLinks");
        if (string.IsNullOrWhiteSpace(pattern))
        {
            _excludeLinksRegexCache[currentFilePath] = null;
            return null;
        }

        try
        {
            Regex regex = new(pattern.Trim(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
            _excludeLinksRegexCache[currentFilePath] = regex;
            return regex;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"Invalid excludeLinks regex in '{currentFilePath}': {pattern}", exception);
        }
    }

    /// <summary>
    /// Resolves whether heading links are enabled for a markdown source file.
    /// </summary>
    private bool ResolveHeadingLinksEnabled(string currentFilePath)
    {
        if (!currentFilePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_headingLinksEnabledCache.TryGetValue(currentFilePath, out bool cachedEnabled))
        {
            return cachedEnabled;
        }

        string raw = File.ReadAllText(currentFilePath);
        ParsedMarkdown parsed = ParseMarkdownDocument(raw);
        bool enabled = GetBooleanValue(parsed.FrontMatter, "headingLinks") ?? false;
        _headingLinksEnabledCache[currentFilePath] = enabled;
        return enabled;
    }

    /// <summary>
    /// Builds anchor markup for a linked entity mention.
    /// </summary>
    private static string BuildMentionAnchor(string text, string href)
    {
        return $"<a href=\"{EscapeHtml(href)}\">{EscapeHtml(text)}</a>";
    }

    /// <summary>
    /// Builds mention markup and emits a unique mention target anchor when applicable.
    /// </summary>
    private string BuildMentionMarkup(string text, string href, string topicTitle, string? mentionTargetId, HashSet<string> emittedMentionAnchors)
    {
        string anchorHtml = BuildMentionAnchor(text, href);
        if (string.IsNullOrWhiteSpace(mentionTargetId))
        {
            return anchorHtml;
        }

        string mentionAnchorId = $"{mentionTargetId}-mention-{BuildSectionId(topicTitle)}";
        RegisterEntityMentionTarget(topicTitle, mentionAnchorId);
        return emittedMentionAnchors.Add(mentionAnchorId)
            ? $"<span id=\"{EscapeHtml(mentionAnchorId)}\"></span>{anchorHtml}"
            : anchorHtml;
    }

    /// <summary>
    /// Builds preview link targets for indexed topics that map to source files.
    /// </summary>
    private static Dictionary<string, string> BuildPreviewLinkTargets(string sourceRoot, IEnumerable<IndexTopic> topics)
    {
        Dictionary<string, string> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (IndexTopic topic in topics)
        {
            if (string.IsNullOrWhiteSpace(topic.SourcePath))
            {
                continue;
            }

            string href = ResolveWebsiteMentionHref(topic);
            if (string.IsNullOrWhiteSpace(href) || href.Contains("index-topics.html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string absoluteSourcePath = Path.GetFullPath(Path.Combine(sourceRoot, topic.SourcePath));
            if (File.Exists(absoluteSourcePath))
            {
                targets[href] = ToWebPath(topic.SourcePath);
            }
        }

        return targets;
    }

    /// <summary>
    /// Adds preview link targets for configured entity mention links.
    /// </summary>
    private void AddPreviewEntityMentionLinkTargets(string sourceRoot, Dictionary<string, string> targets)
    {
        foreach ((string title, string href) in _entityMentionLinks)
        {
            if (string.IsNullOrWhiteSpace(href) ||
                href.Contains("index-topics.html", StringComparison.OrdinalIgnoreCase) ||
                !_entityMentionSourcePaths.TryGetValue(title, out string? sourcePath) ||
                string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (relativePath.Equals("..", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
                Path.IsPathRooted(relativePath))
            {
                continue;
            }

            targets.TryAdd(href, ToWebPath(relativePath));
        }
    }

    /// <summary>
    /// Gets the in-memory preview render cache, rebuilding it when required.
    /// </summary>
    private async Task<(PreviewRenderCache Cache, bool CacheHit, long CacheBuildElapsedMs)> GetPreviewRenderCacheAsync(
        string sourceRoot,
        bool rebuildDiskCacheArtifacts,
        IReadOnlyDictionary<string, string> contentHashes,
        CancellationToken cancellationToken)
    {
        string normalizedRoot = Path.GetFullPath(sourceRoot);
        if (_previewRenderCache is not null &&
            !rebuildDiskCacheArtifacts &&
            string.Equals(_previewRenderCacheRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            PreviewRenderCacheHit(normalizedRoot, _previewRenderCache.IndexTopics.Count, _previewRenderCache.LinkTargets.Count);
            return (_previewRenderCache, true, 0);
        }

        PreviewRenderCacheMiss(normalizedRoot);
        var cacheTimer = Stopwatch.StartNew();
        RenderOptions renderOptions = LoadRenderOptions(normalizedRoot, ExportTarget.Website);
        List<IndexTopic> indexTopics = await LoadIndexTopicsAsync(
            normalizedRoot,
            renderOptions.IncludeUnreferencedSnippetsInAppendix,
            renderOptions.GenerateReferenceDictionary,
            renderOptions.ShadowReferences,
            cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> linkTargets = BuildPreviewLinkTargets(normalizedRoot, indexTopics);
        ProjectMetadata metadata = await LoadProjectMetadataAsync(normalizedRoot, cancellationToken).ConfigureAwait(false);
        ProjectPageSubstitutionValues substitutions = BuildPreviewProjectSubstitutionValues(normalizedRoot, indexTopics, metadata);

        _previewRenderCacheRoot = normalizedRoot;
        _previewRenderCache = new PreviewRenderCache(renderOptions, indexTopics, linkTargets, metadata, substitutions);
        if (rebuildDiskCacheArtifacts)
        {
            _previewFileRenderCache.Clear();
            await WritePreviewDiskCacheMetadataAsync(normalizedRoot, indexTopics, contentHashes, clearGeneratedCache: true, cancellationToken).ConfigureAwait(false);
        }

        cacheTimer.Stop();
        PreviewRenderCacheBuilt(
            normalizedRoot,
            indexTopics.Count,
            linkTargets.Count,
            substitutions.DynamicValues.Count,
            cacheTimer.ElapsedMilliseconds);
        return (_previewRenderCache, false, cacheTimer.ElapsedMilliseconds);
    }

    /// <summary>
    /// Builds preview-time project substitution values from indexed topics and metadata.
    /// </summary>
    private static ProjectPageSubstitutionValues BuildPreviewProjectSubstitutionValues(
        string sourceRoot,
        IReadOnlyCollection<IndexTopic> indexTopics,
        ProjectMetadata metadata)
    {
        string contentRoot = Path.Combine(sourceRoot, "content");
        int chapterCount = Directory.Exists(contentRoot)
            ? Directory.GetFiles(contentRoot, "*.md", SearchOption.TopDirectoryOnly).Length
            : 0;
        int referenceCount = indexTopics.Count(static topic =>
            topic.TargetIds.Any(static targetId =>
                targetId.StartsWith("dict-ref-", StringComparison.OrdinalIgnoreCase) &&
                !targetId.Contains("-mention-", StringComparison.OrdinalIgnoreCase)));

        Dictionary<string, string> dynamicValues = new(StringComparer.OrdinalIgnoreCase)
        {
            ["macro.title"] = metadata.Title,
            ["macro.author"] = metadata.Author ?? string.Empty,
            ["macro.license"] = metadata.License ?? string.Empty,
            ["macro.chapterCount"] = chapterCount.ToString(CultureInfo.InvariantCulture),
            ["macro.indexTopicCount"] = indexTopics.Count.ToString(CultureInfo.InvariantCulture),
            ["macro.referenceCount"] = referenceCount.ToString(CultureInfo.InvariantCulture),
            ["macro.dateUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["macro.generatedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        return new(
            1,
            new(StringComparer.OrdinalIgnoreCase),
            dynamicValues);
    }

    /// <summary>
    /// Builds a stable in-memory cache key for rendered preview files.
    /// </summary>
    private static string BuildPreviewFileCacheKey(string sourceRoot, string relativePath)
    {
        return $"{sourceRoot}|{relativePath}";
    }

    /// <summary>
    /// Builds file-system paths used by the preview disk cache.
    /// </summary>
    private static PreviewDiskCachePaths BuildPreviewDiskCachePaths(string sourceRoot)
    {
        string cacheRoot = Path.Combine(sourceRoot, PreviewCachesDirectoryName);
        string generatedRoot = Path.Combine(cacheRoot, PreviewCacheGeneratedDirectoryName);
        return new(
            cacheRoot,
            generatedRoot,
            Path.Combine(cacheRoot, PreviewCacheHashesFileName),
            Path.Combine(cacheRoot, PreviewCacheStateFileName),
            Path.Combine(cacheRoot, PreviewCacheTopicsFileName));
    }

    /// <summary>
    /// Validates preview disk-cache metadata against current content hashes and topic state.
    /// </summary>
    private static async Task<PreviewDiskCacheValidation> ValidatePreviewDiskCacheAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        PreviewDiskCachePaths paths = BuildPreviewDiskCachePaths(sourceRoot);
        Directory.CreateDirectory(paths.CacheRoot);
        Directory.CreateDirectory(paths.GeneratedRoot);

        Dictionary<string, string> currentHashes = await ComputePreviewContentHashesAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(paths.HashesPath) || !File.Exists(paths.StatePath) || !File.Exists(paths.TopicsPath))
        {
            return new(paths, currentHashes, null, IsValid: false);
        }

        Dictionary<string, string>? persistedHashes = await TryLoadStringDictionaryAsync(paths.HashesPath, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string>? persistedTopics = await TryLoadStringDictionaryAsync(paths.TopicsPath, cancellationToken).ConfigureAwait(false);
        PreviewCacheStateDocument? persistedState = await TryLoadPreviewCacheStateAsync(paths.StatePath, cancellationToken).ConfigureAwait(false);
        if (persistedHashes is null || persistedTopics is null || persistedState is null)
        {
            return new(paths, currentHashes, null, IsValid: false);
        }

        Dictionary<string, string> normalizedPersistedHashes = NormalizePersistedHashes(persistedHashes);
        if (!DictionariesEquivalent(currentHashes, normalizedPersistedHashes))
        {
            return new(paths, currentHashes, null, IsValid: false);
        }

        Dictionary<string, string> normalizedPersistedTopics = NormalizePersistedTopics(persistedTopics);
        PreviewTopicState computedState = BuildPreviewTopicState(normalizedPersistedTopics);
        bool matchesState = persistedState.IndexedTopicCount == computedState.IndexedTopicCount &&
                            persistedState.IndexedEntityCount == computedState.IndexedEntityCount &&
                            string.Equals(persistedState.IndexedNamesHashBlake2b512, computedState.IndexedNamesHashBlake2b512, StringComparison.OrdinalIgnoreCase);
        if (!matchesState)
        {
            return new(paths, currentHashes, null, IsValid: false);
        }

        return new(paths, currentHashes, persistedState, IsValid: true);
    }

    /// <summary>
    /// Computes BLAKE2b-512 hashes for cache-relevant source files.
    /// </summary>
    private static async Task<Dictionary<string, string>> ComputePreviewContentHashesAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);
        string[] files =
        [
            .. Directory.GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
                .Where(filePath =>
                {
                    string extension = Path.GetExtension(filePath);
                    return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
                })
                .Where(filePath =>
                {
                    string relativePath = Path.GetRelativePath(sourceRoot, filePath);
                    return !IsPreviewCacheIgnoredRelativePath(relativePath);
                })
                .OrderBy(static filePath => filePath, StringComparer.OrdinalIgnoreCase),
        ];

        foreach (string filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = ToWebPath(Path.GetRelativePath(sourceRoot, filePath));
            hashes[relativePath] = await ComputeBlake2b512HexForFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        return hashes;
    }

    /// <summary>
    /// Determines whether a relative path should be ignored by preview caching.
    /// </summary>
    private static bool IsPreviewCacheIgnoredRelativePath(string relativePath)
    {
        string normalized = ToWebPath(relativePath);
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => PreviewIgnoredDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Computes the BLAKE2b-512 hash of a file and returns it as hexadecimal text.
    /// </summary>
    private static async Task<string> ComputeBlake2b512HexForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        Blake2bHashState hasher = Blake2b.CreateIncrementalHasher();
        try
        {
            await using FileStream stream = File.OpenRead(filePath);
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                hasher.Update(buffer.AsSpan(0, bytesRead));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        byte[] digest = hasher.Finish();
        return Convert.ToHexString(digest);
    }

    /// <summary>
    /// Tries to load a string dictionary from JSON and normalize key casing behavior.
    /// </summary>
    private static async Task<Dictionary<string, string>?> TryLoadStringDictionaryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (parsed is null)
            {
                return null;
            }

            Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string value) in parsed)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                normalized[key.Trim()] = value.Trim();
            }

            return normalized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to load the persisted preview cache state document.
    /// </summary>
    private static async Task<PreviewCacheStateDocument?> TryLoadPreviewCacheStateAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PreviewCacheStateDocument>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Determines whether two dictionaries contain equivalent keys and values.
    /// </summary>
    private static bool DictionariesEquivalent(Dictionary<string, string> left, Dictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, string value) in left)
        {
            if (!right.TryGetValue(key, out string? rightValue) ||
                !string.Equals(value, rightValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Normalizes persisted content hashes for consistent cache comparison.
    /// </summary>
    private static Dictionary<string, string> NormalizePersistedHashes(IReadOnlyDictionary<string, string> persisted)
    {
        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in persisted)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[ToWebPath(key.Trim())] = value.Trim();
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes persisted topic mappings for consistent cache comparison.
    /// </summary>
    private static Dictionary<string, string> NormalizePersistedTopics(IReadOnlyDictionary<string, string> persisted)
    {
        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in persisted)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!normalized.ContainsKey(key.Trim()))
            {
                normalized[key.Trim()] = ToWebPath(value.Trim());
            }
        }

        return normalized;
    }

    /// <summary>
    /// Builds a map from topic titles to normalized source paths for preview state.
    /// </summary>
    private static Dictionary<string, string> BuildPreviewTopicMap(IEnumerable<IndexTopic> indexTopics)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (IndexTopic topic in indexTopics)
        {
            if (string.IsNullOrWhiteSpace(topic.Title) || string.IsNullOrWhiteSpace(topic.SourcePath))
            {
                continue;
            }

            string normalizedPath = ToWebPath(topic.SourcePath);
            map.TryAdd(topic.Title, normalizedPath);
        }

        return map;
    }

    /// <summary>
    /// Builds aggregate preview topic-state counters and name hash values.
    /// </summary>
    private static PreviewTopicState BuildPreviewTopicState(IReadOnlyDictionary<string, string> topics)
    {
        int indexedTopicCount = 0;
        int indexedEntityCount = 0;
        foreach ((string _, string relativePath) in topics)
        {
            if (relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
            {
                indexedTopicCount++;
            }
            else
            {
                indexedEntityCount++;
            }
        }

        string namesConcat = string.Join(
            "\n",
            topics.Keys
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
        string namesHash = Convert.ToHexString(Blake2b.ComputeHash(Encoding.UTF8.GetBytes(namesConcat)));
        return new PreviewTopicState(indexedTopicCount, indexedEntityCount, namesHash);
    }

    /// <summary>
    /// Writes preview disk-cache metadata documents and optionally clears generated entries.
    /// </summary>
    private static async Task WritePreviewDiskCacheMetadataAsync(
        string sourceRoot,
        IReadOnlyList<IndexTopic> indexTopics,
        IReadOnlyDictionary<string, string> contentHashes,
        bool clearGeneratedCache,
        CancellationToken cancellationToken)
    {
        PreviewDiskCachePaths paths = BuildPreviewDiskCachePaths(sourceRoot);
        Directory.CreateDirectory(paths.CacheRoot);
        Directory.CreateDirectory(paths.GeneratedRoot);

        if (clearGeneratedCache)
        {
            foreach (string file in Directory.GetFiles(paths.GeneratedRoot, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(file);
            }
        }

        Dictionary<string, string> topicMap = BuildPreviewTopicMap(indexTopics);
        PreviewTopicState state = BuildPreviewTopicState(topicMap);
        PreviewCacheStateDocument stateDocument = new(
            state.IndexedTopicCount,
            state.IndexedEntityCount,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            state.IndexedNamesHashBlake2b512);

        await File.WriteAllTextAsync(paths.HashesPath, JsonSerializer.Serialize(contentHashes, PreviewCacheJsonIndentedOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(paths.TopicsPath, JsonSerializer.Serialize(topicMap, PreviewCacheJsonIndentedOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(paths.StatePath, JsonSerializer.Serialize(stateDocument, PreviewCacheJsonIndentedOptions), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tries to read a generated preview cache entry for a specific source file hash.
    /// </summary>
    private static async Task<PreviewGeneratedCacheEntry?> TryReadGeneratedPreviewCacheAsync(
        PreviewDiskCachePaths paths,
        string relativePath,
        string sourceContentHash,
        CancellationToken cancellationToken)
    {
        string cachePath = BuildGeneratedPreviewCachePath(paths.GeneratedRoot, relativePath);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            PreviewGeneratedCacheDocument? document = JsonSerializer.Deserialize<PreviewGeneratedCacheDocument>(json);
            if (document is null ||
                !string.Equals(document.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(document.SourceContentHashBlake2b512, sourceContentHash, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(document.Html))
            {
                return null;
            }

            Dictionary<string, string> linkTargets = new(document.LinkTargets, StringComparer.OrdinalIgnoreCase);
            return new(document.Html, linkTargets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a generated preview cache entry for rendered HTML content.
    /// </summary>
    private static async Task WriteGeneratedPreviewCacheAsync(
        PreviewDiskCachePaths paths,
        string relativePath,
        string sourceContentHash,
        string html,
        IReadOnlyDictionary<string, string> linkTargets,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.GeneratedRoot);
        string cachePath = BuildGeneratedPreviewCachePath(paths.GeneratedRoot, relativePath);
        PreviewGeneratedCacheDocument document = new(
            RelativePath: relativePath,
            SourceContentHashBlake2b512: sourceContentHash,
            Html: html,
            LinkTargets: new Dictionary<string, string>(linkTargets, StringComparer.OrdinalIgnoreCase),
            CachedUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(document, PreviewCacheJsonCompactOptions), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the generated preview cache file path for a relative source path.
    /// </summary>
    private static string BuildGeneratedPreviewCachePath(string generatedRoot, string relativePath)
    {
        string key = Convert.ToHexString(Blake2b.ComputeHash(Encoding.UTF8.GetBytes(relativePath)));
        return Path.Combine(generatedRoot, $"{key}.json");
    }

    /// <summary>
    /// Converts a path to a web-style slash-separated representation.
    /// </summary>
    private static string ToWebPath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Rewrites rendered HTML links to preview protocol targets.
    /// </summary>
    private static string RewritePreviewLinks(string html, Dictionary<string, string> linkTargets)
    {
        if (string.IsNullOrEmpty(html) || linkTargets.Count == 0)
        {
            return html;
        }

        Dictionary<string, string> previewHrefByRenderedHref = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string href, string sourcePath) in linkTargets)
        {
            string previewHref = "grimoire://open?path=" + Uri.EscapeDataString(sourcePath);
            previewHrefByRenderedHref[EscapeHtml(href)] = EscapeHtml(previewHref);
        }

        return HtmlHrefAttributeRegex.Replace(html, match =>
        {
            string renderedHref = match.Groups["href"].Value;
            return previewHrefByRenderedHref.TryGetValue(renderedHref, out string? previewHref)
                ? $"href=\"{previewHref}\""
                : match.Value;
        });
    }
}
