using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;

namespace Grimoire.Core;

/// <summary>
/// Provides catalog and advanced search operations for project content files.
/// </summary>
public sealed partial class ProjectSearchService(ILogger<ProjectSearchService>? inputLogger = null)
{
    /// <summary>
    /// Stores the YAML deserializer used to parse markdown front matter.
    /// </summary>
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    /// <summary>
    /// Stores the logger used for diagnostic search events.
    /// </summary>
    private readonly ILogger<ProjectSearchService> logger = inputLogger ?? NullLogger<ProjectSearchService>.Instance;

    /// <summary>
    /// Searches the project catalog using the provided request and default logging.
    /// </summary>
    /// <param name="request">The search request to execute.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to catalog search entries.</returns>
    public static Task<IReadOnlyList<ProjectSearchEntry>> SearchAsync(ProjectSearchRequest request, CancellationToken cancellationToken)
    {
        return SearchAsync(request, logger: null, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Searches the project catalog using the provided request.
    /// </summary>
    /// <param name="request">The search request to execute.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to catalog search entries.</returns>
    public static async Task<IReadOnlyList<ProjectSearchEntry>> SearchAsync(
        ProjectSearchRequest request,
        ILogger<ProjectSearchService>? logger,
        CancellationToken cancellationToken)
    {
        ProjectSearchService service = new(logger);
        return await service.SearchCatalogAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes catalog search across candidate files.
    /// </summary>
    /// <param name="request">The catalog search request.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to sorted catalog entries.</returns>
    private async Task<IReadOnlyList<ProjectSearchEntry>> SearchCatalogAsync(ProjectSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string inputPath = string.IsNullOrWhiteSpace(request.InputPath) ? "." : request.InputPath;
        string rootPath = Path.GetFullPath(inputPath);
        if (!Directory.Exists(rootPath))
        {
            throw new ArgumentException($"Search directory does not exist: {rootPath}", nameof(request));
        }

        CatalogSearchStarted(rootPath);
        string query = request.Query?.Trim() ?? string.Empty;
        BoyerMoorePattern? queryPattern = string.IsNullOrWhiteSpace(query) ? null : BuildBoyerMoorePattern(query);
        HashSet<string>? filter = BuildFilter(request.SubdirectoryFilters);
        List<ProjectSearchEntry> entries = [];
        Dictionary<string, string?> templateFormatCache = new(StringComparer.OrdinalIgnoreCase);
        string[] allFiles = [.. EnumerateCandidateFiles(rootPath)];
        Dictionary<string, ImmutableArray<string>> includedByMap = await BuildIncludedByMapAsync(rootPath, allFiles, cancellationToken).ConfigureAwait(false);

        int catalogFileIndex = 0;
        foreach (string filePath in allFiles)
        {
            catalogFileIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizePath(Path.GetRelativePath(rootPath, filePath));
            ProcessingSearchFile("catalog", catalogFileIndex, allFiles.Length, relativePath);
            string? topLevelDirectory = GetTopLevelDirectory(relativePath);
            if (filter is not null && (string.IsNullOrWhiteSpace(topLevelDirectory) || !filter.Contains(topLevelDirectory)))
            {
                continue;
            }

            JsonElement root = await LoadEntityRootAsync(filePath, cancellationToken).ConfigureAwait(false);
            string name = ResolveDisplayName(root, filePath);
            string directory = Path.GetDirectoryName(filePath) ?? rootPath;

            if (!templateFormatCache.TryGetValue(directory, out string? cliFormat))
            {
                cliFormat = await LoadCliFormatAsync(rootPath, directory, cancellationToken).ConfigureAwait(false);
                templateFormatCache[directory] = cliFormat;
            }

            string details = string.IsNullOrWhiteSpace(cliFormat)
                ? RenderPropertyTable(root)
                : RenderCliFormat(cliFormat, root);
            ImmutableArray<string> includedBy = GetIncludedBy(includedByMap, relativePath);
            if (queryPattern is not null &&
                !CatalogEntryMatchesQuery(name, relativePath, details, includedBy, queryPattern))
            {
                continue;
            }

            entries.Add(new(name, relativePath, details, includedBy));
        }

        entries.Sort(static (left, right) =>
        {
            int byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
        });

        CatalogSearchCompleted(entries.Count);
        return entries;
    }

    /// <summary>
    /// Runs an advanced search using the provided request and default logging.
    /// </summary>
    /// <param name="request">The advanced search request.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to advanced search matches.</returns>
    public static Task<IReadOnlyList<ProjectSearchMatch>> SearchAdvancedAsync(ProjectSearchAdvancedRequest request, CancellationToken cancellationToken)
    {
        return SearchAdvancedAsync(request, logger: null, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs an advanced search using the provided request.
    /// </summary>
    /// <param name="request">The advanced search request.</param>
    /// <param name="logger">The logger used for diagnostic output.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to advanced search matches.</returns>
    public static async Task<IReadOnlyList<ProjectSearchMatch>> SearchAdvancedAsync(
        ProjectSearchAdvancedRequest request,
        ILogger<ProjectSearchService>? logger,
        CancellationToken cancellationToken)
    {
        ProjectSearchService service = new(logger);
        return await service.SearchAdvancedCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the mode-specific advanced search pipeline.
    /// </summary>
    /// <param name="request">The advanced search request.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to advanced search matches.</returns>
    private async Task<IReadOnlyList<ProjectSearchMatch>> SearchAdvancedCoreAsync(ProjectSearchAdvancedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string inputPath = string.IsNullOrWhiteSpace(request.InputPath) ? "." : request.InputPath;
        string rootPath = Path.GetFullPath(inputPath);
        if (!Directory.Exists(rootPath))
        {
            throw new ArgumentException($"Search directory does not exist: {rootPath}", nameof(request));
        }

        AdvancedSearchStarted(request.Mode, rootPath, request.Limit);
        HashSet<string>? filter = BuildFilter(request.SubdirectoryFilters);
        string[] allFiles = [.. EnumerateCandidateFiles(rootPath)];
        Dictionary<string, ImmutableArray<string>> includedByMap = await BuildIncludedByMapAsync(rootPath, allFiles, cancellationToken).ConfigureAwait(false);
        string[] files =
        [
            .. allFiles
            .Where(file =>
            {
                if (filter is null)
                {
                    return true;
                }

                string relativePath = NormalizePath(Path.GetRelativePath(rootPath, file));
                string? topLevelDirectory = GetTopLevelDirectory(relativePath);
                return !string.IsNullOrWhiteSpace(topLevelDirectory) && filter.Contains(topLevelDirectory);
            }),
        ];

        int limit = request.Limit <= 0 ? 200 : Math.Min(request.Limit, 5000);
        List<ProjectSearchMatch> matches = [];
        switch (request.Mode)
        {
            case ProjectSearchMode.FullText:
                await SearchFullTextAsync(rootPath, files, request, limit, matches, includedByMap, cancellationToken).ConfigureAwait(false);
                break;
            case ProjectSearchMode.Property:
                await SearchPropertiesAsync(rootPath, files, request, limit, matches, includedByMap, cancellationToken).ConfigureAwait(false);
                break;
            case ProjectSearchMode.KeywordUsage:
                await SearchKeywordUsageAsync(rootPath, files, request, limit, matches, includedByMap, cancellationToken).ConfigureAwait(false);
                break;
            case ProjectSearchMode.CrossReference:
                await SearchCrossReferencesAsync(rootPath, files, request, limit, matches, includedByMap, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), $"Unsupported search mode: {request.Mode}");
        }

        AdvancedSearchCompleted(request.Mode, matches.Count);
        return matches;
    }

    /// <summary>
    /// Performs full-text matching for the requested query across candidate files.
    /// </summary>
    /// <param name="rootPath">The absolute root path used for relative path resolution.</param>
    /// <param name="files">The candidate files to scan.</param>
    /// <param name="request">The active advanced search request.</param>
    /// <param name="limit">The maximum number of matches to collect.</param>
    /// <param name="matches">The destination list of collected matches.</param>
    /// <param name="includedByMap">The map of nested paths and includer pages.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A completed task.</returns>
    private Task SearchFullTextAsync(
        string rootPath,
        string[] files,
        ProjectSearchAdvancedRequest request,
        int limit,
        List<ProjectSearchMatch> matches,
        IReadOnlyDictionary<string, ImmutableArray<string>> includedByMap,
        CancellationToken cancellationToken)
    {
        string query = request.Query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Full-text search requires a non-empty query.", nameof(request));
        }

        BoyerMoorePattern pattern = BuildBoyerMoorePattern(query);
        int fullTextFileIndex = 0;
        foreach (string filePath in files)
        {
            fullTextFileIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Count >= limit)
            {
                break;
            }

            string relativePath = NormalizePath(Path.GetRelativePath(rootPath, filePath));
            ProcessingSearchFile("full-text", fullTextFileIndex, files.Length, relativePath);
            ScanFileForPatternMatches(
                filePath,
                pattern,
                (window, matchIndex, lineNumber) =>
                {
                    if (matches.Count >= limit)
                    {
                        return false;
                    }

                    matches.Add(new(
                        relativePath,
                        ResolveFileKind(filePath),
                        "fullText",
                        lineNumber,
                        null,
                        null,
                        Truncate(ExtractMatchSnippet(window, matchIndex, pattern.Needle.Length), 320),
                        null,
                        null,
                        GetIncludedBy(includedByMap, relativePath)));
                    return matches.Count < limit;
                },
                cancellationToken);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs scalar-property matching across candidate entity files.
    /// </summary>
    /// <param name="rootPath">The absolute root path used for relative path resolution.</param>
    /// <param name="files">The candidate files to scan.</param>
    /// <param name="request">The active advanced search request.</param>
    /// <param name="limit">The maximum number of matches to collect.</param>
    /// <param name="matches">The destination list of collected matches.</param>
    /// <param name="includedByMap">The map of nested paths and includer pages.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that completes when scanning finishes.</returns>
    private async Task SearchPropertiesAsync(
        string rootPath,
        string[] files,
        ProjectSearchAdvancedRequest request,
        int limit,
        List<ProjectSearchMatch> matches,
        IReadOnlyDictionary<string, ImmutableArray<string>> includedByMap,
        CancellationToken cancellationToken)
    {
        string query = request.Query?.Trim() ?? string.Empty;
        string propertyFilter = request.PropertyPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(propertyFilter))
        {
            throw new ArgumentException("Property search requires query and/or property path filter.", nameof(request));
        }

        BoyerMoorePattern? queryPattern = string.IsNullOrWhiteSpace(query) ? null : BuildBoyerMoorePattern(query);
        int propertyFileIndex = 0;
        foreach (string filePath in files)
        {
            propertyFileIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Count >= limit)
            {
                break;
            }

            JsonElement root = await LoadEntityRootAsync(filePath, cancellationToken).ConfigureAwait(false);
            string relativePath = NormalizePath(Path.GetRelativePath(rootPath, filePath));
            ProcessingSearchFile("property", propertyFileIndex, files.Length, relativePath);
            foreach ((string propertyPath, string value) in EnumerateScalarProperties(root, prefix: null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matches.Count >= limit)
                {
                    break;
                }

                bool pathMatch = string.IsNullOrWhiteSpace(propertyFilter) || propertyPath.Contains(propertyFilter, StringComparison.OrdinalIgnoreCase);
                bool valueMatch = queryPattern is null ||
                                  BoyerMooreContains(value, queryPattern) ||
                                  BoyerMooreContains(propertyPath, queryPattern);
                if (!pathMatch || !valueMatch)
                {
                    continue;
                }

                matches.Add(new(
                    relativePath,
                    ResolveFileKind(filePath),
                    "property",
                    null,
                    propertyPath,
                    null,
                    Truncate($"{propertyPath}: {value}", 320),
                    null,
                    null,
                    GetIncludedBy(includedByMap, relativePath)));
            }
        }
    }

    /// <summary>
    /// Finds file locations where entities matching the query are referenced by name.
    /// </summary>
    /// <param name="rootPath">The absolute root path used for relative path resolution.</param>
    /// <param name="files">The candidate files to scan.</param>
    /// <param name="request">The active advanced search request.</param>
    /// <param name="limit">The maximum number of matches to collect.</param>
    /// <param name="matches">The destination list of collected matches.</param>
    /// <param name="includedByMap">The map of nested paths and includer pages.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that completes when scanning finishes.</returns>
    private async Task SearchKeywordUsageAsync(
        string rootPath,
        string[] files,
        ProjectSearchAdvancedRequest request,
        int limit,
        List<ProjectSearchMatch> matches,
        IReadOnlyDictionary<string, ImmutableArray<string>> includedByMap,
        CancellationToken cancellationToken)
    {
        string query = request.Query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Keyword usage search requires a non-empty query.", nameof(request));
        }

        BoyerMoorePattern queryPattern = BuildBoyerMoorePattern(query);
        List<(string Name, string RelativePath, BoyerMoorePattern Pattern)> entities = [];
        int keywordEntityScanIndex = 0;
        foreach (string filePath in files)
        {
            keywordEntityScanIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            JsonElement root = await LoadEntityRootAsync(filePath, cancellationToken).ConfigureAwait(false);
            string name = ResolveDisplayName(root, filePath);
            string entityScanRelativePath = NormalizePath(Path.GetRelativePath(rootPath, filePath));
            ProcessingSearchFile("keyword-usage-entity-scan", keywordEntityScanIndex, files.Length, entityScanRelativePath);
            if (!BoyerMooreContains(name, queryPattern))
            {
                continue;
            }

            entities.Add((name, NormalizePath(Path.GetRelativePath(rootPath, filePath)), BuildBoyerMoorePattern(name)));
        }

        if (entities.Count == 0)
        {
            return;
        }

        int keywordUsageScanIndex = 0;
        foreach (string filePath in files)
        {
            keywordUsageScanIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Count >= limit)
            {
                break;
            }

            string relativePath = NormalizePath(Path.GetRelativePath(rootPath, filePath));
            ProcessingSearchFile("keyword-usage", keywordUsageScanIndex, files.Length, relativePath);
            foreach ((string entityName, string entityPath, BoyerMoorePattern entityPattern) in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (matches.Count >= limit)
                {
                    break;
                }

                ScanFileForPatternMatches(
                    filePath,
                    entityPattern,
                    (window, matchIndex, lineNumber) =>
                    {
                        if (matches.Count >= limit)
                        {
                            return false;
                        }

                        matches.Add(new(
                            relativePath,
                            ResolveFileKind(filePath),
                            "keywordUsage",
                            lineNumber,
                            null,
                            null,
                            Truncate(ExtractMatchSnippet(window, matchIndex, entityPattern.Needle.Length), 320),
                            entityName,
                            entityPath,
                            GetIncludedBy(includedByMap, relativePath)));
                        return matches.Count < limit;
                    },
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Searches markdown and JSON files for include, macro, and mention cross references.
    /// </summary>
    /// <param name="rootPath">The absolute root path used for relative path resolution.</param>
    /// <param name="files">The candidate files to scan.</param>
    /// <param name="request">The active advanced search request.</param>
    /// <param name="limit">The maximum number of matches to collect.</param>
    /// <param name="matches">The destination list of collected matches.</param>
    /// <param name="includedByMap">The map of nested paths and includer pages.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that completes when scanning finishes.</returns>
    private async Task SearchCrossReferencesAsync(
        string rootPath,
        string[] files,
        ProjectSearchAdvancedRequest request,
        int limit,
        List<ProjectSearchMatch> matches,
        IReadOnlyDictionary<string, ImmutableArray<string>> includedByMap,
        CancellationToken cancellationToken)
    {
        string query = request.Query?.Trim() ?? string.Empty;
        BoyerMoorePattern? queryPattern = string.IsNullOrWhiteSpace(query) ? null : BuildBoyerMoorePattern(query);
        List<(string Name, string RelativePath, BoyerMoorePattern Pattern)> mentionEntities = [];
        if (queryPattern is not null)
        {
            foreach (string filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonElement root = await LoadEntityRootAsync(filePath, cancellationToken).ConfigureAwait(false);
                string entityName = ResolveDisplayName(root, filePath);
                if (!BoyerMooreContains(entityName, queryPattern))
                {
                    continue;
                }

                mentionEntities.Add((entityName, NormalizePath(Path.GetRelativePath(rootPath, filePath)), BuildBoyerMoorePattern(entityName)));
            }
        }

        int crossReferenceFileIndex = 0;
        foreach (string filePath in files)
        {
            crossReferenceFileIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Count >= limit)
            {
                break;
            }

            string extension = Path.GetExtension(filePath);
            if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string sourceRelativePath = NormalizePath(Path.GetRelativePath(rootPath, filePath));
            ProcessingSearchFile("cross-reference", crossReferenceFileIndex, files.Length, sourceRelativePath);
            ScanFileLines(
                filePath,
                (line, lineNumber) =>
                {
                    string lineText = line.ToString();
                    if (matches.Count >= limit)
                    {
                        return false;
                    }

                    foreach (ProjectReferenceEdge edge in EnumerateCrossReferenceEdgesForLine(rootPath, filePath, sourceRelativePath, line, lineNumber))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (queryPattern is not null &&
                            !BoyerMooreContains(edge.SourcePath, queryPattern) &&
                            !BoyerMooreContains(edge.TargetPath, queryPattern) &&
                            !BoyerMooreContains(lineText, queryPattern))
                        {
                            continue;
                        }

                        matches.Add(new(
                            edge.SourcePath,
                            ResolveFileKind(filePath),
                            edge.ReferenceType,
                            edge.LineNumber,
                            null,
                            edge.TargetPath,
                            Truncate($"{edge.SourcePath} -> {edge.TargetPath}", 320),
                            null,
                            null,
                            GetIncludedBy(includedByMap, edge.SourcePath)));
                        if (matches.Count >= limit)
                        {
                            return false;
                        }
                    }

                    foreach ((string entityName, string entityPath, BoyerMoorePattern entityPattern) in mentionEntities)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (matches.Count >= limit)
                        {
                            return false;
                        }

                        if (string.Equals(entityPath, sourceRelativePath, StringComparison.OrdinalIgnoreCase) ||
                            !BoyerMooreContains(lineText, entityPattern))
                        {
                            continue;
                        }

                        matches.Add(new(
                            sourceRelativePath,
                            ResolveFileKind(filePath),
                            "mention",
                            lineNumber,
                            null,
                            entityPath,
                            Truncate($"{sourceRelativePath} -> {entityPath}", 320),
                            entityName,
                            entityPath,
                            GetIncludedBy(includedByMap, sourceRelativePath)));
                    }

                    return true;
                },
                cancellationToken);
        }
    }

    /// <summary>
    /// Builds a map of nested content files to the top-level pages that include them.
    /// </summary>
    /// <param name="rootPath">The absolute content root path.</param>
    /// <param name="files">The files available for edge analysis.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that resolves to the included-by map.</returns>
    private static Task<Dictionary<string, ImmutableArray<string>>> BuildIncludedByMapAsync(string rootPath, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        HashSet<string> contentPages = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> outgoingEdges = new(StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizePath(Path.GetRelativePath(rootPath, filePath));
            if (!IsContentDocument(relativePath))
            {
                continue;
            }

            if (IsTopLevelContentPage(relativePath))
            {
                contentPages.Add(relativePath);
            }

            List<string> targets = [];
            ScanFileLines(
                filePath,
                (line, lineNumber) =>
                {
                    foreach (ProjectReferenceEdge edge in EnumerateCrossReferenceEdgesForLine(rootPath, filePath, relativePath, line, lineNumber))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        targets.Add(edge.TargetPath);
                    }

                    return true;
                },
                cancellationToken);

            if (targets.Count > 0)
            {
                outgoingEdges[relativePath] = [.. targets.Distinct(StringComparer.OrdinalIgnoreCase)];
            }
        }

        Dictionary<string, HashSet<string>> includedBy = new(StringComparer.OrdinalIgnoreCase);
        foreach (string contentPage in contentPages)
        {
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { contentPage };
            Stack<string> stack = new();
            if (outgoingEdges.TryGetValue(contentPage, out List<string>? initialTargets))
            {
                foreach (string target in initialTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stack.Push(target);
                }
            }

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (IsNestedContentMarkdown(current))
                {
                    if (!includedBy.TryGetValue(current, out HashSet<string>? includers))
                    {
                        includers = new(StringComparer.OrdinalIgnoreCase);
                        includedBy[current] = includers;
                    }

                    includers.Add(contentPage);
                }

                if (!outgoingEdges.TryGetValue(current, out List<string>? nextTargets)) continue;
                foreach (string target in nextTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stack.Push(target);
                }
            }
        }

        var result = includedBy.ToDictionary(
            static pair => pair.Key,
            static pair => (ImmutableArray<string>)[.. pair.Value.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)],
            StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Determines whether a relative path points to a searchable content document.
    /// </summary>
    /// <param name="relativePath">The relative path to evaluate.</param>
    /// <returns><see langword="true"/> when the path points to a content markdown or JSON file; otherwise, <see langword="false"/>.</returns>
    private static bool IsContentDocument(string relativePath)
    {
        if (!relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether a relative path points to a top-level content markdown page.
    /// </summary>
    /// <param name="relativePath">The relative path to evaluate.</param>
    /// <returns><see langword="true"/> when the path points to a top-level markdown page; otherwise, <see langword="false"/>.</returns>
    private static bool IsTopLevelContentPage(string relativePath)
    {
        if (!relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase) ||
            !relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = relativePath["content/".Length..];
        return !remainder.Contains('/', StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether a relative path points to nested markdown content.
    /// </summary>
    /// <param name="relativePath">The relative path to evaluate.</param>
    /// <returns><see langword="true"/> when the path points to nested markdown content; otherwise, <see langword="false"/>.</returns>
    private static bool IsNestedContentMarkdown(string relativePath)
    {
        if (!relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase) ||
            !relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = relativePath["content/".Length..];
        return remainder.Contains('/', StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets includer pages for a relative content path.
    /// </summary>
    /// <param name="includedByMap">The map containing include relationships.</param>
    /// <param name="relativePath">The relative path to resolve.</param>
    /// <returns>An immutable array of includer paths.</returns>
    private static ImmutableArray<string> GetIncludedBy(IReadOnlyDictionary<string, ImmutableArray<string>> includedByMap, string relativePath)
    {
        return includedByMap.TryGetValue(relativePath, out ImmutableArray<string> includedBy) ? includedBy : [];
    }

    /// <summary>
    /// Builds a Boyer-Moore pattern for case-insensitive text matching.
    /// </summary>
    /// <param name="query">The query text to compile.</param>
    /// <returns>A compiled Boyer-Moore pattern.</returns>
    private static BoyerMoorePattern BuildBoyerMoorePattern(string query)
    {
        string needle = query.ToUpperInvariant();
        int length = needle.Length;
        int[] shifts = new int[char.MaxValue + 1];
        Array.Fill(shifts, length);
        for (int index = 0; index < length - 1; index++)
        {
            shifts[needle[index]] = length - 1 - index;
        }

        return new(needle, shifts);
    }

    /// <summary>
    /// Determines whether text contains the compiled pattern.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="pattern">The compiled Boyer-Moore pattern.</param>
    /// <returns><see langword="true"/> when a match is found; otherwise, <see langword="false"/>.</returns>
    private static bool BoyerMooreContains(string text, BoyerMoorePattern pattern)
    {
        return BoyerMooreContains(text.AsSpan(), pattern);
    }

    /// <summary>
    /// Determines whether span text contains the compiled pattern.
    /// </summary>
    /// <param name="text">The text span to search.</param>
    /// <param name="pattern">The compiled Boyer-Moore pattern.</param>
    /// <returns><see langword="true"/> when a match is found; otherwise, <see langword="false"/>.</returns>
    private static bool BoyerMooreContains(ReadOnlySpan<char> text, BoyerMoorePattern pattern)
    {
        return BoyerMooreIndexOf(text, pattern) >= 0;
    }

    /// <summary>
    /// Finds the first index of the compiled pattern in span text.
    /// </summary>
    /// <param name="text">The text span to search.</param>
    /// <param name="pattern">The compiled Boyer-Moore pattern.</param>
    /// <param name="startIndex">The starting offset in <paramref name="text"/>.</param>
    /// <returns>The zero-based match index, or <c>-1</c> when no match exists.</returns>
    private static int BoyerMooreIndexOf(ReadOnlySpan<char> text, BoyerMoorePattern pattern, int startIndex = 0)
    {
        if (text.IsEmpty)
        {
            return -1;
        }

        string needle = pattern.Needle;
        int needleLength = needle.Length;
        int haystackLength = text.Length;
        if (needleLength == 0)
        {
            return 0;
        }

        if (needleLength > haystackLength)
        {
            return -1;
        }

        int offset = Math.Max(0, startIndex);
        while (offset <= haystackLength - needleLength)
        {
            int needleIndex = needleLength - 1;
            while (needleIndex >= 0 && needle[needleIndex] == char.ToUpperInvariant(text[offset + needleIndex]))
            {
                needleIndex--;
            }

            if (needleIndex < 0)
            {
                return offset;
            }

            offset += pattern.Shifts[char.ToUpperInvariant(text[offset + needleLength - 1])];
        }

        return -1;
    }

    /// <summary>
    /// Enumerates scalar JSON property values as path-value pairs.
    /// </summary>
    /// <param name="element">The root JSON element to traverse.</param>
    /// <param name="prefix">The current key-path prefix.</param>
    /// <returns>An enumerable sequence of scalar property path-value pairs.</returns>
    private static IEnumerable<(string Path, string Value)> EnumerateScalarProperties(JsonElement element, string? prefix)
    {
        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string childPath = string.IsNullOrWhiteSpace(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    foreach ((string nestedPath, string nestedValue) in EnumerateScalarProperties(property.Value, childPath))
                    {
                        yield return (nestedPath, nestedValue);
                    }
                }

                yield break;
            }
            case JsonValueKind.Array:
            {
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string childPath = $"{prefix}[{index}]";
                    foreach ((string nestedPath, string nestedValue) in EnumerateScalarProperties(item, childPath))
                    {
                        yield return (nestedPath, nestedValue);
                    }

                    index++;
                }

                yield break;
            }
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            yield break;
        }

        string value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(value))
        {
            yield return (prefix, value);
        }
    }

    /// <summary>
    /// Scans a file for Boyer-Moore pattern matches and invokes a callback per match.
    /// </summary>
    /// <param name="filePath">The file path to scan.</param>
    /// <param name="pattern">The compiled pattern to match.</param>
    /// <param name="visitor">The callback that receives each match.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    private static void ScanFileForPatternMatches(string filePath, BoyerMoorePattern pattern, PatternMatchVisitor visitor, CancellationToken cancellationToken)
    {
        const int BufferSize = 32768;
        int overlapLength = Math.Max(0, pattern.Needle.Length - 1);
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: BufferSize);
        char[] buffer = ArrayPool<char>.Shared.Rent(BufferSize + overlapLength + 1);
        int carryLength = 0;
        int lineNumberAtWindowStart = 1;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int charsRead = reader.Read(buffer, carryLength, BufferSize);
                if (charsRead <= 0)
                {
                    break;
                }

                int totalLength = carryLength + charsRead;
                bool isFinalBlock = reader.Peek() < 0;
                int searchableLength = isFinalBlock ? totalLength : Math.Max(0, totalLength - overlapLength);
                if (searchableLength > 0)
                {
                    ReadOnlySpan<char> searchableSpan = buffer.AsSpan(0, searchableLength);
                    int searchStart = 0;
                    int lineScanCursor = 0;
                    int lineNumber = lineNumberAtWindowStart;
                    while (searchStart <= searchableSpan.Length - pattern.Needle.Length)
                    {
                        int matchIndex = BoyerMooreIndexOf(searchableSpan, pattern, searchStart);
                        if (matchIndex < 0)
                        {
                            break;
                        }

                        lineNumber += CountNewLines(searchableSpan[lineScanCursor..matchIndex]);
                        if (!visitor(buffer.AsSpan(0, totalLength), matchIndex, lineNumber))
                        {
                            return;
                        }

                        searchStart = matchIndex + Math.Max(1, pattern.Needle.Length);
                        lineScanCursor = matchIndex;
                    }

                    lineNumberAtWindowStart += CountNewLines(searchableSpan);
                }

                if (isFinalBlock)
                {
                    break;
                }

                carryLength = totalLength - searchableLength;
                if (carryLength > 0)
                {
                    buffer.AsSpan(searchableLength, carryLength).CopyTo(buffer);
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Counts newline characters in a character span.
    /// </summary>
    /// <param name="span">The span to inspect.</param>
    /// <returns>The number of newline characters.</returns>
    private static int CountNewLines(ReadOnlySpan<char> span)
    {
        int count = 0;
        foreach (char c in span)
            if (c == '\n')
                ++count;

        return count;
    }

    /// <summary>
    /// Extracts a bounded snippet surrounding a pattern match.
    /// </summary>
    /// <param name="span">The source text span.</param>
    /// <param name="matchIndex">The zero-based index of the match.</param>
    /// <param name="matchLength">The matched length.</param>
    /// <returns>A trimmed snippet containing match context.</returns>
    private static string ExtractMatchSnippet(ReadOnlySpan<char> span, int matchIndex, int matchLength)
    {
        int snippetStart = Math.Max(0, matchIndex - 140);
        int snippetEnd = Math.Min(span.Length, matchIndex + matchLength + 140);
        ReadOnlySpan<char> snippet = span[snippetStart..snippetEnd].Trim();
        return snippet.ToString();
    }

    /// <summary>
    /// Scans a file line by line and invokes a callback for each line.
    /// </summary>
    /// <param name="filePath">The file path to scan.</param>
    /// <param name="visitor">The callback that receives each line and line number.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    private static void ScanFileLines(string filePath, LineVisitor visitor, CancellationToken cancellationToken)
    {
        const int BufferSize = 8192;
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: BufferSize);
        char[] readBuffer = ArrayPool<char>.Shared.Rent(BufferSize);
        char[] lineBuffer = ArrayPool<char>.Shared.Rent(BufferSize);
        int lineLength = 0;
        int lineNumber = 0;
        bool previousWasCarriageReturn = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int charsRead = reader.Read(readBuffer, 0, readBuffer.Length);
                if (charsRead <= 0)
                {
                    break;
                }

                for (int index = 0; index < charsRead; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    char current = readBuffer[index];
                    if (previousWasCarriageReturn)
                    {
                        previousWasCarriageReturn = false;
                        if (current == '\n')
                        {
                            continue;
                        }
                    }

                    if (current == '\r')
                    {
                        lineNumber++;
                        if (!visitor(lineBuffer.AsSpan(0, lineLength), lineNumber))
                        {
                            return;
                        }

                        lineLength = 0;
                        previousWasCarriageReturn = true;
                        continue;
                    }

                    if (current == '\n')
                    {
                        lineNumber++;
                        if (!visitor(lineBuffer.AsSpan(0, lineLength), lineNumber))
                        {
                            return;
                        }

                        lineLength = 0;
                        continue;
                    }

                    if (lineLength == lineBuffer.Length)
                    {
                        char[] expanded = ArrayPool<char>.Shared.Rent(lineBuffer.Length * 2);
                        lineBuffer.AsSpan(0, lineLength).CopyTo(expanded);
                        ArrayPool<char>.Shared.Return(lineBuffer, clearArray: false);
                        lineBuffer = expanded;
                    }

                    lineBuffer[lineLength++] = current;
                }
            }

            if (lineLength > 0 || previousWasCarriageReturn)
            {
                lineNumber++;
                visitor(lineBuffer.AsSpan(0, lineLength), lineNumber);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(readBuffer, clearArray: false);
            ArrayPool<char>.Shared.Return(lineBuffer, clearArray: false);
        }
    }

}

/// <summary>
/// Represents input criteria for catalog search.
/// </summary>
public sealed record ProjectSearchRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchRequest"/> record.
    /// </summary>
    /// <param name="inputPath">The root input path to search.</param>
    /// <param name="subdirectoryFilters">Optional top-level subdirectory filters.</param>
    /// <param name="query">An optional catalog query string.</param>
    public ProjectSearchRequest(
        string inputPath,
        IReadOnlyCollection<string>? subdirectoryFilters = null,
        string? query = null)
    {
        InputPath = inputPath;
        SubdirectoryFilters = subdirectoryFilters;
        Query = query;
    }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the search input path.
    /// </summary>
    public string InputPath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="IReadOnlyCollection{String}"/> representing top-level subdirectory filters.
    /// </summary>
    public IReadOnlyCollection<string>? SubdirectoryFilters { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the catalog query text.
    /// </summary>
    public string? Query { get; init; }
}

/// <summary>
/// Represents a catalog search entry result.
/// </summary>
public sealed record ProjectSearchEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchEntry"/> record.
    /// </summary>
    /// <param name="name">The display name of the matched entity.</param>
    /// <param name="relativePath">The relative source path for the entity.</param>
    /// <param name="details">The rendered entry details.</param>
    /// <param name="includedBy">The top-level pages that include the entry.</param>
    public ProjectSearchEntry(string name, string relativePath, string details, ImmutableArray<string> includedBy)
    {
        Name = name;
        RelativePath = relativePath;
        Details = details;
        IncludedBy = includedBy;
    }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the entry display name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the entry relative path.
    /// </summary>
    public string RelativePath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing rendered entry details.
    /// </summary>
    public string Details { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="ImmutableArray{String}"/> indicating includer paths.
    /// </summary>
    public ImmutableArray<string> IncludedBy { get; init; }
}

/// <summary>
/// Defines supported advanced project search modes.
/// </summary>
public enum ProjectSearchMode
{
    /// <summary>
    /// Indicates full-text matching across file content.
    /// </summary>
    FullText,

    /// <summary>
    /// Indicates scalar property matching by key path and value.
    /// </summary>
    Property,

    /// <summary>
    /// Indicates keyword usage matching based on discovered entity names.
    /// </summary>
    KeywordUsage,

    /// <summary>
    /// Indicates cross-reference matching for includes, macros, and mentions.
    /// </summary>
    CrossReference,
}

/// <summary>
/// Represents input criteria for advanced project search.
/// </summary>
public sealed record ProjectSearchAdvancedRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchAdvancedRequest"/> record.
    /// </summary>
    /// <param name="InputPath">The root input path to search.</param>
    /// <param name="Mode">The advanced search mode to execute.</param>
    /// <param name="Query">The optional query text.</param>
    /// <param name="PropertyPath">The optional property-path filter.</param>
    /// <param name="Limit">The requested maximum match count.</param>
    /// <param name="SubdirectoryFilters">Optional top-level subdirectory filters.</param>
    public ProjectSearchAdvancedRequest(
        string InputPath,
        ProjectSearchMode Mode,
        string? Query = null,
        string? PropertyPath = null,
        int Limit = 200,
        IReadOnlyCollection<string>? SubdirectoryFilters = null)
    {
        this.InputPath = InputPath;
        this.Mode = Mode;
        this.Query = Query;
        this.PropertyPath = PropertyPath;
        this.Limit = Limit;
        this.SubdirectoryFilters = SubdirectoryFilters;
    }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the search input path.
    /// </summary>
    public string InputPath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="ProjectSearchMode"/> indicating the advanced search mode.
    /// </summary>
    public ProjectSearchMode Mode { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing query text for the selected mode.
    /// </summary>
    public string? Query { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing a property-path filter.
    /// </summary>
    public string? PropertyPath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the requested result limit.
    /// </summary>
    public int Limit { get; init; } = 200;

    /// <summary>
    /// Gets or sets a <see cref="IReadOnlyCollection{String}"/> representing top-level subdirectory filters.
    /// </summary>
    public IReadOnlyCollection<string>? SubdirectoryFilters { get; init; }
}

/// <summary>
/// Represents a single advanced search match.
/// </summary>
public sealed record ProjectSearchMatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchMatch"/> record.
    /// </summary>
    /// <param name="relativePath">The relative path where the match was found.</param>
    /// <param name="fileKind">The normalized file kind.</param>
    /// <param name="matchKind">The mode-specific match kind identifier.</param>
    /// <param name="lineNumber">The one-based line number when available.</param>
    /// <param name="propertyPath">The matched property path when available.</param>
    /// <param name="targetPath">The referenced target path when available.</param>
    /// <param name="snippet">The rendered match snippet.</param>
    /// <param name="entityName">The matched entity name when available.</param>
    /// <param name="entityPath">The matched entity path when available.</param>
    /// <param name="includedBy">The top-level pages that include the source path.</param>
    public ProjectSearchMatch(
        string relativePath,
        string fileKind,
        string matchKind,
        int? lineNumber,
        string? propertyPath,
        string? targetPath,
        string snippet,
        string? entityName,
        string? entityPath,
        ImmutableArray<string> includedBy)
    {
        RelativePath = relativePath;
        FileKind = fileKind;
        MatchKind = matchKind;
        LineNumber = lineNumber;
        PropertyPath = propertyPath;
        TargetPath = targetPath;
        Snippet = snippet;
        EntityName = entityName;
        EntityPath = entityPath;
        IncludedBy = includedBy;
    }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the source relative path.
    /// </summary>
    public string RelativePath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the normalized file kind.
    /// </summary>
    public string FileKind { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the mode-specific match kind.
    /// </summary>
    public string MatchKind { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the one-based line number for the match.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the matched property path.
    /// </summary>
    public string? PropertyPath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the referenced target path.
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the rendered snippet.
    /// </summary>
    public string Snippet { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the related entity name.
    /// </summary>
    public string? EntityName { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the related entity path.
    /// </summary>
    public string? EntityPath { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="ImmutableArray{String}"/> indicating includer paths.
    /// </summary>
    public ImmutableArray<string> IncludedBy { get; init; }
}
