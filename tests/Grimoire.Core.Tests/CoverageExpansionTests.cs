using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Grimoire.Core;
using Microsoft.Data.Sqlite;

namespace Grimoire.Core.Tests;

/// <summary>
/// Expands coverage over validation, fallback, and edge-case paths in core services.
/// </summary>
public sealed class CoverageExpansionTests
{
    /// <summary>
    /// Verifies that input inspection rejects invalid paths and handles zip input.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task InputInspectorThrowsForInvalidInputAsync()
    {
        InputInspector inspector = new();
        Assert.Throws<ArgumentException>(() => inspector.Inspect(" "));
        Assert.Throws<ArgumentException>(() => inspector.Inspect(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}")));

        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-input");
        string zipRoot = workspace.CreateProjectRoot();
        await File.WriteAllTextAsync(Path.Combine(zipRoot, "a.txt"), "x").ConfigureAwait(true);
        string zipPath = Path.Combine(workspace.RootPath, "input.zip");
        await ZipFile.CreateFromDirectoryAsync(zipRoot, zipPath, CancellationToken.None).ConfigureAwait(true);
        InputInspectionResult inspected = inspector.Inspect(zipPath);
        Assert.Equal(InputSourceKind.ZipArchive, inspected.SourceKind);
    }

    /// <summary>
    /// Verifies that output inspection rejects blank output values.
    /// </summary>
    [Fact]
    public void OutputInspectorThrowsForInvalidOutput()
    {
        OutputInspector inspector = new();
        Assert.Throws<ArgumentException>(() => inspector.Inspect(" "));
    }

    /// <summary>
    /// Verifies that project scaffolding rejects an invalid target path.
    /// </summary>
    [Fact]
    public void ProjectScaffolderThrowsForInvalidTarget()
    {
        Assert.Throws<ArgumentException>(() => ProjectScaffolder.Scaffold(" "));
    }

    /// <summary>
    /// Verifies zip compilation and invalid export target handling.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SourcebookCompilerSupportsZipInputAndRejectsUnknownTargetAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-zip");
        string projectRoot = workspace.CreateProjectRoot();
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "README.md"), "# Foreword\nZip path.").ConfigureAwait(true);
        string contentDir = Path.Combine(projectRoot, "content");
        Directory.CreateDirectory(contentDir);
        await File.WriteAllTextAsync(Path.Combine(contentDir, "001.md"), "# Chapter\nZip works.").ConfigureAwait(true);

        string zipPath = Path.Combine(workspace.RootPath, "project.zip");
        await ZipFile.CreateFromDirectoryAsync(projectRoot, zipPath, CancellationToken.None).ConfigureAwait(true);
        string outputDir = Path.Combine(workspace.RootPath, "site");

        SourcebookCompiler compiler = new();
        CompilationRequest zipRequest = new(zipPath, InputSourceKind.ZipArchive, outputDir, ExportTarget.Website);
        await compiler.CompileAsync(zipRequest, CancellationToken.None).ConfigureAwait(true);
        Assert.True(File.Exists(Path.Combine(outputDir, "index.html")));

        CompilationRequest invalidTarget = new(projectRoot, InputSourceKind.Directory, outputDir, (ExportTarget)999);
        await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(invalidTarget, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies metadata/template rendering and Foundry DB overwrite behavior.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SourcebookCompilerCoversMetadataTemplatesAndFoundryDbOverwriteAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-compiler");
        string projectRoot = workspace.CreateProjectRoot();
        Directory.CreateDirectory(Path.Combine(projectRoot, "settings"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "settings", "fonts"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "content"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "items"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "spells"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "creatures"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "factions"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "locations"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "snippets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "players"));

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "TITLE.md"),
            """
            ---
            title: Title Front Matter
            author: Primary Author
            description: Campaign description
            jumbotron: maps/cover.png
            ---
            # Cover
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "AUTHORS.md"),
            """
            ---
            authors: Party Writer
            copyright: (c) Team
            ---
            Narrative credits
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "LICENSE.md"),
            """
            ---
            license: MIT
            ---
            MIT body text
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "README.md"), "# Foreword\nWelcome.").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "SOURCES.md"), "# Bibliography\n- Source A").ConfigureAwait(true);
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "settings", "global.yml"),
            """
            project:
              title: Global Title
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "settings", "html.yml"),
            """
            fonts:
              headings:
                family: Heading X
                color: "#123456"
              body:
                family: Body Y
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "settings", "pdf.yml"), "this-is-not-a-map").ConfigureAwait(true);

        await File.WriteAllBytesAsync(Path.Combine(projectRoot, "settings", "fonts", "heading.ttf"), [1, 2, 3]).ConfigureAwait(true);
        await File.WriteAllBytesAsync(Path.Combine(projectRoot, "settings", "fonts", "body.otf"), [1, 2, 3]).ConfigureAwait(true);
        await File.WriteAllBytesAsync(Path.Combine(projectRoot, "settings", "fonts", "extra.woff"), [1, 2, 3]).ConfigureAwait(true);
        await File.WriteAllBytesAsync(Path.Combine(projectRoot, "settings", "fonts", "extra2.woff2"), [1, 2, 3]).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "settings", "fonts", "ignore.txt"), "x").ConfigureAwait(true);

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "content", "001_intro.md"),
            """
            ---
            title: Intro Chapter
            jumbotron: maps/chapter.png
            ---
            Includes ![Local Image](maps/chapter.png)
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "content", "README.md"), "# Should not become root README").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "items", "sword.json"), """{"name":"Longsword","description":"Blade","features":"Versatile"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "spells", "spark.json"), """{"name":"Spark","table":"1 action","features":"light","description":"A spark"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "creatures", "goblin.json"), """{"name":"Goblin","statBlock":"AC 15","description":"Small raider"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "factions", "guild.json"), """{"name":"Guild","ideology":"Profit","methods":"Deals","people":"Traders"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "locations", "city.json"), """{"name":"City","description":"Large","features":"Walls","people":"Many","shops":"Bazaar","subLocations":"Docks"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "snippets", "note.json"), """{"title":"Did You Know","content":"Trivia"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "players", "hero.json"), """{"name":"Hero","characterSheet":"Sheet","description":"Brave"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "content", "json-topic.json"), """{"title":"Json Topic"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "content", "json-topic-2.json"), """{"name":"Named Topic"}""").ConfigureAwait(true);

        string outputDir = Path.Combine(workspace.RootPath, "website");
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(new CompilationRequest(projectRoot, InputSourceKind.Directory, outputDir, ExportTarget.Website), CancellationToken.None).ConfigureAwait(true);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDir, "index.html")).ConfigureAwait(true);
        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "chapter-001-intro.html")).ConfigureAwait(true);
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "index-topics.html")).ConfigureAwait(true);
        string css = await File.ReadAllTextAsync(Path.Combine(outputDir, "styles.css")).ConfigureAwait(true);
        Assert.Contains("By:</strong> Primary Author", html, StringComparison.Ordinal);
        Assert.Contains("Description:</strong> Campaign description", html, StringComparison.Ordinal);
        Assert.Contains("Copyright:</strong> (c) Team", html, StringComparison.Ordinal);
        Assert.Contains("License:</strong> MIT", html, StringComparison.Ordinal);
        Assert.Contains("chapter-jumbotron", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("Json Topic", indexHtml, StringComparison.Ordinal);
        Assert.Contains("Named Topic", indexHtml, StringComparison.Ordinal);
        Assert.Contains("format('woff')", css, StringComparison.Ordinal);
        Assert.Contains("format('woff2')", css, StringComparison.Ordinal);

        string dbPath = Path.Combine(workspace.RootPath, "foundry.db");
        await File.WriteAllTextAsync(dbPath, "placeholder").ConfigureAwait(true);
        await compiler.CompileAsync(new CompilationRequest(projectRoot, InputSourceKind.Directory, dbPath, ExportTarget.FoundryDb), CancellationToken.None).ConfigureAwait(true);
        Assert.True(new FileInfo(dbPath).Length > 100);
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Verifies D&amp;D Beyond sync handling for alternative payload shapes.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task DndBeyondSyncServiceCoversAlternativePayloadShapesAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-dndb");
        using CoverageDndbHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));

        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: null);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, summary.SourceCount);
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "items", "7-entity.json")));
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "spells", "8-MAGIC-MISSILE.json")));
    }

    /// <summary>
    /// Verifies D&amp;D Beyond sync behavior when entitlement source collections are missing.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task DndBeyondSyncServiceHandlesMissingEntitlementSourcesAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-dndb-empty");
        using EmptyEntitlementsDndbHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncSummary summary = await service.SyncAsync(
                new DndBeyondSyncOptions("token", workspace.RootPath, IncludeHomebrew: false, CampaignId: null),
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(0, summary.SourceCount);
    }

    /// <summary>
    /// Verifies that synchronization rejects missing cobalt token values.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task DndBeyondSyncServiceRejectsMissingTokenAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-token");
        using CoverageDndbHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: " ",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SyncAsync(options, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// Verifies lore query validation and exclusion of ignored settings folders.
    /// </summary>
    [Fact]
    public void LoreQueryEngineCoversValidationAndIgnoredFolders()
    {
        Assert.Throws<ArgumentException>(() => new LoreQueryEngine(" "));
        Assert.Throws<ArgumentException>(() => new LoreQueryEngine(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}")));

        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-lore");
        string project = workspace.CreateProjectRoot();
        Directory.CreateDirectory(Path.Combine(project, "settings", "fonts"));
        Directory.CreateDirectory(Path.Combine(project, "content"));
        File.WriteAllText(Path.Combine(project, "settings", "fonts", "skip.md"), "# Hidden\nquery");
        File.WriteAllText(Path.Combine(project, "settings", "fonts", "skip.json"), """{"name":"Hidden","content":"query"}""");
        File.WriteAllText(Path.Combine(project, "content", "nomatch.md"), "# No Match\nnothing here");
        File.WriteAllText(Path.Combine(project, "content", "plain.md"), "query without heading");
        File.WriteAllText(Path.Combine(project, "content", "entry.json"), """{"name":"entry","content":"query in json"}""");

        LoreQueryEngine engine = new(project);
        Assert.Throws<ArgumentException>(() => engine.Search(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Search("query", 0));

        IReadOnlyList<LoreSearchResult> results = engine.Search("query", 10);
        Assert.DoesNotContain(results, static r => r.Path.Contains("settings", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, static r => string.Equals(r.Title, "plain", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies private helper fallback paths used by sourcebook compilation.
    /// </summary>
    [Fact]
    public void SourcebookPrivateHelpersCoverFallbackPaths()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-private");
        string root = workspace.CreateProjectRoot();
        Directory.CreateDirectory(Path.Combine(root, "items"));
        Directory.CreateDirectory(Path.Combine(root, "spells"));
        Directory.CreateDirectory(Path.Combine(root, "players"));
        Directory.CreateDirectory(Path.Combine(root, "unknown"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));

        string mdNoName = Path.Combine(root, "content.md");
        File.WriteAllText(mdNoName, "---\nfoo: bar\n---\nBody");
        string jsonNoName = Path.Combine(root, "items", "mystery.json");
        File.WriteAllText(jsonNoName, """{"description":"No name"}""");
        string jsonWithName = Path.Combine(root, "items", "named.json");
        File.WriteAllText(jsonWithName, """{"name":"Named Json","description":"x"}""");
        string jsonContent = Path.Combine(root, "unknown", "content.json");
        File.WriteAllText(jsonContent, """{"content":"Fallback content"}""");
        string jsonNoContent = Path.Combine(root, "unknown", "empty.json");
        File.WriteAllText(jsonNoContent, """{"x":"y"}""");
        string jsonTemplateFallback = Path.Combine(root, "items", "fallback.json");
        File.WriteAllText(jsonTemplateFallback, """{"name":"Fallback Item","content":"Fallback from content"}""");
        string jsonNested = Path.Combine(root, "nested", "nested.json");
        File.WriteAllText(jsonNested, """{"outer":{"inner":"nested-value","list":[{"name":"first"}]}}""");
        string spellJson = Path.Combine(root, "spells", "spell.json");
        File.WriteAllText(spellJson, """{"name":"Arcane Burst","level":1,"school":"Conjuration","components":[1,2,3],"damage":{"diceString":"2d8","damageType":"Force"},"tags":["Warding","Buff"],"atHigherLevels":[{"level":3,"damage":{"diceString":"3d8","damageType":"Force"}}]}""");
        string ddbSpellJson = Path.Combine(root, "spells", "9876-fire-bolt.json");
        File.WriteAllText(ddbSpellJson, """{"level":1,"school":"Evocation"}""");
        string ddbPlayerJson = Path.Combine(root, "players", "12345-jane-doe.json");
        File.WriteAllText(ddbPlayerJson, """{"ddb":{"character":{"baseHitPoints":22,"stats":[{"value":10},{"value":12},{"value":14},{"value":16},{"value":8},{"value":18}]}}}""");
        string jsonTemplate = Path.Combine(root, "items", "TEMPLATE.md");
        File.WriteAllText(jsonTemplate, "# {{name}}\n{{description::-{{content}}}}");
        File.WriteAllText(Path.Combine(root, "spells", "TEMPLATE.md"), "# {{name}}\n{{_dndBeyondId::ddb-spell-link}}\n{{level::ordinal}}\n{{components}}\n{{damage::damage}}\n{{tags::csv}}\n{{atHigherLevels::higher-levels}}");
        File.WriteAllText(Path.Combine(root, "players", "TEMPLATE.md"), "# {{name}}\n{{_dndBeyondId::ddb-link}}");
        File.WriteAllText(Path.Combine(root, "nested", "TEMPLATE.md"), "{{outer.inner}}|{{outer.list[0].name}}");

        string mdResolved = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", mdNoName, (string?)null)!;
        Assert.Equal("content", mdResolved);
        string jsonResolved = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", jsonNoName, (string?)null)!;
        Assert.Equal("mystery", jsonResolved);
        string jsonNamed = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", jsonWithName, (string?)null)!;
        Assert.Equal("Named Json", jsonNamed);
        string jsonProperty = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", jsonNoName, "description")!;
        Assert.Equal("No name", jsonProperty);

        Assert.Throws<TargetInvocationException>(() => InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", mdNoName, "missing"));
        Assert.Throws<TargetInvocationException>(() => InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", jsonNoName, "missing"));
        Assert.Throws<TargetInvocationException>(() => InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", Path.Combine(root, "note.txt"), (string?)null));
        string nestedJsonProperty = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "ResolveReferenceValue", jsonNested, "outer.inner")!;
        Assert.Equal("nested-value", nestedJsonProperty);

        string renderedTemplate = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", jsonNoName)!;
        Assert.Contains("No name", renderedTemplate, StringComparison.Ordinal);
        string renderedTemplateFallback = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", jsonTemplateFallback)!;
        Assert.Contains("Fallback from content", renderedTemplateFallback, StringComparison.Ordinal);
        Assert.DoesNotContain("{{content}}", renderedTemplateFallback, StringComparison.Ordinal);
        string renderedNestedTemplate = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", jsonNested)!;
        Assert.Equal("nested-value|first", renderedNestedTemplate);
        string renderedSpellTemplate = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", spellJson)!;
        Assert.Contains("1st", renderedSpellTemplate, StringComparison.Ordinal);
        Assert.Contains("V, S, M", renderedSpellTemplate, StringComparison.Ordinal);
        Assert.Contains("2d8 Force", renderedSpellTemplate, StringComparison.Ordinal);
        Assert.Contains("Warding, Buff", renderedSpellTemplate, StringComparison.Ordinal);
        Assert.Contains("At Higher Levels:", renderedSpellTemplate, StringComparison.Ordinal);
        string renderedDdbSpellTemplate = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", ddbSpellJson)!;
        Assert.Contains("# fire bolt", renderedDdbSpellTemplate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[9876](https://www.dndbeyond.com/spells/9876)", renderedDdbSpellTemplate, StringComparison.Ordinal);
        string renderedPlayerTemplate = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", ddbPlayerJson)!;
        Assert.Contains("[12345](https://www.dndbeyond.com/characters/12345)", renderedPlayerTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("12345-jane-doe", renderedPlayerTemplate, StringComparison.Ordinal);
        string renderedContentFallback = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", jsonContent)!;
        Assert.Equal("Fallback content", renderedContentFallback);
        string renderedNoContent = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RenderJsonWithTemplate", jsonNoContent)!;
        Assert.Equal("No content.", renderedNoContent);

        Assert.Null(InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", (string?)null));
        Assert.Null(InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "unknown"));
        Assert.Contains("Armor Class", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "creatures")!, StringComparison.Ordinal);
        Assert.Contains("Ideology", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "factions")!, StringComparison.Ordinal);
        Assert.Contains("Features", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "items")!, StringComparison.Ordinal);
        Assert.Contains("Sub-locations", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "locations")!, StringComparison.Ordinal);
        Assert.Contains("{{content}}", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "snippets")!, StringComparison.Ordinal);
        Assert.Contains("Casting Time", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "spells")!, StringComparison.Ordinal);
        Assert.Contains("Character Sheet", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "GetDefaultCategoryTemplate", "players")!, StringComparison.Ordinal);

        string projectFallback = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "InferProjectTitle", Path.GetPathRoot(root)!)!;
        Assert.Equal("Grimoire Sourcebook", projectFallback);
        Assert.Equal(string.Empty, (string)InvokePrivateStatic(typeof(SourcebookCompiler), "FirstNonEmpty", (object)new string?[] { null, " ", null })!);
        Assert.Null(InvokePrivateStatic(typeof(SourcebookCompiler), "FirstNonEmptyOrNull", (object)new string?[] { null, " ", null }));
        Assert.Equal("truetype", (string)InvokePrivateStatic(typeof(SourcebookCompiler), "ToCssFontFormat", ".xyz")!);
    }

    /// <summary>
    /// Verifies topic index generation skips ignored pages and resolves JSON include fallback values.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SourcebookCompilerCoversIndexSkipsAndJsonIncludeFallbackAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-index");
        string projectRoot = workspace.CreateProjectRoot();
        Directory.CreateDirectory(Path.Combine(projectRoot, "settings"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "content"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "items"));

        await File.WriteAllTextAsync(Path.Combine(projectRoot, "README.md"), "# Foreword\nStart").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "settings", "notes.md"), "# Ignore me").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "content", "001.md"),
            """
            ---
            Title: README
            ---
            ![Item Box](../items/ring.json)
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "items", "ring.json"), """{"name":"Ring of Echoes","description":"Resonant"}""").ConfigureAwait(true);

        SourcebookCompiler compiler = new();
        string outputDir = Path.Combine(workspace.RootPath, "site");
        await compiler.CompileAsync(new CompilationRequest(projectRoot, InputSourceKind.Directory, outputDir, ExportTarget.Website), CancellationToken.None).ConfigureAwait(true);
        string html = await File.ReadAllTextAsync(Path.Combine(outputDir, "chapter-001.html")).ConfigureAwait(true);
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDir, "index-topics.html")).ConfigureAwait(true);

        Assert.Contains("Item Box", html, StringComparison.Ordinal);
        Assert.Contains("Ring of Echoes", indexHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignore me", indexHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">README<", indexHtml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies Foundry export behavior when the content directory is absent.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SourcebookCompilerFoundryExportHandlesMissingContentDirectoryAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-cov-foundry-empty");
        string projectRoot = workspace.CreateProjectRoot();
        string dbPath = Path.Combine(workspace.RootPath, "empty.db");
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(new CompilationRequest(projectRoot, InputSourceKind.Directory, dbPath, ExportTarget.FoundryDb), CancellationToken.None).ConfigureAwait(true);
        Assert.True(File.Exists(dbPath));
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Invokes a non-public static method through reflection.
    /// </summary>
    /// <param name="type">The declaring type.</param>
    /// <param name="method">The non-public static method name.</param>
    /// <param name="args">The invocation arguments.</param>
    /// <returns>The returned value, if any.</returns>
    private static object? InvokePrivateStatic(Type type, string method, params object?[] args)
    {
        MethodInfo? methodInfo = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(methodInfo);
        return methodInfo.Invoke(null, args);
    }

    /// <summary>
    /// Provides deterministic D&amp;D Beyond proxy payloads for coverage tests.
    /// </summary>
    private sealed class CoverageDndbHandler : HttpMessageHandler
    {
        /// <summary>
        /// Handles outgoing requests by returning canned proxy responses.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[{"id":7,"name":"!!!"}]}}""",
                "/proxy/class/spells" => """{"success":true,"data":[{"definition":{"id":8,"name":"Magic Missile"}}]}""",
                "/proxy/monster" => """{"success":true,"data":[{"id":9,"name":"Goblin"}]}""",
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
    /// Provides empty entitlement collections for D&amp;D Beyond sync coverage tests.
    /// </summary>
    private sealed class EmptyEntitlementsDndbHandler : HttpMessageHandler
    {
        /// <summary>
        /// Handles outgoing requests by returning empty data payloads.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[]}}""",
                "/proxy/class/spells" => """{"success":true,"data":[]}""",
                "/proxy/monster" => """{"success":true,"data":[]}""",
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
    /// Manages disposable temporary project workspaces used by coverage tests.
    /// </summary>
    private sealed class TempWorkspace : IDisposable
    {
        /// <summary>
        /// Gets or sets a <see cref="bool"/> indicating whether the workspace has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TempWorkspace"/> class.
        /// </summary>
        /// <param name="rootPath">The workspace root path.</param>
        private TempWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the workspace root path.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Creates a new temporary workspace with a unique directory name.
        /// </summary>
        /// <param name="prefix">The directory name prefix.</param>
        /// <returns>A workspace instance rooted at a unique path.</returns>
        public static TempWorkspace Create(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        /// <summary>
        /// Creates the project root directory inside the workspace.
        /// </summary>
        /// <returns>The project root path.</returns>
        public string CreateProjectRoot()
        {
            string project = Path.Combine(RootPath, "project");
            Directory.CreateDirectory(project);
            return project;
        }

        /// <summary>
        /// Deletes the workspace directory when it has not already been disposed.
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
