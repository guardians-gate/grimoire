using System.Buffers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Represents a project lore search engine that scans markdown and JSON content for matching text excerpts.
/// </summary>
public sealed partial class LoreQueryEngine
{
    /// <summary>
    /// A <see cref="SearchValues{T}"/> representing newline delimiters used while inferring markdown titles.
    /// </summary>
    private static readonly SearchValues<char> NewLineChars = SearchValues.Create("\r\n");

    /// <summary>
    /// A <see cref="string"/> representing the normalized project root searched by this engine.
    /// </summary>
    private readonly string _projectRoot;

    /// <summary>
    /// A <see cref="ILogger{TCategoryName}"/> representing diagnostics for search lifecycle events.
    /// </summary>
    private readonly ILogger<LoreQueryEngine> _logger;

    /// <summary>
    /// Initializes a lore query engine for a specific project root.
    /// </summary>
    /// <param name="projectRoot">The project root path representing the source directory to search.</param>
    /// <param name="logger">The optional logger representing where search diagnostics should be emitted.</param>
    public LoreQueryEngine(string projectRoot, ILogger<LoreQueryEngine>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        _projectRoot = Path.GetFullPath(projectRoot);
        _logger = logger ?? NullLogger<LoreQueryEngine>.Instance;
        if (!Directory.Exists(_projectRoot))
        {
            throw new ArgumentException("Project root directory does not exist.", nameof(projectRoot));
        }
    }

    /// <summary>
    /// Gets a <see cref="string"/> representing the normalized project root searched by this engine.
    /// </summary>
    public string ProjectRoot => _projectRoot;

