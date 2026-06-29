using Grimoire.Core;
using Grimoire.Core.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Grimoire.Ui;

/// <summary>
/// Defines workflow operations used by the Grimoire UI.
/// </summary>
public interface IGrimoireUiWorkflowService
{
    /// <summary>
    /// Compiles a source project into an output target.
    /// </summary>
    /// <param name="inputPath">The source project path.</param>
    /// <param name="outputPath">The output path for generated artifacts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<CompilationRequest> CompileAsync(string inputPath, string outputPath, CancellationToken cancellationToken);

    /// <summary>
    /// Scaffolds a new project at the specified path.
    /// </summary>
    /// <param name="targetPath">The target directory path.</param>
    /// <param name="overwriteExisting"><see langword="true"/> to overwrite existing files.</param>
    /// <returns>The full project path.</returns>
    string Scaffold(string targetPath, bool overwriteExisting);

    /// <summary>
    /// Synchronizes content from D&amp;D Beyond.
    /// </summary>
    /// <param name="options">The synchronization options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<DndBeyondSyncSummary> SyncDndbAsync(DndBeyondSyncOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Searches lore content within a project.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="query">The search query text.</param>
    /// <param name="limit">The maximum number of results.</param>
    /// <returns>The matching lore search results.</returns>
    IReadOnlyList<LoreSearchResult> SearchLore(string projectPath, string query, int limit);

    /// <summary>
    /// Opens a project and returns its indexed entries.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <returns>The discovered project entries.</returns>
    IReadOnlyList<ProjectFileItem> OpenProject(string projectPath);

    /// <summary>
    /// Reads a project file.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The file path relative to the project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<ProjectFileDocument> ReadProjectFileAsync(string projectPath, string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Saves text content to a project file.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The file path relative to the project root.</param>
    /// <param name="content">The file content to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveProjectFileAsync(string projectPath, string relativePath, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Renders a preview for a project file.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The file path relative to the project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<SourcebookPreviewResult> RenderPreviewAsync(string projectPath, string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Imports an asset file into the project.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="sourceAssetPath">The source asset file path.</param>
    /// <param name="targetSubdirectory">The optional destination subdirectory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<AssetImportResult> ImportAssetAsync(string projectPath, string sourceAssetPath, string? targetSubdirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Exports a project as a zip file.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="zipPath">The destination zip file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<string> ExportZipAsync(string projectPath, string zipPath, CancellationToken cancellationToken);

    /// <summary>
    /// Imports a zip archive into a target directory.
    /// </summary>
    /// <param name="zipPath">The source zip file path.</param>
    /// <param name="targetDirectory">The destination directory path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<string> ImportZipAsync(string zipPath, string targetDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Gets current git status entries for a project.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<IReadOnlyList<GitStatusEntry>> GetGitStatusAsync(string projectPath, CancellationToken cancellationToken);

    /// <summary>
    /// Gets git history entries for a project.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="limit">The maximum number of entries to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<IReadOnlyList<GitHistoryEntry>> GetGitHistoryAsync(string projectPath, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a git commit from current project changes.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="message">The commit message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<string> CommitGitAsync(string projectPath, string message, CancellationToken cancellationToken);

    /// <summary>
    /// Scans markdown references in a project and reports issues.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<ReferenceScanResult> ScanReferencesAsync(string projectPath, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a project file system entry.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The source entry path relative to the project root.</param>
    /// <param name="targetDirectory">The target directory path relative to the project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task MoveProjectEntryAsync(string projectPath, string relativePath, string targetDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Copies a project file system entry.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The source entry path relative to the project root.</param>
    /// <param name="targetDirectory">The target directory path relative to the project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<string> CopyProjectEntryAsync(string projectPath, string relativePath, string targetDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a project file system entry.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The entry path relative to the project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteProjectEntryAsync(string projectPath, string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Renames a project file system entry.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The entry path relative to the project root.</param>
    /// <param name="newName">The new file or directory name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    Task<string> RenameProjectEntryAsync(string projectPath, string relativePath, string newName, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an entry path to an absolute file system path.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <param name="relativePath">The entry path relative to the project root.</param>
    /// <returns>The full entry path.</returns>
    string GetProjectEntryFullPath(string projectPath, string relativePath);
}

