using Grimoire.Core;
using Microsoft.Maui.Graphics;
using System.Reflection;

namespace Grimoire.Ui.Tests;

/// <summary>
/// Contains tests for <see cref="GrimoireUiWorkflowController"/> and related UI workflow helpers.
/// </summary>
public sealed class GrimoireUiWorkflowControllerTests
{
    /// <summary>
    /// Verifies that compile requests reject missing input and output paths.
    /// </summary>
    [Fact]
    public async Task CompileAsyncValidatesRequiredInput()
    {
        FakeWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CompileAsync("", "out", CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CompileAsync("in", " ", CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies that successful compile requests return a formatted completion message.
    /// </summary>
    [Fact]
    public async Task CompileAsyncReturnsFormattedResult()
    {
        FakeWorkflowService service = new()
        {
            CompileRequest = new CompilationRequest("input", InputSourceKind.Directory, "c:\\out\\index.html", ExportTarget.Website),
        };
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        string message = await controller.CompileAsync("input", "output", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("Compile finished: Website -> c:\\out\\index.html", message);
    }

    /// <summary>
    /// Verifies that scaffold requests reject a missing target path.
    /// </summary>
    [Fact]
    public async Task ScaffoldAsyncValidatesRequiredPath()
    {
        FakeWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        await Assert.ThrowsAsync<ArgumentException>(() => controller.ScaffoldAsync(" ", CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies that scaffold requests return the expected completion message.
    /// </summary>
    [Fact]
    public async Task ScaffoldAsyncReturnsCompletedMessage()
    {
        FakeWorkflowService service = new()
        {
            ScaffoldResult = "C:\\project",
        };
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        string message = await controller.ScaffoldAsync("C:\\project", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("Scaffold complete: C:\\project", message);
    }

    /// <summary>
    /// Verifies that scaffold requests honor cancellation.
    /// </summary>
    [Fact]
    public async Task ScaffoldAsyncHonorsCancellation()
    {
        FakeWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.ScaffoldAsync("path", cts.Token)).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies that D&amp;D Beyond sync uses entry token and campaign values when provided.
    /// </summary>
    [Fact]
    public async Task SyncDndbAsyncUsesEntryTokenAndCampaign()
    {
        FakeWorkflowService service = new()
        {
            DndSummary = new DndBeyondSyncSummary(1, 2, 3, 4, 5),
        };
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        string message = await controller
            .SyncDndbAsync("out", "entry-token", "42", "Longsword, Lantern", "Goblin", "Fireball", "Rogue", includeHomebrew: true, upgradeToMarkdown: true, CancellationToken.None)
            .ConfigureAwait(true);
        Assert.Equal("DDB sync complete: sources=5, items=1, spells=2, creatures=3, players=4", message);
        Assert.NotNull(service.ReceivedDndOptions);
        Assert.Equal("entry-token", service.ReceivedDndOptions!.CobaltToken);
        Assert.Equal(42, service.ReceivedDndOptions.CampaignId);
        Assert.True(service.ReceivedDndOptions.IncludeHomebrew);
        Assert.True(service.ReceivedDndOptions.UpgradeToMarkdown);
        Assert.Equal(["Longsword", "Lantern"], service.ReceivedDndOptions.ItemNames);
        Assert.Equal(["Goblin"], service.ReceivedDndOptions.CreatureNames);
        Assert.Equal(["Fireball"], service.ReceivedDndOptions.SpellNames);
        Assert.Equal(["Rogue"], service.ReceivedDndOptions.CharacterSheetNames);
    }

    /// <summary>
    /// Verifies that D&amp;D Beyond sync falls back to the environment token when no entry token is provided.
    /// </summary>
    [Fact]
    public async Task SyncDndbAsyncUsesEnvironmentTokenWhenEntryTokenMissing()
    {
        FakeWorkflowService service = new()
        {
            DndSummary = new DndBeyondSyncSummary(0, 0, 0, 0, 0),
        };
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        await controller.SyncDndbAsync("out", null, null, null, null, null, null, includeHomebrew: false, upgradeToMarkdown: false, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("env-token", service.ReceivedDndOptions?.CobaltToken);
    }

    /// <summary>
    /// Verifies that Patreon key resolution prefers entry values over environment values.
    /// </summary>
    [Fact]
    public async Task SyncDndbAsyncUsesPatreonKeyFromEntryOrEnvironment()
    {
        FakeWorkflowService service = new()
        {
            DndSummary = new DndBeyondSyncSummary(0, 0, 0, 0, 0),
        };
        GrimoireUiWorkflowController controllerWithEnvKey = new(service, () => "env-token", envPatreonKeyProvider: () => "env-patreon-key");
        await controllerWithEnvKey.SyncDndbAsync("out", "entry-token", null, null, null, null, null, includeHomebrew: false, upgradeToMarkdown: false, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("env-patreon-key", service.ReceivedDndOptions?.PatreonKey);

        await controllerWithEnvKey.SyncDndbAsync("out", "entry-token", null, null, null, null, null, includeHomebrew: false, upgradeToMarkdown: false, CancellationToken.None, "entry-patreon-key").ConfigureAwait(true);
        Assert.Equal("entry-patreon-key", service.ReceivedDndOptions?.PatreonKey);
    }

    /// <summary>
    /// Verifies that the default environment token provider is used when no override is supplied.
    /// </summary>
    [Fact]
    public async Task SyncDndbAsyncUsesDefaultEnvironmentProviderWhenNotOverridden()
    {
        string? previous = Environment.GetEnvironmentVariable("DND_BEYOND_COBALT");
        try
        {
            Environment.SetEnvironmentVariable("DND_BEYOND_COBALT", "ambient-token");
            FakeWorkflowService service = new()
            {
                DndSummary = new DndBeyondSyncSummary(0, 0, 0, 0, 0),
            };
            GrimoireUiWorkflowController controller = new(service);
            await controller.SyncDndbAsync("out", null, null, null, null, null, null, includeHomebrew: false, upgradeToMarkdown: false, CancellationToken.None).ConfigureAwait(true);
            Assert.Equal("ambient-token", service.ReceivedDndOptions?.CobaltToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DND_BEYOND_COBALT", previous);
        }
    }

    /// <summary>
    /// Verifies that D&amp;D Beyond sync validates required paths, tokens, and campaign identifiers.
    /// </summary>
    [Fact]
    public async Task SyncDndbAsyncValidatesInputs()
    {
        FakeWorkflowService service = new();
        GrimoireUiWorkflowController controllerNoToken = new(service, () => "");
        await Assert.ThrowsAsync<ArgumentException>(() => controllerNoToken.SyncDndbAsync("", "entry", null, null, null, null, null, false, false, CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(() => controllerNoToken.SyncDndbAsync("out", "", null, null, null, null, null, false, false, CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(() => controllerNoToken.SyncDndbAsync("out", "token", "abc", null, null, null, null, false, false, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies that lore search validates project path and query inputs.
    /// </summary>
    [Fact]
    public void SearchLoreValidatesInput()
    {
        FakeWorkflowService service = new();
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        Assert.Throws<ArgumentException>(() => controller.SearchLore("", "query"));
        Assert.Throws<ArgumentException>(() => controller.SearchLore("project", " "));
    }

    /// <summary>
    /// Verifies that lore search returns both a completion message and result entries.
    /// </summary>
    [Fact]
    public void SearchLoreReturnsMessageAndResults()
    {
        FakeWorkflowService service = new()
        {
            SearchResults = new List<LoreSearchResult> { new("content/001.md", "Entry", "excerpt") },
        };
        GrimoireUiWorkflowController controller = new(service, () => "env-token");
        LoreSearchUiResult result = controller.SearchLore("project", "query");
        Assert.Equal("Lore search complete: 1 results.", result.Message);
        Assert.Single(result.Results);
    }

    /// <summary>
    /// Verifies that IDE workflow operations validate input and delegate to the service.
    /// </summary>
    [Fact]
    public async Task IdeOperationsValidateAndDelegateAsync()
    {
        FakeWorkflowService service = new()
        {
            ProjectItems = [new("001.md", "content/001.md", IsDirectory: false, "content")],
            FileDocument = new("content/001.md", "# Entry", "content", [new("Entry", 1)]),
            PreviewResult = new("<html>Entry</html>", new Dictionary<string, string> { ["index.html#content-001-md"] = "content/001.md" }),
            ScanResult = new(1, 2, 3, [new("content/001.md", 4, "../missing.json", "Missing reference target")]),
        };
        GrimoireUiWorkflowController controller = new(service, () => "env-token");

        Assert.Single(controller.OpenProject("project"));
        ProjectFileDocument document = await controller.OpenFileAsync("project", "content/001.md", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("content/001.md", document.RelativePath);
        string saveMessage = await controller.SaveFileAsync("project", "content/001.md", "# Entry", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("Saved content/001.md", saveMessage);
        SourcebookPreviewResult preview = await controller.RenderPreviewAsync("project", "content/001.md", CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("Entry", preview.Html, StringComparison.Ordinal);
        string assetMessage = await controller.ImportAssetAsync("project", "asset.png", null, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("assets/test.png", assetMessage, StringComparison.Ordinal);
        string exportMessage = await controller.ExportZipAsync("project", "project.zip", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("Exported zip: project.zip", exportMessage);
        string importMessage = await controller.ImportZipAsync("project.zip", "project", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("Imported zip: project", importMessage);
        Assert.Single(await controller.GetGitStatusAsync("project", CancellationToken.None).ConfigureAwait(true));
        Assert.Single(await controller.GetGitHistoryAsync("project", 5, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("Git commit complete.", await controller.CommitGitAsync("project", "message", CancellationToken.None).ConfigureAwait(true));
        ReferenceScanResult scan = await controller.ScanReferencesAsync("project", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, scan.FilesScanned);

        Assert.Throws<ArgumentException>(() => controller.OpenProject(""));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.OpenFileAsync("project", "", CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.SaveFileAsync("", "file", "", CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CommitGitAsync("project", "", CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies that controller construction requires a non-null service dependency.
    /// </summary>
    [Fact]
    public void ConstructorValidatesService()
    {
        Assert.Throws<ArgumentNullException>(() => new GrimoireUiWorkflowController(null!));
    }

    /// <summary>
    /// Verifies that preview base URLs point to the rendered source directory as a file URI.
    /// </summary>
    [Fact]
    public void PreviewBaseUrlTargetsSourceDirectoryAsFileUri()
    {
        MethodInfo? method = typeof(MainPage).GetMethod("BuildPreviewBaseUrl", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        string projectRoot = Path.Combine(Path.GetTempPath(), $"grimoire-ui-base-{Guid.NewGuid():N}");
        string result = (string)method!.Invoke(null, [projectRoot, "content/001.md"])!;

        string expectedDirectory = Path.GetFullPath(Path.Combine(projectRoot, "content"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string expected = new Uri(expectedDirectory, UriKind.Absolute).AbsoluteUri;
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that preview navigation includes relative and absolute hyperlink key forms.
    /// </summary>
    [Fact]
    public void PreviewNavigationTargetMapIncludesRelativeAndAbsoluteLinkForms()
    {
        MethodInfo? buildMethod = typeof(MainPage).GetMethod("BuildPreviewNavigationTargets", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? normalizeMethod = typeof(MainPage).GetMethod("NormalizePreviewNavigationKey", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildMethod);
        Assert.NotNull(normalizeMethod);

        Dictionary<string, string> links = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chapter-appendix-reference-dictionary.html#dict-ref-goblin"] = "snippets/goblin.json",
        };
        string baseDirectory = Path.Combine(Path.GetTempPath(), $"grimoire-ui-nav-{Guid.NewGuid():N}", "content");
        string baseUrl = new Uri(baseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, UriKind.Absolute).AbsoluteUri;

        Dictionary<string, string> targets = (Dictionary<string, string>)buildMethod!.Invoke(null, [links, baseUrl])!;
        string absoluteHref = new Uri(new Uri(baseUrl), "chapter-appendix-reference-dictionary.html#dict-ref-goblin").AbsoluteUri;
        string normalizedAbsolute = (string)normalizeMethod!.Invoke(null, [absoluteHref])!;
        string normalizedRelative = (string)normalizeMethod!.Invoke(null, ["chapter-appendix-reference-dictionary.html#dict-ref-goblin"])!;

        Assert.Equal("snippets/goblin.json", targets[normalizedRelative]);
        Assert.Equal("snippets/goblin.json", targets[normalizedAbsolute]);
    }

    /// <summary>
    /// Verifies that preview navigation supports auto-link and grimoire URL forms.
    /// </summary>
    [Fact]
    public void PreviewNavigationUnderstandsAutoLinkAndGrimoireUrlForms()
    {
        MethodInfo? buildMethod = typeof(MainPage).GetMethod("BuildPreviewNavigationTargets", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? extractMethod = typeof(MainPage).GetMethod("ExtractPreviewPath", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? normalizeMethod = typeof(MainPage).GetMethod("NormalizePreviewNavigationKey", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildMethod);
        Assert.NotNull(extractMethod);
        Assert.NotNull(normalizeMethod);

        Dictionary<string, string> links = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chapter-appendix-reference-dictionary.html#dict-ref-16828-cockatrice"] = "creatures/cockatrice.md",
        };
        string baseDirectory = Path.Combine(Path.GetTempPath(), $"grimoire-ui-autolink-{Guid.NewGuid():N}", "content");
        string baseUrl = new Uri(baseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, UriKind.Absolute).AbsoluteUri;

        Dictionary<string, string> targets = (Dictionary<string, string>)buildMethod!.Invoke(null, [links, baseUrl])!;
        string originalHrefKey = (string)normalizeMethod!.Invoke(null, ["chapter-appendix-reference-dictionary.html#dict-ref-16828-cockatrice"])!;
        string grimoireHrefKey = (string)normalizeMethod!.Invoke(null, ["grimoire://open?path=creatures%2Fcockatrice.md"])!;

        Assert.Equal("creatures/cockatrice.md", targets[originalHrefKey]);
        Assert.Equal("creatures/cockatrice.md", targets[grimoireHrefKey]);
        Assert.Equal("creatures/cockatrice.md", extractMethod!.Invoke(null, ["grimoire://open?path=creatures%2Fcockatrice.md"]));
        Assert.Equal("creatures/cockatrice.md", extractMethod.Invoke(null, ["grimoire://open/?path=creatures%2Fcockatrice.md"]));
    }

    /// <summary>
    /// Verifies that preview path extraction supports encoded markdown and JSON paths.
    /// </summary>
    [Fact]
    public void PreviewPathExtractionSupportsMarkdownAndJsonTargetsWithEncodedPaths()
    {
        MethodInfo? extractMethod = typeof(MainPage).GetMethod("ExtractPreviewPath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(extractMethod);

        Assert.Equal(
            "content/chapter 02.md",
            extractMethod!.Invoke(null, ["grimoire://open?path=content%2Fchapter%2002.md"]) as string);
        Assert.Equal(
            "snippets/goblin.json",
            extractMethod.Invoke(null, ["grimoire://open/?source=preview&path=snippets%2Fgoblin.json&line=12"]) as string);
        Assert.Equal(
            "content/appendix.md",
            extractMethod.Invoke(null, ["path=content%2Fappendix.md"]) as string);
    }

    /// <summary>
    /// Verifies that trusted preview open URLs require the grimoire open scheme.
    /// </summary>
    [Fact]
    public void PreviewOpenUrlTrustRequiresGrimoireOpenScheme()
    {
        MethodInfo? trustMethod = typeof(MainPage).GetMethod("IsTrustedPreviewOpenUrl", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(trustMethod);

        Assert.True((bool)trustMethod!.Invoke(null, ["grimoire://open?path=content%2Fchapter%2002.md"])!);
        Assert.True((bool)trustMethod.Invoke(null, ["grimoire://open/?path=snippets%2Fgoblin.json"])!);
        Assert.False((bool)trustMethod.Invoke(null, ["https://example.com/?path=content%2Fchapter%2002.md"])!);
        Assert.False((bool)trustMethod.Invoke(null, ["grimoire://evil?path=content%2Fchapter%2002.md"])!);
    }

    /// <summary>
    /// Verifies that markdown and JSON preview links map to source file targets.
    /// </summary>
    [Fact]
    public void PreviewNavigationTargetsMapMarkdownAndJsonRelativeLinksToSourceFiles()
    {
        MethodInfo? buildMethod = typeof(MainPage).GetMethod("BuildPreviewNavigationTargets", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? normalizeMethod = typeof(MainPage).GetMethod("NormalizePreviewNavigationKey", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(buildMethod);
        Assert.NotNull(normalizeMethod);

        Dictionary<string, string> links = new(StringComparer.OrdinalIgnoreCase)
        {
            ["./content/chapter%2002.md#arrival"] = "content/chapter 02.md",
            ["./snippets/goblin.json#stats"] = "snippets/goblin.json",
        };
        string baseDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "preview-base", "content"));
        string baseUrl = new Uri(baseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, UriKind.Absolute).AbsoluteUri;

        Dictionary<string, string> targets = (Dictionary<string, string>)buildMethod!.Invoke(null, [links, baseUrl])!;
        string markdownRelative = (string)normalizeMethod!.Invoke(null, ["./content/chapter%2002.md#arrival"])!;
        string markdownAbsolute = (string)normalizeMethod.Invoke(null, [new Uri(new Uri(baseUrl), "./content/chapter%2002.md#arrival").AbsoluteUri])!;
        string markdownGrimoire = (string)normalizeMethod.Invoke(null, ["grimoire://open?path=content%2Fchapter%2002.md"])!;
        string jsonRelative = (string)normalizeMethod.Invoke(null, ["./snippets/goblin.json#stats"])!;
        string jsonGrimoire = (string)normalizeMethod.Invoke(null, ["grimoire://open/?path=snippets%2Fgoblin.json"])!;

        Assert.Equal("content/chapter 02.md", targets[markdownRelative]);
        Assert.Equal("content/chapter 02.md", targets[markdownAbsolute]);
        Assert.Equal("content/chapter 02.md", targets[markdownGrimoire]);
        Assert.Equal("snippets/goblin.json", targets[jsonRelative]);
        Assert.Equal("snippets/goblin.json", targets[jsonGrimoire]);
    }

    /// <summary>
    /// Verifies that preview navigation fallback resolves in-project file and relative links.
    /// </summary>
    [Fact]
    public void PreviewNavigationFallbackResolvesFileUriAndRelativeSourcePathWithinProject()
    {
        MethodInfo? resolveMethod = typeof(MainPage).GetMethod("ResolvePreviewProjectRelativePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolveMethod);

        string workspaceRoot = Path.Combine(Directory.GetCurrentDirectory(), "test-artifacts", $"preview-nav-{Guid.NewGuid():N}");
        string contentDirectory = Path.Combine(workspaceRoot, "content");
        string snippetsDirectory = Path.Combine(workspaceRoot, "snippets");
        Directory.CreateDirectory(contentDirectory);
        Directory.CreateDirectory(snippetsDirectory);
        string markdownFile = Path.Combine(contentDirectory, "001.md");
        string jsonFile = Path.Combine(snippetsDirectory, "goblin.json");
        string outsideJsonPath = Path.Combine(Directory.GetCurrentDirectory(), $"outside-preview-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(markdownFile, "# Chapter");
            File.WriteAllText(jsonFile, """{"name":"Goblin"}""");
            File.WriteAllText(outsideJsonPath, """{"name":"Outside"}""");

            string? fromFileUri = (string?)resolveMethod!.Invoke(null, [new Uri(jsonFile, UriKind.Absolute).AbsoluteUri, workspaceRoot, "content/001.md"]);
            string? fromRelativeHref = (string?)resolveMethod.Invoke(null, ["../snippets/goblin.json#stats", workspaceRoot, "content/001.md"]);
            string? fromOutsideFileUri = (string?)resolveMethod.Invoke(null, [new Uri(outsideJsonPath, UriKind.Absolute).AbsoluteUri, workspaceRoot, "content/001.md"]);
            string? fromExternalWebHref = (string?)resolveMethod.Invoke(null, ["https://example.com/content/001.md#anchor", workspaceRoot, "content/001.md"]);

            Assert.Equal("snippets/goblin.json", fromFileUri);
            Assert.Equal("snippets/goblin.json", fromRelativeHref);
            Assert.Null(fromOutsideFileUri);
            Assert.Null(fromExternalWebHref);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (File.Exists(outsideJsonPath))
            {
                File.Delete(outsideJsonPath);
            }
        }
    }

    /// <summary>
    /// Verifies that dock target resolution follows configured drop-location thresholds.
    /// </summary>
    [Fact]
    public void DockTargetResolutionUsesDropLocationThresholds()
    {
        MethodInfo? resolveMethod = typeof(MainPage).GetMethod("ResolveDockTarget", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolveMethod);

        object? left = resolveMethod!.Invoke(null, [new Point(20, 40), 900d, 600d]);
        object? right = resolveMethod.Invoke(null, [new Point(520, 40), 900d, 600d]);
        object? bottom = resolveMethod.Invoke(null, [new Point(360, 580), 900d, 600d]);
        object? fallback = resolveMethod.Invoke(null, [null, 900d, 600d]);

        Assert.Equal("Left", left?.ToString());
        Assert.Equal("Right", right?.ToString());
        Assert.Equal("Bottom", bottom?.ToString());
        Assert.Equal("Right", fallback?.ToString());
    }

    /// <summary>
    /// Verifies that preview progress mapping tracks long-running preview phases.
    /// </summary>
    [Fact]
    public void PreviewProgressMappingTracksLongPreviewPhases()
    {
        double bodyProgress = InvokePreviewProgress("Rendering preview body for content/001.md: includes, substitutions, and autolinks.");
        double autolinkProgress = InvokePreviewProgress("Preview auto-linking mention 2580/5161: Brutus.");
        double autolinkCompleteProgress = InvokePreviewProgress("Preview auto-linking complete: 5161 entity names processed.");
        double rewriteProgress = InvokePreviewProgress("Rewriting preview links for content/001.md: 42 navigable entity links.");
        double completeProgress = InvokePreviewProgress("Completed preview render for content/001.md: cacheHit=False, indexTopics=5161, elapsedMs=1000.");

        Assert.Equal(0.58, bodyProgress, precision: 2);
        Assert.InRange(autolinkProgress, 0.77, 0.79);
        Assert.Equal(0.90, autolinkCompleteProgress, precision: 2);
        Assert.Equal(0.95, rewriteProgress, precision: 2);
        Assert.Equal(1, completeProgress, precision: 2);
    }

    /// <summary>
    /// Verifies that legacy preview autolink progress messages remain compatible.
    /// </summary>
    [Fact]
    public void PreviewProgressMappingKeepsLegacyAutolinkMessageCompatible()
    {
        double progress = InvokePreviewProgress("Preview autolink 50/100: Ancient Key.");

        Assert.Equal(0.78, progress, precision: 2);
    }

    /// <summary>
    /// Verifies that long progress messages preserve the useful trailing context.
    /// </summary>
    [Fact]
    public void ProgressMessageFormattingPrefersUsefulTailWhenLong()
    {
        MethodInfo? method = typeof(MainPage).GetMethod("FormatProgressMessage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        string suffix = "Preview auto-linking mention 5161/5161: Brutus.";
        string message = new string('x', 200) + suffix;
        string formatted = (string)method!.Invoke(null, [message])!;

        Assert.StartsWith("...", formatted, StringComparison.Ordinal);
        Assert.EndsWith(suffix, formatted, StringComparison.Ordinal);
        Assert.True(formatted.Length <= 140);
    }

    /// <summary>
    /// Verifies that supported YAML settings files map to expected settings profiles.
    /// </summary>
    [Fact]
    public void SettingsProfileDetectionRecognizesSupportedYamlFiles()
    {
        MethodInfo? method = typeof(MainPage).GetMethod("TryGetSettingsProfile", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        AssertProfile(method!, "settings/global.yml", "Global");
        AssertProfile(method, "settings/html.yml", "Html");
        AssertProfile(method, "settings/pdf.yml", "Pdf");
        AssertProfile(method, "settings/foundry.yml", "Foundry");
        AssertProfile(method, "settings/other.yml", null);
    }

    /// <summary>
    /// Verifies that syntax-highlighted JSON output includes expected token CSS classes.
    /// </summary>
    [Fact]
    public void SyntaxHighlightHtmlIncludesTokenClassesForJson()
    {
        MethodInfo? method = typeof(MainPage).GetMethod("BuildSyntaxHighlightedHtml", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        string html = (string)method!.Invoke(null, ["{\"name\":\"Spark\",\"active\":true}", ".json"])!;
        Assert.Contains("class=\"s\">\"Spark\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"b\">true", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the editable syntax editor renders as a single editable surface with line numbers.
    /// </summary>
    [Fact]
    public void EditableSyntaxEditorHtmlIsSingleEditableSurfaceWithLineNumbers()
    {
        MethodInfo? method = typeof(MainPage).GetMethod("BuildEditableSyntaxEditorHtml", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        string html = (string)method!.Invoke(null, ["# Spark\n\n{\"active\":true}", ".md", 2])!;

        Assert.Contains("<script src=\"https://cdnjs.cloudflare.com/ajax/libs/ace/1.36.0/ace.js\"></script>", html, StringComparison.Ordinal);
        Assert.Contains("const editor = ace.edit('editor');", html, StringComparison.Ordinal);
        Assert.Contains("editor.session.setMode(resolveMode());", html, StringComparison.Ordinal);
        Assert.Contains("getValue: () => getText()", html, StringComparison.Ordinal);
        Assert.Contains("window.grimoireEditor", html, StringComparison.Ordinal);
        Assert.Contains("grimoire-editor", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Invokes preview progress mapping and returns the mapped progress value.
    /// </summary>
    /// <param name="message">The progress message to map.</param>
    /// <returns>The mapped preview progress value.</returns>
    private static double InvokePreviewProgress(string message)
    {
        MethodInfo? method = typeof(MainPage).GetMethod("TryMapPreviewProgress", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        object? value = method!.Invoke(null, [message]);
        Assert.NotNull(value);
        return Assert.IsType<double>(value);
    }

    /// <summary>
    /// Asserts that the settings profile resolver returns the expected profile match result.
    /// </summary>
    /// <param name="method">The profile resolver method.</param>
    /// <param name="path">The settings file path to evaluate.</param>
    /// <param name="expectedProfile">The expected profile name, or <see langword="null"/> when no match is expected.</param>
    private static void AssertProfile(MethodInfo method, string path, string? expectedProfile)
    {
        object?[] args = [path, null];
        bool matched = (bool)method.Invoke(null, args)!;
        if (expectedProfile is null)
        {
            Assert.False(matched);
            return;
        }

        Assert.True(matched);
        Assert.Equal(expectedProfile, args[1]?.ToString());
    }

    /// <summary>
    /// Provides a controllable workflow service test double for controller tests.
    /// </summary>
    private sealed class FakeWorkflowService : IGrimoireUiWorkflowService
    {
        /// <summary>
        /// Gets or sets a <see cref="CompilationRequest"/> representing the compile response returned by this fake service.
        /// </summary>
        public CompilationRequest CompileRequest { get; init; } = new("in", InputSourceKind.Directory, "out", ExportTarget.Website);

        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the scaffold path returned by this fake service.
        /// </summary>
        public string ScaffoldResult { get; init; } = "scaffolded";

        /// <summary>
        /// Gets or sets a <see cref="DndBeyondSyncSummary"/> representing the D&amp;D Beyond sync summary returned by this fake service.
        /// </summary>
        public DndBeyondSyncSummary DndSummary { get; init; } = new(0, 0, 0, 0, 0);

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyList{LoreSearchResult}"/> representing the lore search results returned by this fake service.
        /// </summary>
        public IReadOnlyList<LoreSearchResult> SearchResults { get; init; } = [];

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyList{ProjectFileItem}"/> representing the project items returned by this fake service.
        /// </summary>
        public IReadOnlyList<ProjectFileItem> ProjectItems { get; init; } = [];

        /// <summary>
        /// Gets or sets a <see cref="ProjectFileDocument"/> representing the project file document returned by this fake service.
        /// </summary>
        public ProjectFileDocument FileDocument { get; init; } = new("content/001.md", "# Entry", "content", []);

        /// <summary>
        /// Gets or sets a <see cref="SourcebookPreviewResult"/> representing the preview result returned by this fake service.
        /// </summary>
        public SourcebookPreviewResult PreviewResult { get; init; } = new("<html></html>", new Dictionary<string, string>());

        /// <summary>
        /// Gets or sets a <see cref="ReferenceScanResult"/> representing the reference scan result returned by this fake service.
        /// </summary>
        public ReferenceScanResult ScanResult { get; init; } = new(0, 0, 0, []);

        /// <summary>
        /// Gets a <see cref="DndBeyondSyncOptions"/> representing the latest D&amp;D Beyond sync options received by this fake service.
        /// </summary>
        public DndBeyondSyncOptions? ReceivedDndOptions { get; private set; }

        /// <summary>
        /// Returns the configured compilation request.
        /// </summary>
        /// <param name="inputPath">The source input path.</param>
        /// <param name="outputPath">The output path.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the configured compilation request.</returns>
        public Task<CompilationRequest> CompileAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(CompileRequest);
        }

        /// <summary>
        /// Returns the configured scaffold result path.
        /// </summary>
        /// <param name="targetPath">The target scaffold path.</param>
        /// <param name="overwriteExisting">A value indicating whether existing files are overwritten.</param>
        /// <returns>The configured scaffold result path.</returns>
        public string Scaffold(string targetPath, bool overwriteExisting)
        {
            return ScaffoldResult;
        }

        /// <summary>
        /// Stores and returns the configured D&amp;D Beyond sync summary.
        /// </summary>
        /// <param name="options">The sync options received by the service.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the configured sync summary.</returns>
        public Task<DndBeyondSyncSummary> SyncDndbAsync(DndBeyondSyncOptions options, CancellationToken cancellationToken)
        {
            ReceivedDndOptions = options;
            return Task.FromResult(DndSummary);
        }

        /// <summary>
        /// Returns the configured lore search results.
        /// </summary>
        /// <param name="projectPath">The project path to search.</param>
        /// <param name="query">The search query.</param>
        /// <param name="limit">A value indicating the maximum result count.</param>
        /// <returns>The configured lore search results.</returns>
        public IReadOnlyList<LoreSearchResult> SearchLore(string projectPath, string query, int limit)
        {
            return SearchResults;
        }

        /// <summary>
        /// Returns the configured project item list.
        /// </summary>
        /// <param name="projectPath">The project path to open.</param>
        /// <returns>The configured project item list.</returns>
        public IReadOnlyList<ProjectFileItem> OpenProject(string projectPath)
        {
            return ProjectItems;
        }

        /// <summary>
        /// Returns the configured project file document.
        /// </summary>
        /// <param name="projectPath">The project path containing the file.</param>
        /// <param name="relativePath">The project-relative file path.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the configured file document.</returns>
        public Task<ProjectFileDocument> ReadProjectFileAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(FileDocument);
        }

        /// <summary>
        /// Completes successfully without persisting content.
        /// </summary>
        /// <param name="projectPath">The project path containing the file.</param>
        /// <param name="relativePath">The project-relative file path.</param>
        /// <param name="content">The file content to save.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A completed task.</returns>
        public Task SaveProjectFileAsync(string projectPath, string relativePath, string content, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns the configured sourcebook preview result.
        /// </summary>
        /// <param name="projectPath">The project path containing the file.</param>
        /// <param name="relativePath">The project-relative file path.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the configured preview result.</returns>
        public Task<SourcebookPreviewResult> RenderPreviewAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(PreviewResult);
        }

        /// <summary>
        /// Returns a fixed asset import result for tests.
        /// </summary>
        /// <param name="projectPath">The project path receiving the asset.</param>
        /// <param name="sourceAssetPath">The source asset path.</param>
        /// <param name="targetSubdirectory">The optional project subdirectory for import.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the fixed asset import result.</returns>
        public Task<AssetImportResult> ImportAssetAsync(string projectPath, string sourceAssetPath, string? targetSubdirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AssetImportResult("assets/test.png", "![test](assets/test.png)"));
        }

        /// <summary>
        /// Returns the supplied zip path.
        /// </summary>
        /// <param name="projectPath">The project path to export.</param>
        /// <param name="zipPath">The destination zip path.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the supplied zip path.</returns>
        public Task<string> ExportZipAsync(string projectPath, string zipPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(zipPath);
        }

        /// <summary>
        /// Returns the supplied target directory.
        /// </summary>
        /// <param name="zipPath">The zip archive path to import.</param>
        /// <param name="targetDirectory">The target extraction directory.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the supplied target directory.</returns>
        public Task<string> ImportZipAsync(string zipPath, string targetDirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult(targetDirectory);
        }

        /// <summary>
        /// Returns a fixed git status entry.
        /// </summary>
        /// <param name="projectPath">The project path to inspect.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing a fixed git status list.</returns>
        public Task<IReadOnlyList<GitStatusEntry>> GetGitStatusAsync(string projectPath, CancellationToken cancellationToken)
        {
            IReadOnlyList<GitStatusEntry> entries = [new("M", "content/001.md")];
            return Task.FromResult(entries);
        }

        /// <summary>
        /// Returns a fixed git history entry.
        /// </summary>
        /// <param name="projectPath">The project path to inspect.</param>
        /// <param name="limit">A value indicating the history entry limit.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing a fixed git history list.</returns>
        public Task<IReadOnlyList<GitHistoryEntry>> GetGitHistoryAsync(string projectPath, int limit, CancellationToken cancellationToken)
        {
            IReadOnlyList<GitHistoryEntry> entries = [new("abc123", "2026-06-21", "Tester", "Initial")];
            return Task.FromResult(entries);
        }

        /// <summary>
        /// Returns a fixed git commit completion value.
        /// </summary>
        /// <param name="projectPath">The project path receiving the commit.</param>
        /// <param name="message">The commit message.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing a fixed commit completion value.</returns>
        public Task<string> CommitGitAsync(string projectPath, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult("committed");
        }

        /// <summary>
        /// Returns the configured reference scan result.
        /// </summary>
        /// <param name="projectPath">The project path to scan.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the configured reference scan result.</returns>
        public Task<ReferenceScanResult> ScanReferencesAsync(string projectPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(ScanResult);
        }

        /// <summary>
        /// Completes successfully without moving any file.
        /// </summary>
        /// <param name="projectPath">The project path containing the entry.</param>
        /// <param name="relativePath">The project-relative entry path.</param>
        /// <param name="targetDirectory">The target directory for the move.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A completed task.</returns>
        public Task MoveProjectEntryAsync(string projectPath, string relativePath, string targetDirectory, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns a copied entry path composed from target directory and source file name.
        /// </summary>
        /// <param name="projectPath">The project path containing the entry.</param>
        /// <param name="relativePath">The project-relative entry path.</param>
        /// <param name="targetDirectory">The target directory for the copy.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the computed copied entry path.</returns>
        public Task<string> CopyProjectEntryAsync(string projectPath, string relativePath, string targetDirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult($"{targetDirectory.Trim('/')}/{Path.GetFileName(relativePath)}".Trim('/'));
        }

        /// <summary>
        /// Completes successfully without deleting any file.
        /// </summary>
        /// <param name="projectPath">The project path containing the entry.</param>
        /// <param name="relativePath">The project-relative entry path.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A completed task.</returns>
        public Task DeleteProjectEntryAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns the requested new entry name.
        /// </summary>
        /// <param name="projectPath">The project path containing the entry.</param>
        /// <param name="relativePath">The project-relative entry path.</param>
        /// <param name="newName">The new entry name.</param>
        /// <param name="cancellationToken">A token indicating cancellation.</param>
        /// <returns>A task containing the supplied new entry name.</returns>
        public Task<string> RenameProjectEntryAsync(string projectPath, string relativePath, string newName, CancellationToken cancellationToken)
        {
            return Task.FromResult(newName);
        }

        /// <summary>
        /// Combines project and relative paths into a full file system path.
        /// </summary>
        /// <param name="projectPath">The project root path.</param>
        /// <param name="relativePath">The project-relative entry path.</param>
        /// <returns>The combined full path.</returns>
        public string GetProjectEntryFullPath(string projectPath, string relativePath)
        {
            return Path.Combine(projectPath, relativePath);
        }
    }
}
