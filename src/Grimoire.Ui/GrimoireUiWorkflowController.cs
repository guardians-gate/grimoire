using Grimoire.Core;
using Grimoire.Core.Localization;
using Microsoft.Extensions.Localization;

namespace Grimoire.Ui;


/// <summary>
/// Represents UI-facing lore search output text plus result rows.
/// </summary>
/// <param name="Message">The message representing user-facing search status text.</param>
/// <param name="Results">The result list representing lore matches returned by the search operation.</param>
public sealed record LoreSearchUiResult(string Message, IReadOnlyList<LoreSearchResult> Results);

/// <summary>
/// Represents a project-file entry used by the UI tree and list views.
/// </summary>
public sealed record ProjectFileItem(
    string Name,
    string RelativePath,
    bool IsDirectory,
    string Kind,
    string Icon = "file",
    int Depth = 0,
    string ParentPath = "",
    bool IsSpecial = false)
{
    /// <summary>
    /// Gets a <see cref="string"/> representing the display path with directory suffix formatting.
    /// </summary>
    public string DisplayPath => IsDirectory ? $"{RelativePath}/" : RelativePath;

    /// <summary>
    /// Gets a <see cref="string"/> representing left-padding used when rendering tree indentation.
    /// </summary>
    public string TreeIndent => new(' ', Math.Max(0, Depth) * 2);
}

/// <summary>
/// Represents the contents and metadata for an opened project file.
/// </summary>
/// <param name="RelativePath">The relative path representing the opened file.</param>
/// <param name="Content">The content string representing the file body.</param>
/// <param name="Kind">The kind string representing the file category.</param>
/// <param name="KeywordHighlights">The highlights list representing contextual keyword matches for quick navigation.</param>
public sealed record ProjectFileDocument(string RelativePath, string Content, string Kind, IReadOnlyList<KeywordHighlight> KeywordHighlights);

/// <summary>
/// Represents a keyword highlight marker within opened document content.
/// </summary>
/// <param name="Keyword">The keyword representing the matched term.</param>
/// <param name="LineNumber">The line number indicating where the keyword appears.</param>
public sealed record KeywordHighlight(string Keyword, int LineNumber);

/// <summary>
/// Represents the result of importing an asset into a project.
/// </summary>
/// <param name="RelativePath">The relative path representing where the asset was imported.</param>
/// <param name="MarkdownReference">The markdown reference string representing how to embed the imported asset.</param>
public sealed record AssetImportResult(string RelativePath, string MarkdownReference);

/// <summary>
/// Represents a git status row for a project entry.
/// </summary>
/// <param name="Status">The status code representing git index/working-tree state.</param>
/// <param name="Path">The path representing the affected project entry.</param>
public sealed record GitStatusEntry(string Status, string Path);

/// <summary>
/// Represents a git history row used by UI commit history displays.
/// </summary>
/// <param name="Commit">The commit hash representing the revision identifier.</param>
/// <param name="Date">The date string representing commit timestamp text.</param>
/// <param name="Author">The author string representing commit author identity.</param>
/// <param name="Subject">The subject string representing the commit message headline.</param>
public sealed record GitHistoryEntry(string Commit, string Date, string Author, string Subject);

/// <summary>
/// Represents a reference-scan issue discovered in project content.
/// </summary>
/// <param name="Path">The relative path representing the file containing the issue.</param>
/// <param name="LineNumber">The line number indicating where the issue occurs.</param>
/// <param name="Reference">The reference token representing the unresolved include or macro.</param>
/// <param name="Message">The message representing issue details.</param>
public sealed record ReferenceScanIssue(string Path, int LineNumber, string Reference, string Message);

/// <summary>
/// Represents aggregate reference-scan metrics and issues.
/// </summary>
/// <param name="FilesScanned">The file count indicating how many files were scanned.</param>
/// <param name="Includes">The include count indicating how many include tokens were processed.</param>
/// <param name="Macros">The macro count indicating how many macro tokens were processed.</param>
/// <param name="Issues">The issue list representing detected reference problems.</param>
public sealed record ReferenceScanResult(int FilesScanned, int Includes, int Macros, IReadOnlyList<ReferenceScanIssue> Issues);

/// <summary>
/// Represents selectable build targets exposed by the UI workflow.
/// </summary>
public enum BuildTarget
{
    /// <summary>
    /// Indicates that all configured output targets should be built.
    /// </summary>
    All,
    /// <summary>
    /// Indicates that only HTML output should be built.
    /// </summary>
    Html,
    /// <summary>
    /// Indicates that only Foundry VTT output should be built.
    /// </summary>
    FoundryVtt,
    /// <summary>
    /// Indicates that only PDF output should be built.
    /// </summary>
    Pdf,
}

