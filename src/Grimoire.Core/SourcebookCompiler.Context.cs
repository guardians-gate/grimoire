using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Builds render context data and discovers source materials for compilation.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// A <see cref="int"/> indicating the material-entry count above which PDF appendix rendering switches to compact mode.
    /// </summary>
    private const int CompactPdfAppendixEntryThreshold = 200;

    /// <summary>
    /// Builds the complete render context used to generate output documents.
    /// </summary>
    private async Task<RenderContext> BuildRenderContextAsync(string sourceRoot, string outputDirectory, ExportTarget target, CancellationToken cancellationToken)
    {
        LoadingRenderContext(target, sourceRoot);
        FontSettings fontSettings = LoadFontSettings(sourceRoot);
        RenderOptions renderOptions = LoadRenderOptions(sourceRoot, target);
        string fontsSourcePath = Path.Combine(sourceRoot, "settings", "fonts");
        string fontsOutputPath = Path.Combine(outputDirectory, "assets", "fonts");
        CopyingFontAssets(fontsSourcePath, fontsOutputPath);
        List<FontAsset> copiedFonts = CopyFontAssets(fontsSourcePath, fontsOutputPath);
        LoadingIndexTopics();
        List<IndexTopic> indexTopics = await LoadIndexTopicsAsync(
            sourceRoot,
            renderOptions.IncludeUnreferencedSnippetsInAppendix,
            renderOptions.GenerateReferenceDictionary,
            renderOptions.ShadowReferences,
            cancellationToken).ConfigureAwait(false);
        LoadedIndexTopics(indexTopics.Count);
        ConfigureEntityMentionLinks(
            indexTopics,
            target,
            renderOptions.AutoLinkEntityMentions,
            renderOptions.GenerateReferenceDictionary,
            sourceRoot);

        LoadingContentSections();
        List<ContentSection> sections = await LoadSectionsAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        if (renderOptions.GenerateReferenceDictionary)
        {
            BuildingReferenceDictionarySection();
            ContentSection? referenceDictionarySection = await BuildReferencedSnippetDictionarySectionAsync(sourceRoot, renderOptions.ShadowReferences, cancellationToken).ConfigureAwait(false);
            if (referenceDictionarySection is not null)
            {
                sections.Add(referenceDictionarySection);
            }
        }

        if (renderOptions.IncludeUnreferencedSnippetsInAppendix)
        {
            BuildingUnreferencedAppendixSection();
            ContentSection? appendixSection = await BuildUnreferencedSnippetAppendixSectionAsync(
                sourceRoot,
                target == ExportTarget.Pdf,
                cancellationToken).ConfigureAwait(false);
            if (appendixSection is not null)
            {
                sections.Add(appendixSection);
            }
        }

        LoadingCoverMetadataBibliography();
        string? coverHtml = await LoadCoverHtmlAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        ProjectMetadata metadata = await LoadProjectMetadataAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(metadata.CoverJumbotron))
        {
            metadata = metadata with
            {
                CoverJumbotron = RewriteAssetPathForOutput(metadata.CoverJumbotron, Path.Combine(sourceRoot, "TITLE.md"), sourceRoot),
            };
        }
        string? bibliographyHtml = await LoadBibliographyHtmlAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
        ApplyMentionTargetsToIndexTopics(indexTopics, sourceRoot);
        string title = InferProjectTitle(sourceRoot);
        RenderContextReady(sections.Count, indexTopics.Count, copiedFonts.Count);
        return new(title, sourceRoot, sections, coverHtml, metadata, indexTopics, bibliographyHtml, fontSettings, copiedFonts, renderOptions);
    }

    /// <summary>
    /// Infers a default project title from the source root directory name.
    /// </summary>
    private static string InferProjectTitle(string sourceRoot)
    {
        string folderName = Path.GetFileName(sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(folderName)
            ? "Grimoire Sourcebook"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(folderName.Replace('-', ' '));
    }

    /// <summary>
    /// Loads chapter and foreword sections from markdown content files.
    /// </summary>
    private async Task<List<ContentSection>> LoadSectionsAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        List<ContentSection> sections = [];
        string contentRoot = Path.Combine(sourceRoot, "content");
        if (Directory.Exists(contentRoot))
        {
            string[] chapterFiles = Directory.GetFiles(contentRoot, "*.md", SearchOption.TopDirectoryOnly);
            Array.Sort(chapterFiles, StringComparer.OrdinalIgnoreCase);
            LoadingChapterMarkdownFiles(chapterFiles.Length, contentRoot);
            foreach (string filePath in chapterFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string raw = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                ParsedMarkdown parsed = ParseMarkdownDocument(raw);
                string title = ResolveSectionTitle(filePath, parsed);
                string processed = ProcessInlineTokens(parsed.Body, filePath, sourceRoot, currentPageTitleOverride: title, contentPageTitleOverride: title);
                string html = RenderMarkdownToHtml(processed);
                string? jumbotron = parsed.FrontMatter.TryGetValue("jumbotron", out string? value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : null;
                if (!string.IsNullOrWhiteSpace(jumbotron))
                {
                    jumbotron = RewriteAssetPathForOutput(jumbotron, filePath, sourceRoot);
                }
                sections.Add(new ContentSection(title, html, BuildSectionId(filePath), jumbotron));
            }
        }

        string readmePath = Path.Combine(sourceRoot, "README.md");
        if (!File.Exists(readmePath)) return sections;

        string readme = await File.ReadAllTextAsync(readmePath, cancellationToken).ConfigureAwait(false);
        ParsedMarkdown parsedReadme = ParseMarkdownDocument(readme);
        string processedReadme = ProcessInlineTokens(parsedReadme.Body, readmePath, sourceRoot, currentPageTitleOverride: "Foreword", contentPageTitleOverride: "Foreword");
        string readmeHtml = RenderMarkdownToHtml(processedReadme);
        sections.Insert(0, new("Foreword", readmeHtml, "foreword", null));
        return sections;
    }

    /// <summary>
    /// Builds the appendix section that includes unreferenced material snippets.
    /// </summary>
    private async Task<ContentSection?> BuildUnreferencedSnippetAppendixSectionAsync(
        string sourceRoot,
        bool compactForPdf,
        CancellationToken cancellationToken)
    {
        HashSet<string> referenceableMaterialPaths = LoadReferenceableMaterialPaths(sourceRoot);
        if (referenceableMaterialPaths.Count == 0)
        {
            return null;
        }

        HashSet<string> referencedMaterialPaths = await LoadReferencedMaterialPathsAsync(sourceRoot, includeMacros: true, cancellationToken).ConfigureAwait(false);
        string[] unreferencedMaterialFiles =
        [
            .. referenceableMaterialPaths
                .Where(path => !referencedMaterialPaths.Contains(path))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
        ];

        if (unreferencedMaterialFiles.Length == 0)
        {
            return null;
        }

        return await BuildContinuousMaterialsAppendixSectionAsync(
            sourceRoot,
            unreferencedMaterialFiles,
            "Unreferenced Materials Appendix",
            "appendix-snippets",
            null,
            compactForPdf,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the reference dictionary section from referenced and shadowed materials.
    /// </summary>
    private async Task<ContentSection?> BuildReferencedSnippetDictionarySectionAsync(string sourceRoot, IReadOnlyCollection<string> shadowReferences, CancellationToken cancellationToken)
    {
        HashSet<string> referenceableMaterialPaths = LoadReferenceableMaterialPaths(sourceRoot);
        if (referenceableMaterialPaths.Count == 0)
        {
            return null;
        }

        HashSet<string> dictionaryMaterialPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> referencedMaterialPaths = await LoadReferencedMaterialPathsAsync(sourceRoot, includeMacros: true, cancellationToken).ConfigureAwait(false);
        foreach (string materialPath in referenceableMaterialPaths.Where(referencedMaterialPaths.Contains))
        {
            cancellationToken.ThrowIfCancellationRequested();
            dictionaryMaterialPaths.Add(materialPath);
        }

        foreach ((string mentionTitle, HashSet<string> _) in _entityMentionTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_entityMentionSourcePaths.TryGetValue(mentionTitle, out string? mentionSourcePath) &&
                !string.IsNullOrWhiteSpace(mentionSourcePath) &&
                referenceableMaterialPaths.Contains(mentionSourcePath))
            {
                dictionaryMaterialPaths.Add(mentionSourcePath);
            }
        }

        foreach (string shadowPath in LoadShadowReferenceMaterialPaths(sourceRoot, shadowReferences))
        {
            cancellationToken.ThrowIfCancellationRequested();
            dictionaryMaterialPaths.Add(shadowPath);
        }

        string[] referencedSnippetFiles =
        [
            .. dictionaryMaterialPaths
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
        ];

        if (referencedSnippetFiles.Length == 0)
        {
            return null;
        }

        var baselineMentionTargets = _entityMentionTargets.ToDictionary(
            entry => entry.Key,
            entry => new HashSet<string>(entry.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entityMentionTargets.Clear();
            foreach ((string topicTitle, HashSet<string> targets) in baselineMentionTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _entityMentionTargets[topicTitle] = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
            }

            referencedSnippetFiles =
            [
                .. dictionaryMaterialPaths
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            ];

            if (referencedSnippetFiles.Length == 0)
            {
                return null;
            }

            ContentSection? section = await BuildContinuousMaterialsAppendixSectionAsync(
                sourceRoot,
                referencedSnippetFiles,
                "Reference Dictionary",
                "appendix-reference-dictionary",
                "dict",
                compactForPdf: false,
                cancellationToken).ConfigureAwait(false);
            if (section is null)
            {
                return null;
            }

            bool expanded = false;
            string[] mentionTitles = [.. _entityMentionTargets.Keys];
            foreach (string mentionTitle in mentionTitles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_entityMentionSourcePaths.TryGetValue(mentionTitle, out string? mentionSourcePath) &&
                    !string.IsNullOrWhiteSpace(mentionSourcePath) &&
                    referenceableMaterialPaths.Contains(mentionSourcePath))
                {
                    expanded |= dictionaryMaterialPaths.Add(mentionSourcePath);
                }
            }

            if (!expanded)
            {
                return section;
            }
        }
    }

    /// <summary>
    /// Builds a grouped appendix section for the supplied material files.
    /// </summary>
    private async Task<ContentSection?> BuildContinuousMaterialsAppendixSectionAsync(
        string sourceRoot,
        string[] materialFiles,
        string title,
        string sectionId,
        string? entryAnchorPrefix,
        bool compactForPdf,
        CancellationToken cancellationToken)
    {
        if (materialFiles.Length == 0)
        {
            return null;
        }

        RenderingAppendixSection(title, materialFiles.Length);
        bool useCompactEntries = compactForPdf && materialFiles.Length >= CompactPdfAppendixEntryThreshold;
        StringBuilder html = new();
        html.Append("<div class=\"appendix-materials\">");

        bool hasEntries = false;
        foreach (IGrouping<string, string> group in materialFiles
                     .GroupBy(path => GetMaterialDirectoryLabel(sourceRoot, path))
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] groupEntries =
            [
                .. group
                    .Select(path => new { Path = path, Title = ResolveMaterialTitle(path, null) })
                    .OrderBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(static entry => entry.Path),
            ];

            if (groupEntries.Length == 0)
            {
                continue;
            }

            RenderingAppendixGroup(group.Key, groupEntries.Length);
            html.Append("<section class=\"materials-group\"><h3>")
                .Append(EscapeHtml(group.Key))
                .AppendLine("</h3><div class=\"materials-columns\">");

            for (int index = 0; index < groupEntries.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string materialPath = groupEntries[index];
                string entryHtml = await BuildMaterialEntryHtmlAsync(
                    materialPath,
                    sourceRoot,
                    entryAnchorPrefix,
                    sectionId,
                    useCompactEntries,
                    cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(entryHtml))
                {
                    continue;
                }

                hasEntries = true;
                html.Append(entryHtml);
                if (index < groupEntries.Length - 1)
                {
                    html.AppendLine("<hr class=\"material-divider\" />");
                }
            }

            html.AppendLine("</div></section>");
        }

        html.Append("</div>");
        if (!hasEntries)
        {
            return null;
        }

        return new(title, html.ToString(), sectionId, null);
    }

    /// <summary>
    /// Builds rendered HTML for a single material entry in an appendix section.
    /// </summary>
    private async Task<string> BuildMaterialEntryHtmlAsync(
        string materialPath,
        string sourceRoot,
        string? entryAnchorPrefix,
        string? mentionTargetId,
        bool compactEntry,
        CancellationToken cancellationToken)
    {
        string title = ResolveMaterialTitle(materialPath, null);
        string anchorId = string.IsNullOrWhiteSpace(entryAnchorPrefix)
            ? BuildMaterialAnchorId(sourceRoot, materialPath)
            : $"{entryAnchorPrefix}-{BuildMaterialAnchorId(sourceRoot, materialPath)}";

        if (compactEntry)
        {
            return $"<article id=\"{EscapeHtml(anchorId)}\" class=\"material-entry\"><h4>{EscapeHtml(title)}</h4></article>";
        }

        string bodyMarkdown = await RenderMaterialBodyMarkdownAsync(
            materialPath,
            sourceRoot,
            anchorId,
            title,
            title,
            cancellationToken).ConfigureAwait(false);
        string bodyHtml = RenderMarkdownToHtml(bodyMarkdown);
        string heroImageHtml = BuildMaterialHeroImageHtml(materialPath, sourceRoot);
        return $"<article id=\"{EscapeHtml(anchorId)}\" class=\"material-entry\">{heroImageHtml}<h4>{EscapeHtml(title)}</h4><div class=\"material-content\">{bodyHtml}</div></article>";
    }

    /// <summary>
    /// Loads referenced material file paths discovered across project markdown files.
    /// </summary>
    private async Task<HashSet<string>> LoadReferencedMaterialPathsAsync(string sourceRoot, bool includeMacros, CancellationToken cancellationToken)
    {
        HashSet<string> referencedPaths = new(StringComparer.OrdinalIgnoreCase);
        List<string> markdownFiles = [];

        string[] rootMarkdownFiles =
        [
            Path.Combine(sourceRoot, "README.md"),
            Path.Combine(sourceRoot, "TITLE.md"),
            Path.Combine(sourceRoot, "SOURCES.md"),
        ];

        foreach (string path in rootMarkdownFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                markdownFiles.Add(path);
            }
        }

        string contentRoot = Path.Combine(sourceRoot, "content");
        if (Directory.Exists(contentRoot))
        {
            markdownFiles.AddRange(Directory.GetFiles(contentRoot, "*.md", SearchOption.AllDirectories));
        }

        ScanningReferencedMaterialPaths(markdownFiles.Count);
        int scannedIndex = 0;
        foreach (string markdownFilePath in markdownFiles)
        {
            scannedIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            if (logger?.IsEnabled(LogLevel.Debug) ?? false)
            {
                string relativePath = Path.GetRelativePath(sourceRoot, markdownFilePath);
                ScanningReferencedMaterialPathFile(scannedIndex, markdownFiles.Count, relativePath);
            }

            string rawMarkdown = await File.ReadAllTextAsync(markdownFilePath, cancellationToken).ConfigureAwait(false);
            ParsedMarkdown parsed = ParseMarkdownDocument(rawMarkdown);
            string content = parsed.Body;

            foreach (Match includeMatch in IncludeRegex.Matches(content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string includePath = includeMatch.Groups["path"].Value.Trim();
                if (!TryParseIncludePath(includePath, out string includePathWithoutQuery, out _))
                {
                    continue;
                }

                if (!IsStructuredInclude(includePathWithoutQuery))
                {
                    continue;
                }

                string resolvedPath = Path.GetFullPath(ResolveReferencePath(markdownFilePath, includePathWithoutQuery));
                if (IsReferenceableMaterialPath(sourceRoot, resolvedPath))
                {
                    referencedPaths.Add(resolvedPath);
                }
            }

            if (!includeMacros)
            {
                continue;
            }

            foreach (Match macroMatch in MacroRegex.Matches(content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string referencePath = macroMatch.Groups["path"].Value.Trim();
                if (!IsStructuredInclude(referencePath))
                {
                    continue;
                }

                string resolvedPath = Path.GetFullPath(ResolveReferencePath(markdownFilePath, referencePath));
                if (IsReferenceableMaterialPath(sourceRoot, resolvedPath))
                {
                    referencedPaths.Add(resolvedPath);
                }
            }
        }

        return referencedPaths;
    }

    /// <summary>
    /// Loads referenced material paths and maps them to target anchor identifiers.
    /// </summary>
    private async Task<Dictionary<string, HashSet<string>>> LoadReferencedMaterialTargetIdsAsync(
        string sourceRoot,
        bool includeMacros,
        CancellationToken cancellationToken)
    {
        Dictionary<string, HashSet<string>> targets = new(StringComparer.OrdinalIgnoreCase);
        List<string> markdownFiles = [];

        string[] rootMarkdownFiles =
        [
            Path.Combine(sourceRoot, "README.md"),
            Path.Combine(sourceRoot, "TITLE.md"),
            Path.Combine(sourceRoot, "SOURCES.md"),
        ];

        foreach (string path in rootMarkdownFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                markdownFiles.Add(path);
            }
        }

        string contentRoot = Path.Combine(sourceRoot, "content");
        if (Directory.Exists(contentRoot))
        {
            markdownFiles.AddRange(Directory.GetFiles(contentRoot, "*.md", SearchOption.AllDirectories));
        }

        ScanningReferencedMaterialTargetPages(markdownFiles.Count);
        int scannedIndex = 0;
        foreach (string markdownFilePath in markdownFiles)
        {
            scannedIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            if (logger?.IsEnabled(LogLevel.Debug) ?? false)
            {
                string relativePath = Path.GetRelativePath(sourceRoot, markdownFilePath);
                ScanningReferencedMaterialTargetFile(scannedIndex, markdownFiles.Count, relativePath);
            }

            string? referenceTargetId = ResolveReferenceTargetId(sourceRoot, markdownFilePath);
            if (string.IsNullOrWhiteSpace(referenceTargetId))
            {
                continue;
            }

            string rawMarkdown = await File.ReadAllTextAsync(markdownFilePath, cancellationToken).ConfigureAwait(false);
            ParsedMarkdown parsed = ParseMarkdownDocument(rawMarkdown);
            string content = parsed.Body;

            foreach (Match includeMatch in IncludeRegex.Matches(content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string includePath = includeMatch.Groups["path"].Value.Trim();
                if (!TryParseIncludePath(includePath, out string includePathWithoutQuery, out _))
                {
                    continue;
                }

                if (!IsStructuredInclude(includePathWithoutQuery))
                {
                    continue;
                }

                string resolvedPath = Path.GetFullPath(ResolveReferencePath(markdownFilePath, includePathWithoutQuery));
                if (IsReferenceableMaterialPath(sourceRoot, resolvedPath))
                {
                    AddReferenceTarget(targets, resolvedPath, referenceTargetId);
                }
            }

            if (!includeMacros)
            {
                continue;
            }

            foreach (Match macroMatch in MacroRegex.Matches(content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string referencePath = macroMatch.Groups["path"].Value.Trim();
                if (!IsStructuredInclude(referencePath))
                {
                    continue;
                }

                string resolvedPath = Path.GetFullPath(ResolveReferencePath(markdownFilePath, referencePath));
                if (IsReferenceableMaterialPath(sourceRoot, resolvedPath))
                {
                    AddReferenceTarget(targets, resolvedPath, referenceTargetId);
                }
            }
        }

        return targets;
    }

    /// <summary>
    /// Resolves the target anchor identifier associated with a markdown page.
    /// </summary>
    private static string? ResolveReferenceTargetId(string sourceRoot, string markdownFilePath)
    {
        string relativePath = Path.GetRelativePath(sourceRoot, markdownFilePath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string fileName = Path.GetFileName(relativePath);
        if (relativePath.StartsWith($"content{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSectionId(relativePath);
        }

        if (string.Equals(fileName, "README.md", StringComparison.OrdinalIgnoreCase))
        {
            return "foreword";
        }

        if (string.Equals(fileName, "SOURCES.md", StringComparison.OrdinalIgnoreCase))
        {
            return "bibliography";
        }

        if (string.Equals(fileName, "TITLE.md", StringComparison.OrdinalIgnoreCase))
        {
            return "cover";
        }

        return null;
    }

    /// <summary>
    /// Adds a target identifier mapping for a material path.
    /// </summary>
    private static void AddReferenceTarget(Dictionary<string, HashSet<string>> targets, string materialPath, string targetId)
    {
        if (!targets.TryGetValue(materialPath, out HashSet<string>? targetSet))
        {
            targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            targets[materialPath] = targetSet;
        }

        targetSet.Add(targetId);
    }

    /// <summary>
    /// Loads and caches all material paths that can participate in references.
    /// </summary>
    private HashSet<string> LoadReferenceableMaterialPaths(string sourceRoot)
    {
        string normalizedRoot = Path.GetFullPath(sourceRoot);
        if (_referenceableMaterialPathsCache.TryGetValue(normalizedRoot, out HashSet<string>? cachedPaths))
        {
            return cachedPaths;
        }

        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.GetFiles(normalizedRoot, "*.*", SearchOption.AllDirectories))
        {
            string normalizedPath = Path.GetFullPath(filePath);
            if (IsReferenceableMaterialPath(normalizedRoot, normalizedPath))
            {
                result.Add(normalizedPath);
            }
        }

        _referenceableMaterialPathsCache[normalizedRoot] = result;
        return result;
    }

    /// <summary>
    /// Loads and caches candidate entries for reference dictionary mention linking.
    /// </summary>
    private List<ReferenceDictionaryMentionCandidate> LoadReferenceDictionaryMentionCandidates(string sourceRoot)
    {
        string normalizedRoot = Path.GetFullPath(sourceRoot);
        if (_referenceDictionaryMentionCandidatesCache.TryGetValue(normalizedRoot, out List<ReferenceDictionaryMentionCandidate>? cachedCandidates))
        {
            PreviewReferenceDictionaryCandidateCacheHit(normalizedRoot, cachedCandidates.Count);
            return cachedCandidates;
        }

        var cacheTimer = Stopwatch.StartNew();
        List<ReferenceDictionaryMentionCandidate> candidates = [];
        foreach (string materialPath in LoadReferenceableMaterialPaths(normalizedRoot))
        {
            string title = ResolveMaterialTitle(materialPath, null);
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3)
            {
                continue;
            }

            string targetId = BuildReferenceDictionaryMaterialAnchorId(normalizedRoot, materialPath);
            candidates.Add(new ReferenceDictionaryMentionCandidate(title, materialPath, targetId));
        }

        cacheTimer.Stop();
        _referenceDictionaryMentionCandidatesCache[normalizedRoot] = candidates;
        PreviewReferenceDictionaryCandidateCacheBuilt(normalizedRoot, candidates.Count, cacheTimer.ElapsedMilliseconds);
        return candidates;
    }

    /// <summary>
    /// Loads referenceable snippet file paths from the snippets directory.
    /// </summary>
    private static HashSet<string> LoadReferenceableSnippetPaths(string sourceRoot)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        string snippetsRoot = Path.Combine(sourceRoot, "snippets");
        if (!Directory.Exists(snippetsRoot))
        {
            return result;
        }

        foreach (string filePath in Directory.GetFiles(snippetsRoot, "*.*", SearchOption.AllDirectories))
        {
            string normalizedPath = Path.GetFullPath(filePath);
            if (IsReferenceableMaterialPath(sourceRoot, normalizedPath))
            {
                result.Add(normalizedPath);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves configured shadow references to material file paths.
    /// </summary>
    private HashSet<string> LoadShadowReferenceMaterialPaths(string sourceRoot, IReadOnlyCollection<string> shadowReferences)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        if (shadowReferences.Count == 0)
        {
            return result;
        }

        HashSet<string> requested = new(
            shadowReferences
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0)
        {
            return result;
        }

        HashSet<string> referenceablePaths = LoadReferenceableMaterialPaths(sourceRoot);
        ResolvingShadowReferences(requested.Count, referenceablePaths.Count);
        foreach (string path in referenceablePaths)
        {
            string title = ResolveMaterialTitle(path, null);
            string stem = Path.GetFileNameWithoutExtension(path);
            string normalizedStem = NormalizeDashedToken(stem);
            if (requested.Contains(title) || requested.Contains(stem) || requested.Contains(normalizedStem))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>
    /// Determines whether a file path points to a referenceable material source.
    /// </summary>
    private static bool IsReferenceableMaterialPath(string sourceRoot, string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(Path.GetFileName(path), "TEMPLATE.md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relativePath = Path.GetRelativePath(sourceRoot, path);
        if (!relativePath.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relativePath.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        string normalizedRelativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (IsPreviewCacheIgnoredRelativePath(normalizedRelativePath))
        {
            return false;
        }

        if (normalizedRelativePath.StartsWith($"settings{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelativePath.StartsWith($"content{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the display label for the directory containing a material file.
    /// </summary>
    private static string GetMaterialDirectoryLabel(string sourceRoot, string path)
    {
        string relativePath = Path.GetRelativePath(sourceRoot, path);
        string? relativeDirectory = Path.GetDirectoryName(relativePath);
        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            return "materials";
        }

        return relativeDirectory.Replace('\\', '/');
    }

    /// <summary>
    /// Loads and renders the cover markdown file into HTML.
    /// </summary>
    private async Task<string?> LoadCoverHtmlAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        string titlePath = Path.Combine(sourceRoot, "TITLE.md");
        if (!File.Exists(titlePath))
        {
            return null;
        }

        string raw = await File.ReadAllTextAsync(titlePath, cancellationToken).ConfigureAwait(false);
        ParsedMarkdown parsed = ParseMarkdownDocument(raw);
        string processed = ProcessInlineTokens(parsed.Body, titlePath, sourceRoot, currentPageTitleOverride: "Cover", contentPageTitleOverride: "Cover");
        return RenderMarkdownToHtml(processed);
    }

    /// <summary>
    /// Loads project metadata from front matter, settings, and companion markdown files.
    /// </summary>
    private static async Task<ProjectMetadata> LoadProjectMetadataAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        Dictionary<string, string> titleFrontMatter = await LoadFrontMatterFromFileAsync(Path.Combine(sourceRoot, "TITLE.md"), cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> authorsFrontMatter = await LoadFrontMatterFromFileAsync(Path.Combine(sourceRoot, "AUTHORS.md"), cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> licenseFrontMatter = await LoadFrontMatterFromFileAsync(Path.Combine(sourceRoot, "LICENSE.md"), cancellationToken).ConfigureAwait(false);

        Dictionary<string, string> globalSettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "global.yml"));
        Dictionary<string, string> htmlSettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "html.yml"));
        Dictionary<string, string> pdfSettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "pdf.yml"));

        string? authorsBody = await LoadBodyTextAsync(Path.Combine(sourceRoot, "AUTHORS.md"), cancellationToken).ConfigureAwait(false);
        string? licenseBody = await LoadBodyTextAsync(Path.Combine(sourceRoot, "LICENSE.md"), cancellationToken).ConfigureAwait(false);

        string title = FirstNonEmpty(
            GetValue(titleFrontMatter, "title"),
            GetValue(pdfSettings, ProjectTitleSettingKey),
            GetValue(htmlSettings, ProjectTitleSettingKey),
            GetValue(globalSettings, ProjectTitleSettingKey),
            InferProjectTitle(sourceRoot));

        string? author = FirstNonEmptyOrNull(
            GetValue(titleFrontMatter, "author"),
            GetValue(titleFrontMatter, "authors"),
            GetValue(authorsFrontMatter, "author"),
            GetValue(authorsFrontMatter, "authors"),
            GetValue(pdfSettings, ProjectAuthorSettingKey),
            GetValue(pdfSettings, ProjectAuthorsSettingKey),
            GetValue(htmlSettings, ProjectAuthorSettingKey),
            GetValue(htmlSettings, ProjectAuthorsSettingKey),
            GetValue(globalSettings, ProjectAuthorSettingKey),
            GetValue(globalSettings, ProjectAuthorsSettingKey),
            authorsBody);

        string? description = FirstNonEmptyOrNull(
            GetValue(titleFrontMatter, "description"),
            GetValue(authorsFrontMatter, "description"),
            GetValue(pdfSettings, ProjectDescriptionSettingKey),
            GetValue(htmlSettings, ProjectDescriptionSettingKey),
            GetValue(globalSettings, ProjectDescriptionSettingKey));

        string? copyright = FirstNonEmptyOrNull(
            GetValue(licenseFrontMatter, "copyright"),
            GetValue(authorsFrontMatter, "copyright"),
            GetValue(pdfSettings, ProjectCopyrightSettingKey),
            GetValue(htmlSettings, ProjectCopyrightSettingKey),
            GetValue(globalSettings, ProjectCopyrightSettingKey));

        string? license = FirstNonEmptyOrNull(
            GetValue(licenseFrontMatter, "license"),
            GetValue(authorsFrontMatter, "license"),
            GetValue(pdfSettings, ProjectLicenseSettingKey),
            GetValue(htmlSettings, ProjectLicenseSettingKey),
            GetValue(globalSettings, ProjectLicenseSettingKey),
            licenseBody);

        string? coverJumbotron = FirstNonEmptyOrNull(GetValue(titleFrontMatter, "jumbotron"), GetValue(globalSettings, ProjectJumbotronSettingKey));
        return new ProjectMetadata(title, author, description, copyright, license, coverJumbotron);
    }
}