    /// <summary>
    /// Searches lore content for a query string and returns an <see cref="IReadOnlyList{T}"/> representing ranked search matches.
    /// </summary>
    /// <param name="query">The query string representing text to locate in lore files.</param>
    /// <param name="limit">The maximum result count indicating how many matches should be returned.</param>
    /// <returns>An <see cref="IReadOnlyList{T}"/> representing matching lore excerpts.</returns>
    public IReadOnlyList<LoreSearchResult> Search(string query, int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query is required.", nameof(query));
        }

        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be at least 1.");
        }

        string[] files = GetLoreFiles();
        SearchStarted(query, limit, files.Length, _projectRoot);
        List<LoreSearchResult> results = [];
        int fileIndex = 0;
        foreach (string file in files)
        {
            fileIndex++;
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                string fileRelativePathForLog = Path.GetRelativePath(_projectRoot, file);
                ScanningLoreFile(fileIndex, files.Length, fileRelativePathForLog);
            }
            if (results.Count >= limit)
            {
                break;
            }

            string content = File.ReadAllText(file);
            int index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            string excerpt = BuildExcerpt(content, index, query.Length);
            string relativePath = Path.GetRelativePath(_projectRoot, file);
            string title = InferTitle(file, content);
            int lineNumber = CountLineNumber(content, index);
            results.Add(new(relativePath, title, excerpt, lineNumber));
        }

        SearchCompleted(results.Count);
        return results;
    }

    /// <summary>
    /// Enumerates searchable lore files under the project root and returns a <see cref="string"/> array representing normalized file paths.
    /// </summary>
    /// <returns>A <see cref="string"/> array representing markdown and JSON lore file paths.</returns>
    private string[] GetLoreFiles()
    {
        List<string> files = [];
        foreach (string file in Directory.GetFiles(_projectRoot, "*.md", SearchOption.AllDirectories))
        {
            if (IsInIgnoredDirectory(file))
            {
                continue;
            }

            files.Add(file);
        }

        foreach (string file in Directory.GetFiles(_projectRoot, "*.json", SearchOption.AllDirectories))
        {
            if (IsInIgnoredDirectory(file))
            {
                continue;
            }

            files.Add(file);
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return [.. files];
    }

    /// <summary>
    /// Determines whether a file path belongs to an ignored directory and returns a <see cref="bool"/> indicating exclusion status.
    /// </summary>
    /// <param name="filePath">The absolute file path representing a candidate lore source.</param>
    /// <returns><see langword="true"/> indicating the file should be excluded; otherwise, <see langword="false"/>.</returns>
    private bool IsInIgnoredDirectory(string filePath)
    {
        string relative = Path.GetRelativePath(_projectRoot, filePath);
        return relative.StartsWith($"settings{Path.DirectorySeparatorChar}fonts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a snippet around a match position and returns a <see cref="string"/> representing condensed excerpt text.
    /// </summary>
    /// <param name="content">The source content representing full file text.</param>
    /// <param name="matchIndex">The match index indicating where the query begins.</param>
    /// <param name="length">The match length indicating how many characters belong to the query.</param>
    /// <returns>A <see cref="string"/> representing trimmed excerpt text around the match.</returns>
    private static string BuildExcerpt(string content, int matchIndex, int length)
    {
        int start = Math.Max(0, matchIndex - 64);
        int end = Math.Min(content.Length, matchIndex + length + 96);
        string excerpt = content.Substring(start, end - start);
        string condensed = ExcerptWhitespaceRegex.Replace(excerpt, " ");
        return condensed.Trim();
    }

    /// <summary>
    /// Counts the line number for a match index and returns an <see cref="int"/> indicating the one-based line position.
    /// </summary>
    /// <param name="content">The source content representing full file text.</param>
    /// <param name="matchIndex">The match index indicating where the query begins.</param>
    /// <returns>An <see cref="int"/> indicating the one-based line number for the match.</returns>
    private static int CountLineNumber(string content, int matchIndex)
    {
        int line = 1;
        int limit = Math.Min(matchIndex, content.Length);
        for (int index = 0; index < limit; index++)
        {
            if (content[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// Infers a lore title from markdown content and returns a <see cref="string"/> representing the resolved display title.
    /// </summary>
    /// <param name="filePath">The file path representing the source document.</param>
    /// <param name="content">The source content representing full file text.</param>
    /// <returns>A <see cref="string"/> representing the inferred title.</returns>
    private static string InferTitle(string filePath, string content)
    {
        ReadOnlySpan<char> span = content.AsSpan();
        while (!span.IsEmpty)
        {
            int nextBreak = span.IndexOfAny(NewLineChars);
            ReadOnlySpan<char> line = nextBreak < 0 ? span : span[..nextBreak];
            string lineText = line.Trim().ToString();
            if (lineText.StartsWith('#'))
            {
                return lineText.TrimStart('#', ' ').Trim();
            }

            if (nextBreak < 0)
            {
                break;
            }

            int skip = 1;
            if (span.Length > nextBreak + 1 && span[nextBreak] == '\r' && span[nextBreak + 1] == '\n')
            {
                skip = 2;
            }

            span = span[(nextBreak + skip)..];
        }

        return Path.GetFileNameWithoutExtension(filePath);
    }

    /// <summary>
    /// Logs that a lore search has started.
    /// </summary>
    /// <param name="query">The query string representing the requested search text.</param>
    /// <param name="limit">The value indicating the requested result limit.</param>
    /// <param name="fileCount">The value indicating how many files will be scanned.</param>
    /// <param name="projectRoot">The project root path representing the active search source.</param>
    [LoggerMessage(EventId = 2070, Level = LogLevel.Debug, Message = "Lore search started: query={query}, limit={limit}, files={fileCount}, root={projectRoot}.")]
    private partial void SearchStarted(string query, int limit, int fileCount, string projectRoot);

    /// <summary>
    /// Logs that a lore search has completed.
    /// </summary>
    /// <param name="resultCount">The value indicating how many results were produced.</param>
    [LoggerMessage(EventId = 2071, Level = LogLevel.Debug, Message = "Lore search completed with {resultCount} results.")]
    private partial void SearchCompleted(int resultCount);

    /// <summary>
    /// Logs progress while scanning a lore file.
    /// </summary>
    /// <param name="index">The value indicating the one-based file index currently being processed.</param>
    /// <param name="total">The value indicating the total number of files in the scan set.</param>
    /// <param name="relativePath">The relative path representing the file currently being scanned.</param>
    [LoggerMessage(EventId = 2072, Level = LogLevel.Debug, Message = "Scanning lore file {index}/{total}: {relativePath}.")]
    private partial void ScanningLoreFile(int index, int total, string relativePath);

    /// <summary>
    /// Gets a <see cref="Regex"/> representing whitespace compaction used while building lore excerpts.
    /// </summary>
    [GeneratedRegex(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ExcerptWhitespaceRegex { get; }
}

/// <summary>
/// Represents a single lore search match containing source path, title, excerpt, and line information.
/// </summary>
/// <param name="Path">The relative path representing the source file of the match.</param>
/// <param name="Title">The title representing the matched lore entry.</param>
/// <param name="Excerpt">The excerpt representing contextual text around the match.</param>
/// <param name="LineNumber">The line number indicating where the match begins in the source file.</param>
public sealed record LoreSearchResult(string Path, string Title, string Excerpt, int LineNumber = 1);