/// <summary>
/// Represents output paths for HTML, Foundry, and PDF build targets.
/// </summary>
/// <param name="HtmlPath">The path representing the HTML output location.</param>
/// <param name="FoundryPath">The path representing the Foundry output location.</param>
/// <param name="PdfPath">The path representing the PDF output location.</param>
public sealed record BuildOutputPaths(string HtmlPath, string FoundryPath, string PdfPath);

/// <summary>
/// Represents the UI orchestration layer that validates user input and delegates project operations to workflow services.
/// </summary>
/// <param name="service">The workflow service representing project operation implementations.</param>
/// <param name="envTokenProvider">The optional token provider representing fallback D&amp;D Beyond cobalt-token retrieval.</param>
/// <param name="localizer">The optional localizer representing UI-facing message resources.</param>
/// <param name="envPatreonKeyProvider">The optional provider representing fallback Patreon key retrieval.</param>
public sealed class GrimoireUiWorkflowController(
    IGrimoireUiWorkflowService service,
    Func<string?>? envTokenProvider = null,
    IStringLocalizer? localizer = null,
    Func<string?>? envPatreonKeyProvider = null)
{
    /// <summary>
    /// An <see cref="IGrimoireUiWorkflowService"/> representing the backend workflow implementation for UI operations.
    /// </summary>
    private readonly IGrimoireUiWorkflowService _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>
    /// A <see cref="Func{TResult}"/> representing environment-token resolution for D&amp;D Beyond operations.
    /// </summary>
    private readonly Func<string?> _envTokenProvider = envTokenProvider ?? (() => Environment.GetEnvironmentVariable("DND_BEYOND_COBALT"));

    /// <summary>
    /// A <see cref="Func{TResult}"/> representing environment-based Patreon key resolution for D&amp;D Beyond operations.
    /// </summary>
    private readonly Func<string?> _envPatreonKeyProvider = envPatreonKeyProvider ?? (() =>
        Environment.GetEnvironmentVariable("MRPRIMATE_PATREON")
        ?? Environment.GetEnvironmentVariable("DND_BEYOND_PATREON_KEY"));

    /// <summary>
    /// An <see cref="IStringLocalizer"/> representing localized UI message text.
    /// </summary>
    private readonly IStringLocalizer _localizer = localizer ?? new GrimoireLocalizationFactory().CreateDefault();

    /// <summary>
    /// Compiles a project for a single output path and returns a <see cref="Task{TResult}"/> representing a localized completion message.
    /// </summary>
    /// <param name="inputPath">The project input path representing source content root.</param>
    /// <param name="outputPath">The output path representing target artifact destination.</param>
    /// <param name="cancellationToken">The cancellation token indicating when compilation should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> message describing compilation output.</returns>
    public async Task<string> CompileAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException(Text("Ui:Compile:Validation"));
        }

        CompilationRequest request = await _service.CompileAsync(inputPath.Trim(), outputPath.Trim(), cancellationToken).ConfigureAwait(false);
        return Text("Ui:Compile:Result", request.Target, request.OutputPath);
    }

    /// <summary>
    /// Scaffolds a project with default overwrite behavior and returns a <see cref="Task{TResult}"/> representing a localized completion message.
    /// </summary>
    /// <param name="targetPath">The target path representing where the scaffold should be created.</param>
    /// <param name="cancellationToken">The cancellation token indicating when scaffolding should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> message describing scaffold output.</returns>
    public Task<string> ScaffoldAsync(string targetPath, CancellationToken cancellationToken)
    {
        return ScaffoldAsync(targetPath, overwriteExisting: false, cancellationToken);
    }

    /// <summary>
    /// Scaffolds a project with explicit overwrite behavior and returns a <see cref="Task{TResult}"/> representing a localized completion message.
    /// </summary>
    /// <param name="targetPath">The target path representing where the scaffold should be created.</param>
    /// <param name="overwriteExisting">The value indicating whether existing files should be overwritten.</param>
    /// <param name="cancellationToken">The cancellation token indicating when scaffolding should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> message describing scaffold output.</returns>
    public Task<string> ScaffoldAsync(string targetPath, bool overwriteExisting, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException(Text("Ui:Scaffold:Validation"));
        }

        string fullPath = _service.Scaffold(targetPath.Trim(), overwriteExisting);
        return Task.FromResult(Text("Ui:Scaffold:Result", fullPath));
    }

    /// <summary>
    /// Builds one or more targets and returns a <see cref="Task{TResult}"/> representing aggregated localized completion messages.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="target">The build target indicating which artifacts should be generated.</param>
    /// <param name="outputPaths">The output paths representing destination paths for each build target.</param>
    /// <param name="cancellationToken">The cancellation token indicating when build operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> containing one or more build result messages.</returns>
    public async Task<string> BuildAsync(string projectPath, BuildTarget target, BuildOutputPaths outputPaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputPaths);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException(Text("Ui:Compile:Validation"));
        }

        List<string> messages = [];
        if (target is BuildTarget.All or BuildTarget.Html)
        {
            if (string.IsNullOrWhiteSpace(outputPaths.HtmlPath))
            {
                throw new ArgumentException("HTML output path is required.");
            }

            CompilationRequest request = await _service.CompileAsync(projectPath.Trim(), outputPaths.HtmlPath.Trim(), cancellationToken).ConfigureAwait(false);
            messages.Add(Text("Ui:Compile:Result", request.Target, request.OutputPath));
        }

        if (target is BuildTarget.All or BuildTarget.FoundryVtt)
        {
            if (string.IsNullOrWhiteSpace(outputPaths.FoundryPath))
            {
                throw new ArgumentException("FoundryVTT output path is required.");
            }

            CompilationRequest request = await _service.CompileAsync(projectPath.Trim(), outputPaths.FoundryPath.Trim(), cancellationToken).ConfigureAwait(false);
            messages.Add(Text("Ui:Compile:Result", request.Target, request.OutputPath));
        }

        if (target is BuildTarget.All or BuildTarget.Pdf)
        {
            if (string.IsNullOrWhiteSpace(outputPaths.PdfPath))
            {
                throw new ArgumentException("PDF output path is required.");
            }

            CompilationRequest request = await _service.CompileAsync(projectPath.Trim(), outputPaths.PdfPath.Trim(), cancellationToken).ConfigureAwait(false);
            messages.Add(Text("Ui:Compile:Result", request.Target, request.OutputPath));
        }

        return string.Join(Environment.NewLine, messages);
    }

    /// <summary>
    /// Synchronizes D&amp;D Beyond content into a project and returns a <see cref="Task{TResult}"/> representing a localized summary message.
    /// </summary>
    /// <param name="outputBaseDirectory">The output base directory representing where synchronized files should be written.</param>
    /// <param name="cobaltToken">The optional cobalt token representing authentication credentials.</param>
    /// <param name="campaignIdText">The optional campaign identifier text representing party scope.</param>
    /// <param name="itemFilters">The optional item filters representing selected item names.</param>
    /// <param name="creatureFilters">The optional creature filters representing selected creature names.</param>
    /// <param name="spellFilters">The optional spell filters representing selected spell names.</param>
    /// <param name="characterSheetFilters">The optional character filters representing selected player sheet names.</param>
    /// <param name="includeHomebrew">The value indicating whether homebrew content should be included.</param>
    /// <param name="upgradeToMarkdown">The value indicating whether synced JSON should be upgraded to Markdown.</param>
    /// <param name="cancellationToken">The cancellation token indicating when sync operations should be aborted.</param>
    /// <param name="patreonKey">The optional Patreon key representing premium API access credentials.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> summary of synchronization results.</returns>
    public async Task<string> SyncDndbAsync(
        string outputBaseDirectory,
        string? cobaltToken,
        string? campaignIdText,
        string? itemFilters,
        string? creatureFilters,
        string? spellFilters,
        string? characterSheetFilters,
        bool includeHomebrew,
        bool upgradeToMarkdown,
        CancellationToken cancellationToken,
        string? patreonKey = null)
    {
        if (string.IsNullOrWhiteSpace(outputBaseDirectory))
        {
            throw new ArgumentException(Text("Ui:Dndb:OutputValidation"));
        }

        string token = string.IsNullOrWhiteSpace(cobaltToken)
            ? _envTokenProvider()?.Trim() ?? string.Empty
            : cobaltToken.Trim();
        string resolvedPatreonKey = string.IsNullOrWhiteSpace(patreonKey)
            ? _envPatreonKeyProvider()?.Trim() ?? string.Empty
            : patreonKey.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException(Text("Ui:Dndb:CobaltValidation"));
        }

        int? campaignId = null;
        if (!string.IsNullOrWhiteSpace(campaignIdText))
        {
            if (!int.TryParse(campaignIdText, out int parsedCampaign))
            {
                throw new ArgumentException(Text("Ui:Dndb:CampaignValidation"));
            }

            campaignId = parsedCampaign;
        }

        DndBeyondSyncOptions options = new(
            CobaltToken: token,
            OutputBaseDirectory: outputBaseDirectory.Trim(),
            IncludeHomebrew: includeHomebrew,
            CampaignId: campaignId,
            ItemNames: SplitFilters(itemFilters),
            CreatureNames: SplitFilters(creatureFilters),
            SpellNames: SplitFilters(spellFilters),
            CharacterSheetNames: SplitFilters(characterSheetFilters),
            UpgradeToMarkdown: upgradeToMarkdown,
            PatreonKey: resolvedPatreonKey);
        DndBeyondSyncSummary summary = await _service.SyncDndbAsync(options, cancellationToken).ConfigureAwait(false);
        string upgradeSummary = summary.UpgradedMarkdownFiles > 0
            ? Text("Ui:Dndb:UpgradeSuffix", summary.UpgradedMarkdownFiles)
            : string.Empty;
        return Text("Ui:Dndb:Result", summary.SourceCount, summary.Items, summary.Spells, summary.Creatures, summary.Players, upgradeSummary);
    }

    /// <summary>
    /// Executes lore search and returns a <see cref="LoreSearchUiResult"/> representing localized status text and matched results.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="query">The query string representing text to search for.</param>
    /// <param name="limit">The result limit indicating maximum matches to return.</param>
    /// <returns>A <see cref="LoreSearchUiResult"/> representing search status and results.</returns>
    public LoreSearchUiResult SearchLore(string projectPath, string query, int limit = 25)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException(Text("Ui:Lore:Validation"));
        }

        IReadOnlyList<LoreSearchResult> results = _service.SearchLore(projectPath.Trim(), query.Trim(), limit);
        return new(Text("Ui:Lore:Result", results.Count), results);
    }

    /// <summary>
    /// Opens a project tree and returns an <see cref="IReadOnlyList{T}"/> representing project entries for UI display.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <returns>An <see cref="IReadOnlyList{T}"/> representing project file and directory entries.</returns>
    public IReadOnlyList<ProjectFileItem> OpenProject(string projectPath)
    {
        return string.IsNullOrWhiteSpace(projectPath)
            ? throw new ArgumentException("Project path is required.")
            : _service.OpenProject(projectPath.Trim());
    }

    /// <summary>
    /// Opens a project file and returns a <see cref="Task{TResult}"/> representing the loaded document.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the file to open.</param>
    /// <param name="cancellationToken">The cancellation token indicating when file read should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="ProjectFileDocument"/> containing file content and metadata.</returns>
    public async Task<ProjectFileDocument> OpenFileAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Project path and file path are required.");
        }

        return await _service.ReadProjectFileAsync(projectPath.Trim(), relativePath.Trim(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves a project file and returns a <see cref="Task{TResult}"/> representing a completion message.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the file to save.</param>
    /// <param name="content">The content string representing persisted file text.</param>
    /// <param name="cancellationToken">The cancellation token indicating when file write should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> save confirmation message.</returns>
    public async Task<string> SaveFileAsync(string projectPath, string relativePath, string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Project path and file path are required.");
        }

        await _service.SaveProjectFileAsync(projectPath.Trim(), relativePath.Trim(), content, cancellationToken).ConfigureAwait(false);
        return $"Saved {relativePath.Trim()}";
    }

    /// <summary>
    /// Renders preview HTML for a project file and returns a <see cref="Task{TResult}"/> representing rendered preview content.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the file to preview.</param>
    /// <param name="cancellationToken">The cancellation token indicating when preview rendering should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="SourcebookPreviewResult"/> with HTML and link targets.</returns>
    public async Task<SourcebookPreviewResult> RenderPreviewAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Project path and file path are required.");
        }

        return await _service.RenderPreviewAsync(projectPath.Trim(), relativePath.Trim(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports an external asset into the project and returns a <see cref="Task{TResult}"/> representing an import summary message.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="sourceAssetPath">The source asset path representing the external file to copy.</param>
    /// <param name="targetSubdirectory">The optional target subdirectory representing destination folder under the project.</param>
    /// <param name="cancellationToken">The cancellation token indicating when import operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> import summary message.</returns>
    public async Task<string> ImportAssetAsync(string projectPath, string sourceAssetPath, string? targetSubdirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(sourceAssetPath))
        {
            throw new ArgumentException("Project path and source asset path are required.");
        }

        AssetImportResult result = await _service.ImportAssetAsync(projectPath.Trim(), sourceAssetPath.Trim(), targetSubdirectory, cancellationToken).ConfigureAwait(false);
        return $"Imported asset: {result.RelativePath} ({result.MarkdownReference})";
    }

    /// <summary>
    /// Exports a project to a zip archive and returns a <see cref="Task{TResult}"/> representing a completion message.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="zipPath">The output zip path representing archive destination.</param>
    /// <param name="cancellationToken">The cancellation token indicating when export operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> export summary message.</returns>
    public async Task<string> ExportZipAsync(string projectPath, string zipPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(zipPath))
        {
            throw new ArgumentException("Project path and zip path are required.");
        }

        string exportedPath = await _service.ExportZipAsync(projectPath.Trim(), zipPath.Trim(), cancellationToken).ConfigureAwait(false);
        return $"Exported zip: {exportedPath}";
    }

    /// <summary>
    /// Imports a project zip archive and returns a <see cref="Task{TResult}"/> representing a completion message.
    /// </summary>
    /// <param name="zipPath">The zip path representing source archive input.</param>
    /// <param name="targetDirectory">The target directory representing extraction destination.</param>
    /// <param name="cancellationToken">The cancellation token indicating when import operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> import summary message.</returns>
    public async Task<string> ImportZipAsync(string zipPath, string targetDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Zip path and target directory are required.");
        }

        string importedPath = await _service.ImportZipAsync(zipPath.Trim(), targetDirectory.Trim(), cancellationToken).ConfigureAwait(false);
        return $"Imported zip: {importedPath}";
    }

    /// <summary>
    /// Retrieves git status entries and returns a <see cref="Task{TResult}"/> representing repository status rows.
    /// </summary>
    /// <param name="projectPath">The project path representing repository root.</param>
    /// <param name="cancellationToken">The cancellation token indicating when status retrieval should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing an <see cref="IReadOnlyList{T}"/> of git status entries.</returns>
    public Task<IReadOnlyList<GitStatusEntry>> GetGitStatusAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Project path is required.");
        }

        return _service.GetGitStatusAsync(projectPath.Trim(), cancellationToken);
    }

    /// <summary>
    /// Retrieves git commit history and returns a <see cref="Task{TResult}"/> representing history rows.
    /// </summary>
    /// <param name="projectPath">The project path representing repository root.</param>
    /// <param name="limit">The limit indicating maximum history entries to return.</param>
    /// <param name="cancellationToken">The cancellation token indicating when history retrieval should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing an <see cref="IReadOnlyList{T}"/> of git history entries.</returns>
    public Task<IReadOnlyList<GitHistoryEntry>> GetGitHistoryAsync(string projectPath, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Project path is required.");
        }

        return _service.GetGitHistoryAsync(projectPath.Trim(), limit, cancellationToken);
    }

    /// <summary>
    /// Commits repository changes and returns a <see cref="Task{TResult}"/> representing a completion message.
    /// </summary>
    /// <param name="projectPath">The project path representing repository root.</param>
    /// <param name="message">The commit message representing revision summary text.</param>
    /// <param name="cancellationToken">The cancellation token indicating when commit operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> commit completion message.</returns>
    public async Task<string> CommitGitAsync(string projectPath, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Project path and commit message are required.");
        }

        await _service.CommitGitAsync(projectPath.Trim(), message.Trim(), cancellationToken).ConfigureAwait(false);
        return "Git commit complete.";
    }

    /// <summary>
    /// Scans project references and returns a <see cref="Task{TResult}"/> representing scan metrics and issues.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="cancellationToken">The cancellation token indicating when scanning should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="ReferenceScanResult"/> with aggregate scan results.</returns>
    public Task<ReferenceScanResult> ScanReferencesAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Project path is required.");
        }

        return _service.ScanReferencesAsync(projectPath.Trim(), cancellationToken);
    }

    /// <summary>
    /// Moves a project entry and returns a <see cref="Task{TResult}"/> representing a completion message.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the entry to move.</param>
    /// <param name="targetDirectory">The target directory representing destination folder under the project.</param>
    /// <param name="cancellationToken">The cancellation token indicating when move operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> move completion message.</returns>
    public async Task<string> MoveProjectEntryAsync(string projectPath, string relativePath, string? targetDirectory, CancellationToken cancellationToken)
    {
        ValidateProjectEntryAction(projectPath, relativePath);
        await _service.MoveProjectEntryAsync(projectPath.Trim(), relativePath.Trim(), targetDirectory?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return $"Moved {relativePath.Trim()}";
    }

    /// <summary>
    /// Copies a project entry and returns a <see cref="Task{TResult}"/> representing a completion message with destination path.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the entry to copy.</param>
    /// <param name="targetDirectory">The target directory representing destination folder under the project.</param>
    /// <param name="cancellationToken">The cancellation token indicating when copy operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> copy completion message.</returns>
    public async Task<string> CopyProjectEntryAsync(string projectPath, string relativePath, string? targetDirectory, CancellationToken cancellationToken)
    {
        ValidateProjectEntryAction(projectPath, relativePath);
        string copiedPath = await _service.CopyProjectEntryAsync(projectPath.Trim(), relativePath.Trim(), targetDirectory?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return $"Copied to {copiedPath}";
    }

    /// <summary>
    /// Deletes a project entry and returns a <see cref="Task{TResult}"/> representing a completion message.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the entry to delete.</param>
    /// <param name="cancellationToken">The cancellation token indicating when delete operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> delete completion message.</returns>
    public async Task<string> DeleteProjectEntryAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
    {
        ValidateProjectEntryAction(projectPath, relativePath);
        await _service.DeleteProjectEntryAsync(projectPath.Trim(), relativePath.Trim(), cancellationToken).ConfigureAwait(false);
        return $"Deleted {relativePath.Trim()}";
    }

    /// <summary>
    /// Renames a project entry and returns a <see cref="Task{TResult}"/> representing a completion message with the new path.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the entry to rename.</param>
    /// <param name="newName">The new name representing the replacement filename.</param>
    /// <param name="cancellationToken">The cancellation token indicating when rename operations should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="string"/> rename completion message.</returns>
    public async Task<string> RenameProjectEntryAsync(string projectPath, string relativePath, string newName, CancellationToken cancellationToken)
    {
        ValidateProjectEntryAction(projectPath, relativePath);
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("New name is required.");
        }

        string renamedPath = await _service.RenameProjectEntryAsync(projectPath.Trim(), relativePath.Trim(), newName.Trim(), cancellationToken).ConfigureAwait(false);
        return $"Renamed to {renamedPath}";
    }

    /// <summary>
    /// Resolves a project entry to an absolute path and returns a <see cref="string"/> representing the full filesystem path.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the entry to resolve.</param>
    /// <returns>A <see cref="string"/> representing the absolute filesystem path for the entry.</returns>
    public string GetProjectEntryFullPath(string projectPath, string relativePath)
    {
        ValidateProjectEntryAction(projectPath, relativePath);
        return _service.GetProjectEntryFullPath(projectPath.Trim(), relativePath.Trim());
    }

    /// <summary>
    /// Validates project-entry action arguments and returns <see langword="void"/>.
    /// </summary>
    /// <param name="projectPath">The project path representing source content root.</param>
    /// <param name="relativePath">The relative path representing the target entry.</param>
    private static void ValidateProjectEntryAction(string projectPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Project path and entry path are required.");
        }
    }

    /// <summary>
    /// Splits filter text into unique entries and returns a <see cref="string"/> array representing normalized filter tokens.
    /// </summary>
    /// <param name="filterText">The filter text representing comma, semicolon, or newline-delimited tokens.</param>
    /// <returns>A <see cref="string"/> array representing normalized distinct filter values.</returns>
    private static string[] SplitFilters(string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return [];
        }

        return
        [
            .. filterText
                .Split([',', ';', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Resolves a localized message by key and returns a <see cref="string"/> representing message text.
    /// </summary>
    /// <param name="key">The localization key representing the requested message template.</param>
    /// <returns>A <see cref="string"/> representing localized message text.</returns>
    private string Text(string key)
    {
        return _localizer[key].Value;
    }

    /// <summary>
    /// Resolves and formats a localized message and returns a <see cref="string"/> representing formatted message text.
    /// </summary>
    /// <param name="key">The localization key representing the requested message template.</param>
    /// <param name="arguments">The formatting arguments representing template substitution values.</param>
    /// <returns>A <see cref="string"/> representing formatted localized message text.</returns>
    private string Text(string key, params object[] arguments)
    {
        return _localizer[key, arguments].Value;
    }
}