/// <summary>
/// Implements UI workflow operations for project management and content processing.
/// </summary>
public sealed partial class GrimoireUiWorkflowService : IGrimoireUiWorkflowService
{
    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown include references.
    /// </summary>
    [GeneratedRegex(@"!\[[^\]]*\]\((?<path>[^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex IncludeRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown macro references.
    /// </summary>
    [GeneratedRegex(@"\$\{(?<path>[^}!]+)(![^}]+)?\}", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MacroRegex { get; }

    /// <summary>
    /// Defines the default Blake2b-512 hash used in generated cache state.
    /// </summary>
    private const string EmptyBlake2b512Hash = "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce";

    /// <summary>
    /// Stores directory names excluded during project traversal.
    /// </summary>
    private static readonly ImmutableArray<string> IgnoredProjectDirectories = [".git", "bin", "obj", ".caches"];

    /// <summary>
    /// Stores editable text file extensions.
    /// </summary>
    private static readonly ImmutableArray<string> TextFileExtensions = [".md", ".json", ".yml", ".yaml", ".txt", ".css", ".html"];

    /// <summary>
    /// Stores supported asset file extensions.
    /// </summary>
    private static readonly ImmutableArray<string> AssetExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"];

    /// <summary>
    /// Stores project directories treated as special entries.
    /// </summary>
    private static readonly ImmutableArray<string> SpecialDirectories = ["content", "maps", "settings"];

    /// <summary>
    /// Stores snippet directories treated as special entries.
    /// </summary>
    private static readonly ImmutableArray<string> WellKnownSnippetDirectories = ["creatures", "items", "players", "spells"];

    /// <summary>
    /// Stores markdown files treated as special content entries.
    /// </summary>
    private static readonly ImmutableArray<string> SpecialContentFiles = ["AUTHORS.md", "LICENSE.md", "README.md", "SOURCES.md", "TITLE.md"];

    /// <summary>
    /// Stores the factory that creates HTTP clients for synchronization requests.
    /// </summary>
    private readonly Func<HttpClient> _httpClientFactory;

    /// <summary>
    /// Stores the optional base URI for D&amp;D Beyond synchronization.
    /// </summary>
    private readonly Uri? _dndBaseUri;

    /// <summary>
    /// Stores the localizer used by synchronization workflows.
    /// </summary>
    private readonly IStringLocalizer _localizer;

    /// <summary>
    /// Stores the logger factory used for workflow services.
    /// </summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Stores the workflow logger instance.
    /// </summary>
    private readonly ILogger<GrimoireUiWorkflowService> _logger;

    /// <summary>
    /// Stores the factory that creates D&amp;D Beyond sync loggers.
    /// </summary>
    private readonly Func<ILogger<DndBeyondSyncService>> _dndLoggerFactory;

    /// <summary>
    /// Stores the compiler used for sourcebook preview rendering.
    /// </summary>
    private readonly SourcebookCompiler _previewCompiler;

    /// <summary>
    /// Stores the cached project entry list for the last opened project root.
    /// </summary>
    private IReadOnlyList<ProjectFileItem>? _projectItemsCache;

    /// <summary>
    /// Stores the project root path associated with <see cref="_projectItemsCache"/>.
    /// </summary>
    private string? _projectItemsCacheRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrimoireUiWorkflowService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The optional HTTP client factory.</param>
    /// <param name="dndBaseUri">The optional D&amp;D Beyond base URI override.</param>
    /// <param name="localizer">The optional string localizer.</param>
    /// <param name="dndLogger">The optional D&amp;D Beyond sync logger.</param>
    /// <param name="loggerFactory">The optional logger factory.</param>
    /// <param name="logger">The optional workflow logger.</param>
    public GrimoireUiWorkflowService(
        Func<HttpClient>? httpClientFactory = null,
        Uri? dndBaseUri = null,
        IStringLocalizer? localizer = null,
        ILogger<DndBeyondSyncService>? dndLogger = null,
        ILoggerFactory? loggerFactory = null,
        ILogger<GrimoireUiWorkflowService>? logger = null)
    {
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient());
        _dndBaseUri = dndBaseUri;
        _localizer = localizer ?? new GrimoireLocalizationFactory().CreateDefault();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = logger ?? _loggerFactory.CreateLogger<GrimoireUiWorkflowService>();
        _previewCompiler = new SourcebookCompiler(_loggerFactory.CreateLogger<SourcebookCompiler>());
        _dndLoggerFactory = dndLogger is null
            ? () => _loggerFactory.CreateLogger<DndBeyondSyncService>()
            : () => dndLogger;
    }

    /// <inheritdoc />
    public async Task<CompilationRequest> CompileAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        WorkflowCompileStarted(inputPath, outputPath);
        InputInspector inputInspector = new(_loggerFactory.CreateLogger<InputInspector>());
        OutputInspector outputInspector = new(_loggerFactory.CreateLogger<OutputInspector>());
        CompilationPlanner planner = new(inputInspector, outputInspector, _loggerFactory.CreateLogger<CompilationPlanner>());
        CompilationRequest request = planner.Plan(inputPath, outputPath);
        SourcebookCompiler compiler = new(_loggerFactory.CreateLogger<SourcebookCompiler>());
        await compiler.CompileAsync(request, cancellationToken).ConfigureAwait(false);
        WorkflowCompileCompleted(request.OutputPath, request.Target);
        return request;
    }

    /// <inheritdoc />
    public string Scaffold(string targetPath, bool overwriteExisting)
    {
        WorkflowScaffoldStarted(targetPath, overwriteExisting);
        ProjectScaffolder scaffolder = new(_loggerFactory.CreateLogger<ProjectScaffolder>());
        scaffolder.ScaffoldProject(targetPath, overwriteExisting);
        InvalidateProjectCaches();
        return Path.GetFullPath(targetPath);
    }

    /// <inheritdoc />
    public async Task<DndBeyondSyncSummary> SyncDndbAsync(DndBeyondSyncOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        WorkflowDndSyncStarted(options.OutputBaseDirectory);
        using HttpClient httpClient = _httpClientFactory();
        DndBeyondSyncService syncService = new(httpClient, _dndBaseUri, _localizer, _dndLoggerFactory());
        return await syncService.SyncAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<LoreSearchResult> SearchLore(string projectPath, string query, int limit)
    {
        WorkflowLoreSearchStarted(projectPath, query, limit);
        LoreQueryEngine engine = new(projectPath, _loggerFactory.CreateLogger<LoreQueryEngine>());
        return engine.Search(query, limit);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProjectFileItem> OpenProject(string projectPath)
    {
        WorkflowOpenProject(projectPath);
        string root = ResolveExistingProjectRoot(projectPath);
        EnsureProjectCacheScaffold(root);
        if (_projectItemsCache is not null &&
            string.Equals(_projectItemsCacheRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            return _projectItemsCache;
        }

        List<ProjectFileItem> items = [];
        int directoryIndex = 0;
        foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            directoryIndex++;
            string relativePath = ToProjectPath(Path.GetRelativePath(root, directory));
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                WorkflowProjectIteration("open-project-directories", directoryIndex, relativePath);
            }
            string name = Path.GetFileName(directory);
            if (IgnoredProjectDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsIgnoredRelativePath(relativePath))
            {
                continue;
            }

            items.Add(CreateProjectFileItem(root, directory, isDirectory: true));
        }

        int fileIndex = 0;
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            fileIndex++;
            string relativePath = ToProjectPath(Path.GetRelativePath(root, file));
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                WorkflowProjectIteration("open-project-files", fileIndex, relativePath);
            }
            if (IsIgnoredRelativePath(relativePath))
            {
                continue;
            }

            items.Add(CreateProjectFileItem(root, file, isDirectory: false));
        }

        items.Sort(static (left, right) =>
        {
            int directorySort = right.IsDirectory.CompareTo(left.IsDirectory);
            return directorySort != 0 ? directorySort : string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase);
        });
        _projectItemsCacheRoot = root;
        _projectItemsCache = [.. items];
        return _projectItemsCache;
    }

