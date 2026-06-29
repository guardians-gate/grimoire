using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Grimoire.Core;

namespace Grimoire.Ui.Tests;

/// <summary>
/// Represents integration tests that exercise UI workflow controller operations against real core services.
/// </summary>
public sealed class GrimoireUiWorkflowIntegrationTests
{
    /// <summary>
    /// Verifies scaffold, compile, and lore search workflows run end-to-end against real core components and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task ScaffoldCompileAndSearchRunAgainstRealCoreAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-ui-int");
        GrimoireUiWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");

        string projectRoot = Path.Combine(workspace.RootPath, "project");
        string scaffoldMessage = await controller.ScaffoldAsync(projectRoot, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("Scaffold complete:", scaffoldMessage, StringComparison.Ordinal);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        string compileMessage = await controller.CompileAsync(projectRoot, outputDirectory, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("Compile finished:", compileMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "index.html")));

        LoreSearchUiResult search = controller.SearchLore(projectRoot, "sourcebook");
        Assert.NotEmpty(search.Results);
        Assert.All(search.Results, static result => Assert.True(result.LineNumber >= 1));
        Assert.Contains("Lore search complete:", search.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies D&amp;D Beyond sync stores downloaded JSON files when driven by a stubbed API handler and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SyncDndbAsyncStoresDownloadedJsonFromStubbedApiAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-ui-dndb");
        using StubDndbHandler handler = new();
        GrimoireUiWorkflowService service = new(() => new HttpClient(handler), new Uri("https://unit.test", UriKind.Absolute));
        GrimoireUiWorkflowController controller = new(service, () => "env-token");

        string message = await controller
            .SyncDndbAsync(workspace.RootPath, "token", "123", null, null, null, null, includeHomebrew: true, upgradeToMarkdown: false, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Contains("DDB sync complete:", message, StringComparison.Ordinal);
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "items"), "*.json").Length > 0);
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "spells"), "*.json").Length > 0);
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "creatures"), "*.json").Length > 0);
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "players"), "*.json").Length > 0);
    }

    /// <summary>
    /// Verifies workflow compile routing supports Foundry export targets and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task WorkflowServiceCompileAsyncCoversFoundryAndPdfTargetsAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-ui-targets");
        string projectRoot = Path.Combine(workspace.RootPath, "project");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "content"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "content", "001.md"), "# Entry\nBody").ConfigureAwait(true);

        GrimoireUiWorkflowService service = new();
        CompilationRequest foundry = await service.CompileAsync(projectRoot, Path.Combine(workspace.RootPath, "book.db"), CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(ExportTarget.FoundryDb, foundry.Target);
        Assert.True(File.Exists(foundry.OutputPath));
    }

    /// <summary>
    /// Verifies IDE-style workspace operations and preview rendering against real project files and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task IdeWorkspaceOperationsUseRealProjectFilesAndCompilerPreviewAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-ui-ide");
        string projectRoot = Path.Combine(workspace.RootPath, "project");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "content"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "snippets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "settings"));
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "settings", "html.yml"),
            """
            compiler:
              autoLink: true
              screen:
                columns: 2
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "snippets", "TEMPLATE.md"), "# {{title}}\n\n{{content}}\n").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "snippets", "goblin.json"), """{"title":"Goblin","content":"Goblin lore."}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(
                Path.Combine(projectRoot, "content", "001.md"),
                """
                # Chapter

                Goblin waits here.

                ![Goblin](../snippets/goblin.json)
                """)
            .ConfigureAwait(true);

        GrimoireUiWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");

        IReadOnlyList<ProjectFileItem> files = controller.OpenProject(projectRoot);
        Assert.True(Directory.Exists(Path.Combine(projectRoot, ".caches")));
        Assert.True(Directory.Exists(Path.Combine(projectRoot, ".caches", "generated")));
        Assert.True(File.Exists(Path.Combine(projectRoot, ".caches", "hashes.json")));
        Assert.True(File.Exists(Path.Combine(projectRoot, ".caches", "topics.json")));
        Assert.True(File.Exists(Path.Combine(projectRoot, ".caches", "state.json")));
        Assert.Contains(files, static file => file.RelativePath == "content" && file.Kind == "directory-content" && file.Icon == "book");
        Assert.Contains(files, static file => file.RelativePath == "settings/html.yml" && file.Kind == "settings" && file.Icon == "gear");
        Assert.Contains(files, static file => file.RelativePath == "content/001.md" && file.Kind == "content-markdown" && file.Icon == "page");
        Assert.Contains(files, static file => file.RelativePath == "snippets/TEMPLATE.md" && file.Kind == "template" && file.Icon == "template");

        ProjectFileDocument document = await controller.OpenFileAsync(projectRoot, "content/001.md", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("Goblin waits", document.Content, StringComparison.Ordinal);
        Assert.Contains(document.KeywordHighlights, static highlight => highlight.Keyword == "goblin");

        SourcebookPreviewResult preview = await controller.RenderPreviewAsync(projectRoot, "content/001.md", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("lore.", preview.Html, StringComparison.Ordinal);
        Assert.Contains("class=\"infobox\"", preview.Html, StringComparison.Ordinal);
        Assert.Contains("grimoire://open?path=snippets%2Fgoblin.json", preview.Html, StringComparison.Ordinal);

        LoreSearchUiResult search = controller.SearchLore(projectRoot, "Goblin waits");
        LoreSearchResult result = Assert.Single(search.Results);
        Assert.Equal(3, result.LineNumber);

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "content", "broken.md"), "# Broken\n\n![Missing](../snippets/missing.json)\n").ConfigureAwait(true);
        ReferenceScanResult scan = await controller.ScanReferencesAsync(projectRoot, CancellationToken.None).ConfigureAwait(true);
        ReferenceScanIssue issue = Assert.Single(scan.Issues);
        Assert.Equal(3, issue.LineNumber);

        string externalAsset = Path.Combine(workspace.RootPath, "portrait.png");
        await File.WriteAllBytesAsync(externalAsset, [1, 2, 3]).ConfigureAwait(true);
        string assetMessage = await controller.ImportAssetAsync(projectRoot, externalAsset, "assets/images", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("assets/images/portrait.png", assetMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(projectRoot, "assets", "images", "portrait.png")));

        string zipPath = Path.Combine(workspace.RootPath, "project.zip");
        string zipMessage = await controller.ExportZipAsync(projectRoot, zipPath, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains(zipPath, zipMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(zipPath));

        string importedPath = Path.Combine(workspace.RootPath, "imported");
        await controller.ImportZipAsync(zipPath, importedPath, CancellationToken.None).ConfigureAwait(true);
        Assert.True(File.Exists(Path.Combine(importedPath, "content", "001.md")));

        string saveMessage = await controller.SaveFileAsync(projectRoot, "content/002.md", "# Saved\n", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("Saved content/002.md", saveMessage);
        Assert.True(File.Exists(Path.Combine(projectRoot, "content", "002.md")));

        string copyMessage = await controller.CopyProjectEntryAsync(projectRoot, "content/002.md", "snippets", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("snippets/002.md", copyMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(projectRoot, "snippets", "002.md")));

        string renameMessage = await controller.RenameProjectEntryAsync(projectRoot, "snippets/002.md", "renamed.md", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("snippets/renamed.md", renameMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(projectRoot, "snippets", "renamed.md")));

        string moveMessage = await controller.MoveProjectEntryAsync(projectRoot, "snippets/renamed.md", "content", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("Moved snippets/renamed.md", moveMessage, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(projectRoot, "content", "renamed.md")));

        string deleteMessage = await controller.DeleteProjectEntryAsync(projectRoot, "content/renamed.md", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("Deleted content/renamed.md", deleteMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(projectRoot, "content", "renamed.md")));
    }

    /// <summary>
    /// Verifies auto-linked preview mentions navigate to concrete project entity files and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task PreviewAutoLinkedEntityMentionsNavigateToRealProjectEntityFilesAsync()
    {
        string projectRoot = Path.Combine(FindRepositoryRoot(), "projects", "nr");
        GrimoireUiWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");

        SourcebookPreviewResult preview = await controller
            .RenderPreviewAsync(projectRoot, "README.md", CancellationToken.None)
            .ConfigureAwait(true);

        const string expectedPath = "players/141291255-BRUTUS.json";
        const string expectedHref = "grimoire://open?path=players%2F141291255-BRUTUS.json";
        Assert.True(
            preview.Html.Contains(expectedHref, StringComparison.Ordinal) ||
            preview.LinkTargets.Values.Any(path => string.Equals(path, expectedPath, StringComparison.Ordinal)),
            $"Expected preview HTML to contain '{expectedHref}' or LinkTargets to contain '{expectedPath}'.");
    }

    /// <summary>
    /// Verifies preview rendering stays within cold and warm performance budgets for the Cormanthor chapter and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncRendersCormanthorChaosWithinPerformanceBudgetAsync()
    {
        string projectRoot = Path.Combine(FindRepositoryRoot(), "projects", "nr");
        string relativeChapterPath = ResolveCormanthorChapterPath(projectRoot);
        GrimoireUiWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");

        Stopwatch coldTimer = Stopwatch.StartNew();
        SourcebookPreviewResult coldPreview = await controller
            .RenderPreviewAsync(projectRoot, relativeChapterPath, CancellationToken.None)
            .ConfigureAwait(true);
        coldTimer.Stop();

        Stopwatch warmTimer = Stopwatch.StartNew();
        SourcebookPreviewResult warmPreview = await controller
            .RenderPreviewAsync(projectRoot, relativeChapterPath, CancellationToken.None)
            .ConfigureAwait(true);
        warmTimer.Stop();

        Assert.Contains("Cormanthor", coldPreview.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cormanthor", warmPreview.Html, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            coldTimer.Elapsed < TimeSpan.FromSeconds(30),
            $"Cold render exceeded budget: {coldTimer.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.True(
            warmTimer.Elapsed < TimeSpan.FromSeconds(10),
            $"Warm render exceeded budget: {warmTimer.Elapsed.TotalMilliseconds:F0} ms.");
    }

    /// <summary>
    /// Resolves the Cormanthor chapter path and returns a <see cref="string"/> representing the repository-relative markdown file path.
    /// </summary>
    /// <param name="projectRoot">The project root path representing the source tree that contains chapter content.</param>
    /// <returns>A <see cref="string"/> representing the relative chapter path using forward slashes.</returns>
    private static string ResolveCormanthorChapterPath(string projectRoot)
    {
        string contentRoot = Path.Combine(projectRoot, "content");
        string[] candidates = Directory.GetFiles(contentRoot, "*cormanthor_chaos.md", SearchOption.TopDirectoryOnly);
        if (candidates.Length == 0)
        {
            throw new FileNotFoundException($"No cormanthor chapter was found under {contentRoot}.");
        }

        Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);
        return Path.GetRelativePath(projectRoot, candidates[0])
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Locates the repository root for integration fixtures and returns a <see cref="string"/> representing the discovered root path.
    /// </summary>
    /// <returns>A <see cref="string"/> representing the repository root containing the expected fixture project.</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "projects", "nr")) &&
                File.Exists(Path.Combine(current.FullName, "src", "Grimoire.Core", "SourcebookCompiler.cs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing projects/nr.");
    }

    /// <summary>
    /// Represents an HTTP message handler that serves deterministic D&amp;D Beyond proxy responses for integration tests.
    /// </summary>
    private sealed class StubDndbHandler : HttpMessageHandler
    {
        /// <summary>
        /// Generates a stubbed HTTP response and returns a <see cref="Task{TResult}"/> representing the synthesized proxy payload.
        /// </summary>
        /// <param name="request">The HTTP request representing the proxy call under test.</param>
        /// <param name="cancellationToken">The cancellation token indicating when request handling should be aborted.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing an <see cref="HttpResponseMessage"/> with deterministic JSON content.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[{"id":1,"name":"Longsword"}]}}""",
                "/proxy/class/spells" => """{"success":true,"data":[{"definition":{"id":2,"name":"Fireball","isHomebrew":false}}]}""",
                "/proxy/monster" => """{"success":true,"data":[{"id":3,"name":"Goblin"}]}""",
                "/proxy/party/123/characters" => """{"success":true,"data":{"characters":[{"characterId":99,"characterName":"Rogue"}]}}""",
                "/proxy/v5/character" => """{"success":true,"data":{"ddb":{"character":{"id":99,"name":"Rogue"}}}}""",
                _ => """{"success":true,"data":[]}""",
            };
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Represents a disposable temporary workspace used by workflow integration tests.
    /// </summary>
    private sealed class TempWorkspace : IDisposable
    {
        /// <summary>
        /// A <see cref="bool"/> indicating whether this workspace fixture has already been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a temporary workspace rooted at a specific directory path.
        /// </summary>
        /// <param name="rootPath">The root path representing the temporary workspace location.</param>
        private TempWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the root directory for the temporary workspace fixture.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Creates a temporary workspace and returns a <see cref="TempWorkspace"/> representing the created fixture root.
        /// </summary>
        /// <param name="prefix">The directory-name prefix representing the fixture identity.</param>
        /// <returns>A <see cref="TempWorkspace"/> representing the created temporary workspace.</returns>
        public static TempWorkspace Create(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        /// <summary>
        /// Deletes the temporary workspace when possible and returns <see langword="void"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Directory.Exists(RootPath))
            {
                try
                {
                    Directory.Delete(RootPath, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