    /// <inheritdoc />
    public async Task<ProjectFileDocument> ReadProjectFileAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
    {
        WorkflowReadProjectFile(relativePath);
        string root = ResolveExistingProjectRoot(projectPath);
        string filePath = ResolveProjectFile(root, relativePath, mustExist: true);
        string extension = Path.GetExtension(filePath);
        if (!TextFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"File is not editable text: {relativePath}", nameof(relativePath));
        }

        string content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<KeywordHighlight> highlights = BuildKeywordHighlights(root, content);
        return new(ToProjectPath(Path.GetRelativePath(root, filePath)), content, ResolveFileKind(filePath), highlights);
    }

    /// <inheritdoc />
    public async Task SaveProjectFileAsync(string projectPath, string relativePath, string content, CancellationToken cancellationToken)
    {
        WorkflowSaveProjectFile(relativePath);
        string root = ResolveExistingProjectRoot(projectPath);
        string filePath = ResolveProjectFile(root, relativePath, mustExist: false);
        string? parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
        InvalidateProjectCaches();
    }

    /// <inheritdoc />
    public async Task<SourcebookPreviewResult> RenderPreviewAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
    {
        WorkflowRenderPreview(relativePath);
        string root = ResolveExistingProjectRoot(projectPath);
        return await Task.Run(
                async () => await _previewCompiler.RenderPreviewAsync(root, relativePath, cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AssetImportResult> ImportAssetAsync(string projectPath, string sourceAssetPath, string? targetSubdirectory, CancellationToken cancellationToken)
    {
        string root = ResolveExistingProjectRoot(projectPath);
        string sourcePath = Path.GetFullPath(sourceAssetPath);
        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException($"Asset file does not exist: {sourceAssetPath}", nameof(sourceAssetPath));
        }

        string targetDirectoryName = string.IsNullOrWhiteSpace(targetSubdirectory) ? "assets" : targetSubdirectory.Trim();
        string targetDirectory = ResolveProjectFile(root, targetDirectoryName, mustExist: false);
        Directory.CreateDirectory(targetDirectory);
        string targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
        await using FileStream source = File.OpenRead(sourcePath);
        await using FileStream target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        string relativePath = ToProjectPath(Path.GetRelativePath(root, targetPath));
        InvalidateProjectCaches();
        return new(relativePath, $"![{Path.GetFileNameWithoutExtension(targetPath)}]({relativePath})");
    }

    /// <inheritdoc />
    public async Task<string> ExportZipAsync(string projectPath, string zipPath, CancellationToken cancellationToken)
    {
        string root = ResolveExistingProjectRoot(projectPath);
        string fullZipPath = Path.GetFullPath(zipPath);
        string? parent = Path.GetDirectoryName(fullZipPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(fullZipPath))
        {
            File.Delete(fullZipPath);
        }

        await ZipFile.CreateFromDirectoryAsync(root, fullZipPath, CompressionLevel.Optimal, includeBaseDirectory: false, cancellationToken).ConfigureAwait(false);
        return fullZipPath;
    }

    /// <inheritdoc />
    public async Task<string> ImportZipAsync(string zipPath, string targetDirectory, CancellationToken cancellationToken)
    {
        string fullZipPath = Path.GetFullPath(zipPath);
        if (!File.Exists(fullZipPath))
        {
            throw new ArgumentException($"Zip file does not exist: {zipPath}", nameof(zipPath));
        }

        string targetRoot = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(targetRoot);
        await ZipFile.ExtractToDirectoryAsync(fullZipPath, targetRoot, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
        InvalidateProjectCaches();
        return targetRoot;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitStatusEntry>> GetGitStatusAsync(string projectPath, CancellationToken cancellationToken)
    {
        string root = ResolveExistingProjectRoot(projectPath);
        GitCommandResult result = await RunGitAsync(root, "status --short", allowFailure: true, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return
        [
            .. result.StandardOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseGitStatusEntry),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHistoryEntry>> GetGitHistoryAsync(string projectPath, int limit, CancellationToken cancellationToken)
    {
        string root = ResolveExistingProjectRoot(projectPath);
        int boundedLimit = Math.Clamp(limit, 1, 100);
        GitCommandResult result = await RunGitAsync(root, $"log -n {boundedLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)} --date=short --pretty=format:%h%x09%ad%x09%an%x09%s", allowFailure: true, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return
        [
            .. result.StandardOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseGitHistoryEntry),
        ];
    }

    /// <inheritdoc />
    public async Task<string> CommitGitAsync(string projectPath, string message, CancellationToken cancellationToken)
    {
        string root = ResolveExistingProjectRoot(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        GitCommandResult addResult = await RunGitAsync(root, "add -A", allowFailure: false, cancellationToken).ConfigureAwait(false);
        if (addResult.ExitCode != 0)
        {
            throw new InvalidOperationException(addResult.StandardError);
        }

        GitCommandResult commitResult = await RunGitAsync(root, $"commit -m {QuoteGitArgument(message.Trim())}", allowFailure: true, cancellationToken).ConfigureAwait(false);
        return commitResult.ExitCode != 0
            ? throw new InvalidOperationException(string.IsNullOrWhiteSpace(commitResult.StandardError)
                ? commitResult.StandardOutput
                : commitResult.StandardError)
            : commitResult.StandardOutput.Trim();
    }

    /// <inheritdoc />
    public async Task<ReferenceScanResult> ScanReferencesAsync(string projectPath, CancellationToken cancellationToken)
    {
        string root = ResolveExistingProjectRoot(projectPath);
        List<ReferenceScanIssue> issues = [];
        int filesScanned = 0;
        int includeCount = 0;
        int macroCount = 0;
        int scanIndex = 0;
        foreach (string file in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories))
        {
            scanIndex++;
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = ToProjectPath(Path.GetRelativePath(root, file));
            WorkflowProjectIteration("scan-references", scanIndex, relativePath);
            if (IsIgnoredRelativePath(relativePath))
            {
                continue;
            }

            filesScanned++;
            string content = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            foreach (Match match in IncludeRegex.Matches(content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                includeCount++;
                AddBrokenReferenceIfMissing(root, file, relativePath, content, match, match.Groups["path"].Value, issues);
            }

            foreach (Match match in MacroRegex.Matches(content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                macroCount++;
                AddBrokenReferenceIfMissing(root, file, relativePath, content, match, match.Groups["path"].Value, issues);
            }
        }

        return new ReferenceScanResult(filesScanned, includeCount, macroCount, issues);
    }

    /// <inheritdoc />
    public Task MoveProjectEntryAsync(string projectPath, string relativePath, string targetDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = ResolveExistingProjectRoot(projectPath);
        string sourcePath = ResolveProjectEntry(root, relativePath, mustExist: true);
        string targetDirectoryPath = ResolveProjectDirectory(root, targetDirectory, mustExist: true);
        string targetPath = Path.Combine(targetDirectoryPath, Path.GetFileName(sourcePath));
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            throw new IOException($"Target already exists: {targetPath}");
        }

        if (Directory.Exists(sourcePath))
        {
            Directory.Move(sourcePath, targetPath);
        }
        else
        {
            File.Move(sourcePath, targetPath);
        }

        InvalidateProjectCaches();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> CopyProjectEntryAsync(string projectPath, string relativePath, string targetDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = ResolveExistingProjectRoot(projectPath);
        string sourcePath = ResolveProjectEntry(root, relativePath, mustExist: true);
        string targetDirectoryPath = ResolveProjectDirectory(root, targetDirectory, mustExist: true);
        string targetPath = BuildAvailableCopyPath(Path.Combine(targetDirectoryPath, Path.GetFileName(sourcePath)));
        if (Directory.Exists(sourcePath))
        {
            CopyDirectory(sourcePath, targetPath);
        }
        else
        {
            File.Copy(sourcePath, targetPath);
        }

        InvalidateProjectCaches();
        return Task.FromResult(ToProjectPath(Path.GetRelativePath(root, targetPath)));
    }

    /// <inheritdoc />
    public Task DeleteProjectEntryAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = ResolveExistingProjectRoot(projectPath);
        string entryPath = ResolveProjectEntry(root, relativePath, mustExist: true);
        if (Directory.Exists(entryPath))
        {
            Directory.Delete(entryPath, recursive: true);
        }
        else
        {
            File.Delete(entryPath);
        }

        InvalidateProjectCaches();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> RenameProjectEntryAsync(string projectPath, string relativePath, string newName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            newName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("New name must be a valid file or directory name.", nameof(newName));
        }

        string root = ResolveExistingProjectRoot(projectPath);
        string entryPath = ResolveProjectEntry(root, relativePath, mustExist: true);
        string parent = Path.GetDirectoryName(entryPath) ?? root;
        string targetPath = Path.Combine(parent, newName.Trim());
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            throw new IOException($"Target already exists: {targetPath}");
        }

        if (Directory.Exists(entryPath))
        {
            Directory.Move(entryPath, targetPath);
        }
        else
        {
            File.Move(entryPath, targetPath);
        }

        InvalidateProjectCaches();
        return Task.FromResult(ToProjectPath(Path.GetRelativePath(root, targetPath)));
    }

    /// <summary>
    /// Invalidates cached project and preview data.
    /// </summary>
    private void InvalidateProjectCaches()
    {
        _projectItemsCache = null;
        _projectItemsCacheRoot = null;
        _previewCompiler.InvalidatePreviewCache();
    }

    /// <inheritdoc />
    public string GetProjectEntryFullPath(string projectPath, string relativePath)
    {
        string root = ResolveExistingProjectRoot(projectPath);
        return ResolveProjectEntry(root, relativePath, mustExist: true);
    }

    /// <summary>
    /// Resolves and validates an existing project root path.
    /// </summary>
    /// <param name="projectPath">The project path to validate.</param>
    /// <returns>The normalized full project root path.</returns>
    private static string ResolveExistingProjectRoot(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        string root = Path.GetFullPath(projectPath.Trim());
        if (!Directory.Exists(root))
        {
            throw new ArgumentException($"Project directory does not exist: {root}", nameof(projectPath));
        }

        return root;
    }

    /// <summary>
    /// Ensures cache directories and cache seed files exist for a project.
    /// </summary>
    /// <param name="root">The project root path.</param>
    private static void EnsureProjectCacheScaffold(string root)
    {
        string cacheRoot = Path.Combine(root, ".caches");
        string generatedRoot = Path.Combine(cacheRoot, "generated");
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(generatedRoot);

        string hashesPath = Path.Combine(cacheRoot, "hashes.json");
        string topicsPath = Path.Combine(cacheRoot, "topics.json");
        string statePath = Path.Combine(cacheRoot, "state.json");
        if (!File.Exists(hashesPath))
        {
            File.WriteAllText(hashesPath, "{}\n");
        }

        if (!File.Exists(topicsPath))
        {
            File.WriteAllText(topicsPath, "{}\n");
        }

        if (!File.Exists(statePath))
        {
            File.WriteAllText(
                statePath,
                $$"""
                {
                  "indexedTopicCount": 0,
                  "indexedEntityCount": 0,
                  "cachedUtc": "",
                  "indexedNamesHashBlake2b512": "{{EmptyBlake2b512Hash}}"
                }
                """);
        }
    }

    /// <summary>
    /// Resolves a project-relative path to a validated file path.
    /// </summary>
    /// <param name="root">The project root path.</param>
    /// <param name="relativePath">The candidate relative file path.</param>
    /// <param name="mustExist"><see langword="true"/> to require that the file exists.</param>
    /// <returns>The normalized full file path.</returns>
    private static string ResolveProjectFile(string root, string relativePath, bool mustExist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string fullPath = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.GetFullPath(Path.Combine(root, relativePath));
        string projectRelative = Path.GetRelativePath(root, fullPath);
        if (projectRelative.Equals("..", StringComparison.Ordinal) ||
            projectRelative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            projectRelative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(projectRelative))
        {
            throw new ArgumentException("Path must stay inside the project.", nameof(relativePath));
        }

        if (mustExist && !File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Project file not found: {relativePath}", fullPath);
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves a project-relative path to a validated file or directory path.
    /// </summary>
    /// <param name="root">The project root path.</param>
    /// <param name="relativePath">The candidate entry path.</param>
    /// <param name="mustExist"><see langword="true"/> to require that the entry exists.</param>
    /// <returns>The normalized full entry path.</returns>
    private static string ResolveProjectEntry(string root, string relativePath, bool mustExist)
    {
        string fullPath = ResolveProjectFile(root, relativePath, mustExist: false);
        if (mustExist && !File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException($"Project entry not found: {relativePath}", fullPath);
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves a project-relative path to a validated directory path.
    /// </summary>
    /// <param name="root">The project root path.</param>
    /// <param name="relativePath">The candidate directory path.</param>
    /// <param name="mustExist"><see langword="true"/> to require that the directory exists.</param>
    /// <returns>The normalized full directory path.</returns>
    private static string ResolveProjectDirectory(string root, string relativePath, bool mustExist)
    {
        string directoryPath = string.IsNullOrWhiteSpace(relativePath) || relativePath.Equals(".", StringComparison.Ordinal)
            ? root
            : ResolveProjectEntry(root, relativePath, mustExist);
        if (mustExist && !Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {relativePath}");
        }

        return directoryPath;
    }

    /// <summary>
    /// Creates a project tree item from a file system path.
    /// </summary>
    /// <param name="root">The project root path.</param>
    /// <param name="fullPath">The entry full path.</param>
    /// <param name="isDirectory"><see langword="true"/> when the entry is a directory.</param>
    /// <returns>A project file item describing the entry.</returns>
    private static ProjectFileItem CreateProjectFileItem(string root, string fullPath, bool isDirectory)
    {
        string relativePath = ToProjectPath(Path.GetRelativePath(root, fullPath));
        string parentPath = ToProjectPath(Path.GetRelativePath(root, Path.GetDirectoryName(fullPath) ?? root));
        if (parentPath.Equals(".", StringComparison.Ordinal))
        {
            parentPath = string.Empty;
        }

        string kind = isDirectory ? ResolveDirectoryKind(fullPath) : ResolveFileKind(fullPath);
        return new ProjectFileItem(
            Path.GetFileName(fullPath),
            relativePath,
            isDirectory,
            kind,
            ResolveProjectIcon(fullPath, isDirectory, kind),
            CountProjectDepth(relativePath),
            parentPath,
            IsSpecialProjectEntry(fullPath, isDirectory));
    }

    /// <summary>
    /// Counts nested path separators in a project-relative path.
    /// </summary>
    /// <param name="relativePath">The project-relative path.</param>
    /// <returns>The path depth.</returns>
    private static int CountProjectDepth(string relativePath)
    {
        return relativePath.Count(static character => character == '/');
    }

    /// <summary>
    /// Determines whether a project entry should be flagged as special.
    /// </summary>
    /// <param name="fullPath">The entry full path.</param>
    /// <param name="isDirectory"><see langword="true"/> when the entry is a directory.</param>
    /// <returns><see langword="true"/> when the entry is special.</returns>
    private static bool IsSpecialProjectEntry(string fullPath, bool isDirectory)
    {
        string name = Path.GetFileName(fullPath);
        if (isDirectory)
        {
            return SpecialDirectories.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                   WellKnownSnippetDirectories.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        return string.Equals(name, "TEMPLATE.md", StringComparison.OrdinalIgnoreCase) ||
               SpecialContentFiles.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a UI kind token for a directory path.
    /// </summary>
    /// <param name="fullPath">The directory full path.</param>
    /// <returns>The resolved kind token.</returns>
    private static string ResolveDirectoryKind(string fullPath)
    {
        string name = Path.GetFileName(fullPath);
        if (string.Equals(name, "content", StringComparison.OrdinalIgnoreCase))
        {
            return "directory-content";
        }

        if (string.Equals(name, "maps", StringComparison.OrdinalIgnoreCase))
        {
            return "directory-maps";
        }

        if (string.Equals(name, "settings", StringComparison.OrdinalIgnoreCase))
        {
            return "directory-settings";
        }

        if (string.Equals(name, "creatures", StringComparison.OrdinalIgnoreCase))
        {
            return "snippet-directory-creatures";
        }

        if (string.Equals(name, "items", StringComparison.OrdinalIgnoreCase))
        {
            return "snippet-directory-items";
        }

        if (string.Equals(name, "players", StringComparison.OrdinalIgnoreCase))
        {
            return "snippet-directory-players";
        }

        if (string.Equals(name, "spells", StringComparison.OrdinalIgnoreCase))
        {
            return "snippet-directory-spells";
        }

        return "folder";
    }

    /// <summary>
    /// Resolves a UI kind token for a file path.
    /// </summary>
    /// <param name="filePath">The file full path.</param>
    /// <returns>The resolved kind token.</returns>
    private static string ResolveFileKind(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        if (string.Equals(fileName, "TEMPLATE.md", StringComparison.OrdinalIgnoreCase))
        {
            return "template";
        }

        string directory = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
        if (string.Equals(directory, "snippets", StringComparison.OrdinalIgnoreCase) ||
            filePath.Contains($"{Path.DirectorySeparatorChar}snippets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return "snippet-markdown";
        }

        string extension = Path.GetExtension(filePath);
        if (AssetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return "asset";
        }

        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveSpecialContentKind(fileName) ?? "content-markdown";
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        if (extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            return "settings";
        }

        return "file";
    }

    /// <summary>
    /// Resolves a special markdown content kind token from a file name.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The special content kind token, or <see langword="null"/>.</returns>
    private static string? ResolveSpecialContentKind(string fileName)
    {
        return fileName.ToUpperInvariant() switch
        {
            "AUTHORS.MD" => "authors",
            "LICENSE.MD" => "license",
            "README.MD" => "readme",
            "SOURCES.MD" => "sources",
            "TITLE.MD" => "title",
            _ => null,
        };
    }

    /// <summary>
    /// Resolves a UI icon token for a project entry.
    /// </summary>
    /// <param name="fullPath">The entry full path.</param>
    /// <param name="isDirectory"><see langword="true"/> when the entry is a directory.</param>
    /// <param name="kind">The resolved entry kind token.</param>
    /// <returns>The resolved icon token.</returns>
    private static string ResolveProjectIcon(string fullPath, bool isDirectory, string kind)
    {
        if (isDirectory)
        {
            return kind switch
            {
                "directory-content" => "book",
                "directory-maps" => "map",
                "directory-settings" => "gear",
                "snippet-directory-creatures" => "bestiary",
                "snippet-directory-items" => "bag",
                "snippet-directory-players" => "people",
                "snippet-directory-spells" => "spark",
                _ => "folder",
            };
        }

        return kind switch
        {
            "template" => "template",
            "authors" => "people",
            "license" => "cert",
            "readme" => "book",
            "sources" => "cite",
            "title" => "cover",
            "snippet-markdown" => "snippet-md",
            "content-markdown" => "page",
            "json" => "code",
            "settings" => "gear",
            "asset" => "asset",
            _ => Path.GetExtension(fullPath).Equals(".md", StringComparison.OrdinalIgnoreCase) ? "page" : "file",
        };
    }

    /// <summary>
    /// Adds a missing-reference issue when a markdown include or macro target is absent.
    /// </summary>
    /// <param name="root">The project root path.</param>
    /// <param name="currentFile">The full path to the source markdown file.</param>
    /// <param name="relativePath">The project-relative path to the source file.</param>
    /// <param name="content">The source file content.</param>
    /// <param name="match">The reference match.</param>
    /// <param name="rawReference">The raw reference text.</param>
    /// <param name="issues">The issue sink list.</param>
    private static void AddBrokenReferenceIfMissing(string root, string currentFile, string relativePath, string content, Match match, string rawReference, List<ReferenceScanIssue> issues)
    {
        string referencePath = rawReference;
        int queryIndex = referencePath.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            referencePath = referencePath[..queryIndex];
        }

        if (Uri.TryCreate(referencePath, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            return;
        }

        string currentDirectory = Path.GetDirectoryName(currentFile) ?? root;
        string targetPath = Path.GetFullPath(Path.Combine(currentDirectory, referencePath));
        if (File.Exists(targetPath))
        {
            return;
        }

        issues.Add(new ReferenceScanIssue(relativePath, CountLineNumber(content, match.Index), rawReference, "Missing reference target"));
    }

    /// <summary>
    /// Counts the one-based line number for a character index in text.
    /// </summary>
    /// <param name="content">The text content.</param>
    /// <param name="index">The character index.</param>
    /// <returns>The one-based line number.</returns>
    private static int CountLineNumber(string content, int index)
    {
        int line = 1;
        int length = Math.Min(index, content.Length);
        for (int i = 0; i < length; i++)
        {
            if (content[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// Executes a git command in a project directory.
    /// </summary>
    /// <param name="root">The project root path.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="allowFailure"><see langword="true"/> to return non-zero exit results without throwing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation result.</returns>
    private static async Task<GitCommandResult> RunGitAsync(string root, string arguments, bool allowFailure, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = new();
        process.StartInfo = startInfo;
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start git.");
        }

        string standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string standardError = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError);
        }

        return new(process.ExitCode, standardOutput, standardError);
    }

    /// <summary>
    /// Parses a single line of git status output.
    /// </summary>
    /// <param name="line">The git status line.</param>
    /// <returns>The parsed git status entry.</returns>
    private static GitStatusEntry ParseGitStatusEntry(string line)
    {
        string status = line.Length >= 2 ? line[..2].Trim() : "?";
        string path = line.Length > 3 ? line[3..].Trim() : line.Trim();
        int renameIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameIndex >= 0)
        {
            path = path[(renameIndex + 4)..];
        }

        return new(status, path);
    }

    /// <summary>
    /// Parses a single line of git history output.
    /// </summary>
    /// <param name="line">The git history line.</param>
    /// <returns>The parsed git history entry.</returns>
    private static GitHistoryEntry ParseGitHistoryEntry(string line)
    {
        string[] parts = line.Split('\t', 4);
        return parts.Length == 4
            ? new GitHistoryEntry(parts[0], parts[1], parts[2], parts[3])
            : new(string.Empty, string.Empty, string.Empty, line);
    }

    /// <summary>
    /// Quotes and escapes a git command argument.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <returns>The quoted argument string.</returns>
    private static string QuoteGitArgument(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            if (character is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Builds a non-conflicting copy destination path.
    /// </summary>
    /// <param name="requestedPath">The initially requested destination path.</param>
    /// <returns>An available destination path.</returns>
    private static string BuildAvailableCopyPath(string requestedPath)
    {
        if (!File.Exists(requestedPath) && !Directory.Exists(requestedPath))
        {
            return requestedPath;
        }

        string directory = Path.GetDirectoryName(requestedPath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        for (int index = 1; index < 1000; index++)
        {
            string candidate = Path.Combine(directory, $"{name} copy {index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Unable to find available copy path for {requestedPath}");
    }

    /// <summary>
    /// Copies a directory tree to a new location.
    /// </summary>
    /// <param name="sourceDirectory">The source directory path.</param>
    /// <param name="targetDirectory">The destination directory path.</param>
    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string targetFile = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
            File.Copy(file, targetFile);
        }
    }

    /// <summary>
    /// Normalizes path separators for project-relative paths.
    /// </summary>
    /// <param name="path">The source path.</param>
    /// <returns>The normalized path.</returns>
    private static string ToProjectPath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Logs the start of a compile workflow.
    /// </summary>
    /// <param name="inputPath">The input project path.</param>
    /// <param name="outputPath">The output path.</param>
    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "UI workflow compile started: input={inputPath}, output={outputPath}.")]
    private partial void WorkflowCompileStarted(string inputPath, string outputPath);

    /// <summary>
    /// Logs completion of a compile workflow.
    /// </summary>
    /// <param name="outputPath">The output path.</param>
    /// <param name="target">The export target.</param>
    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug, Message = "UI workflow compile completed: output={outputPath}, target={target}.")]
    private partial void WorkflowCompileCompleted(string outputPath, ExportTarget target);

    /// <summary>
    /// Logs the start of project scaffolding.
    /// </summary>
    /// <param name="targetPath">The scaffold target path.</param>
    /// <param name="overwriteExisting"><see langword="true"/> when existing content may be overwritten.</param>
    [LoggerMessage(EventId = 4002, Level = LogLevel.Debug, Message = "UI workflow scaffold started: target={targetPath}, overwriteExisting={overwriteExisting}.")]
    private partial void WorkflowScaffoldStarted(string targetPath, bool overwriteExisting);

    /// <summary>
    /// Logs the start of D&amp;D Beyond synchronization.
    /// </summary>
    /// <param name="outputPath">The synchronization output path.</param>
    [LoggerMessage(EventId = 4003, Level = LogLevel.Debug, Message = "UI workflow D&D Beyond sync started: output={outputPath}.")]
    private partial void WorkflowDndSyncStarted(string outputPath);

    /// <summary>
    /// Logs a lore search invocation.
    /// </summary>
    /// <param name="projectPath">The project path.</param>
    /// <param name="query">The search query.</param>
    /// <param name="limit">The result limit.</param>
    [LoggerMessage(EventId = 4004, Level = LogLevel.Debug, Message = "UI workflow lore search: project={projectPath}, query={query}, limit={limit}.")]
    private partial void WorkflowLoreSearchStarted(string projectPath, string query, int limit);

    /// <summary>
    /// Logs opening a project.
    /// </summary>
    /// <param name="projectPath">The project path.</param>
    [LoggerMessage(EventId = 4005, Level = LogLevel.Debug, Message = "UI workflow open project: {projectPath}.")]
    private partial void WorkflowOpenProject(string projectPath);

    /// <summary>
    /// Logs reading a project file.
    /// </summary>
    /// <param name="relativePath">The relative file path.</param>
    [LoggerMessage(EventId = 4006, Level = LogLevel.Debug, Message = "UI workflow read project file: {relativePath}.")]
    private partial void WorkflowReadProjectFile(string relativePath);

    /// <summary>
    /// Logs saving a project file.
    /// </summary>
    /// <param name="relativePath">The relative file path.</param>
    [LoggerMessage(EventId = 4007, Level = LogLevel.Debug, Message = "UI workflow save project file: {relativePath}.")]
    private partial void WorkflowSaveProjectFile(string relativePath);

    /// <summary>
    /// Logs rendering a file preview.
    /// </summary>
    /// <param name="relativePath">The relative file path.</param>
    [LoggerMessage(EventId = 4008, Level = LogLevel.Debug, Message = "UI workflow render preview for {relativePath}.")]
    private partial void WorkflowRenderPreview(string relativePath);

    /// <summary>
    /// Logs iteration progress for long-running project workflows.
    /// </summary>
    /// <param name="phase">The workflow phase name.</param>
    /// <param name="index">The one-based iteration index.</param>
    /// <param name="relativePath">The current project-relative path.</param>
    [LoggerMessage(EventId = 4009, Level = LogLevel.Debug, Message = "UI workflow iteration ({phase}) #{index}: {relativePath}.")]
    private partial void WorkflowProjectIteration(string phase, int index, string relativePath);

    /// <summary>
    /// Represents output from a git process invocation.
    /// </summary>
    private sealed record GitCommandResult
    {
        /// <summary>
        /// Gets a <see cref="int"/> indicating the process exit code.
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// Gets a <see cref="string"/> representing the captured standard output text.
        /// </summary>
        public string StandardOutput { get; }

        /// <summary>
        /// Gets a <see cref="string"/> representing the captured standard error text.
        /// </summary>
        public string StandardError { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitCommandResult"/> record.
        /// </summary>
        /// <param name="exitCode">The process exit code.</param>
        /// <param name="standardOutput">The captured standard output text.</param>
        /// <param name="standardError">The captured standard error text.</param>
        public GitCommandResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }
    }
}
