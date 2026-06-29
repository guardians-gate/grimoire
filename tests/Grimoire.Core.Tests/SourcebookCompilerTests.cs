using System.Reflection;
using System.Text.Json;

namespace Grimoire.Core.Tests;

/// <summary>
/// Contains tests that verify sourcebook compilation and preview behavior.
/// </summary>
public sealed partial class SourcebookCompilerTests
{
    /// <summary>
    /// Verifies that compiling to a PDF target creates a placeholder artifact.
    /// </summary>
    [Fact]
    public async Task CompileAsyncCreatesPlaceholderArtifactForPdfAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        string outputFile = Path.Combine(workspace.RootPath, "artifacts", "book.pdf");

        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputFile);
        SourcebookCompiler compiler = new();

        await compiler.CompileAsync(request, CancellationToken.None);

        Assert.True(File.Exists(outputFile));
    }

    /// <summary>
    /// Verifies that compiling to a website target creates expected site assets.
    /// </summary>
    [Fact]
    public async Task CompileAsyncCreatesWebsiteIndexForWebsiteTargetAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        string outputDirectory = Path.Combine(workspace.RootPath, "site");

        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();

        await compiler.CompileAsync(request, CancellationToken.None);

        string indexPath = Path.Combine(outputDirectory, "index.html");
        string stylesPath = Path.Combine(outputDirectory, "styles.css");
        Assert.True(File.Exists(indexPath));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "assets", "fonts", "Libre Baskerville.ttf")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "assets", "fonts", "Nodesto Caps Condensed.otf")));
        string styles = await File.ReadAllTextAsync(stylesPath).ConfigureAwait(true);
        Assert.Contains("font-family:'Nodesto Caps Condensed'", styles, StringComparison.Ordinal);
        Assert.Contains("font-family:'Libre Baskerville'", styles, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the optional title file contributes cover content.
    /// </summary>
    [Fact]
    public async Task CompileAsyncIncludesCoverFromOptionalTitleFileAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "TITLE.md"), "# Cover Title\nA proper subtitle.");
        string contentDirectory = Path.Combine(inputDirectory, "content");
        Directory.CreateDirectory(contentDirectory);
        await File.WriteAllTextAsync(Path.Combine(contentDirectory, "001.md"), "# Chapter\nSome content.");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();

        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));
        Assert.Contains("Cover Title", html, StringComparison.Ordinal);
        Assert.Contains("cover-page", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that compiling to a Foundry database path writes expected seed data.
    /// </summary>
    [Fact]
    public async Task CompileAsyncWritesFoundrySeedExportToDbPathAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        string contentDirectory = Path.Combine(inputDirectory, "content");
        Directory.CreateDirectory(contentDirectory);
        await File.WriteAllTextAsync(Path.Combine(contentDirectory, "001.md"), "---\ntitle: Log Entry\n---\nThe watch has changed.");

        string outputPath = Path.Combine(workspace.RootPath, "night.db");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputPath);
        SourcebookCompiler compiler = new();

        await compiler.CompileAsync(request, CancellationToken.None);

        using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={outputPath}");
        await connection.OpenAsync().ConfigureAwait(true);

        using Microsoft.Data.Sqlite.SqliteCommand metadata = connection.CreateCommand();
        metadata.CommandText = "SELECT value FROM metadata WHERE key = 'schemaVersion';";
        object? schemaValue = await metadata.ExecuteScalarAsync().ConfigureAwait(true);
        Assert.Equal("1", Convert.ToString(schemaValue, System.Globalization.CultureInfo.InvariantCulture));

        using Microsoft.Data.Sqlite.SqliteCommand docs = connection.CreateCommand();
        docs.CommandText = "SELECT COUNT(1) FROM documents;";
        object? countValue = await docs.ExecuteScalarAsync().ConfigureAwait(true);
        Assert.Equal(1L, Convert.ToInt64(countValue, System.Globalization.CultureInfo.InvariantCulture));

        using Microsoft.Data.Sqlite.SqliteCommand name = connection.CreateCommand();
        name.CommandText = "SELECT name FROM documents LIMIT 1;";
        object? nameValue = await name.ExecuteScalarAsync().ConfigureAwait(true);
        Assert.Equal("Log Entry", Convert.ToString(nameValue, System.Globalization.CultureInfo.InvariantCulture));
        await connection.CloseAsync().ConfigureAwait(true);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Verifies that website compilation emits table of contents, index, and bibliography pages.
    /// </summary>
    [Fact]
    public async Task CompileAsyncBuildsTocIndexAndBibliographySectionsForWebsiteAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        string contentDirectory = Path.Combine(inputDirectory, "content");
        Directory.CreateDirectory(contentDirectory);
        await File.WriteAllTextAsync(Path.Combine(contentDirectory, "001.md"), "---\ntitle: Chapter One\n---\nText.");
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "README.md"), "# Foreword\nIntro text.");
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "TITLE.md"), "# Title\nCover.");
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "SOURCES.md"), "# Sources\n- Book A");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string homeHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));
        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));
        string bibliographyHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "bibliography.html"));
        Assert.Contains("Table of Contents", homeHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"author\" class=\"author-page\"", homeHtml, StringComparison.Ordinal);
        Assert.Contains("chapter-foreword.html#foreword", homeHtml, StringComparison.Ordinal);
        Assert.Contains("chapter-001.html#001", homeHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"#cover\"", homeHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"001\">", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("<details class=\"page-toc\"", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"#001\"", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"index\">", indexHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"#index\"", indexHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"bibliography\">", bibliographyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"idx-readme\"", indexHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"idx-title\"", indexHtml, StringComparison.Ordinal);
        int indexStart = indexHtml.IndexOf("id=\"index\"", StringComparison.Ordinal);
        string indexSegment = indexHtml[indexStart..];
        Assert.DoesNotContain(">README<", indexSegment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">TITLE<", indexSegment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that unreferenced snippets are appended before the index when enabled.
    /// </summary>
    [Fact]
    public async Task CompileAsyncAppendsUnreferencedSnippetsBeforeIndexWhenEnabledAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets", "lore"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                unreferenced: true
            """);
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            ![Used Snippet](../snippets/used.json)
            """);
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "snippets", "used.json"), """{"title":"Used Snippet","content":"Referenced snippet text."}""");
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "snippets", "unused.json"), """{"title":"Unused Snippet","content":"Appendix snippet text."}""");
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "snippets", "lore", "unused-lore.json"), """{"title":"Unused Lore Snippet","content":"Appendix lore snippet text."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string appendixHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-snippets.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));
        int appendixStart = appendixHtml.IndexOf("id=\"appendix-snippets\"", StringComparison.Ordinal);
        int indexStart = indexHtml.IndexOf("id=\"index\"", StringComparison.Ordinal);
        Assert.True(appendixStart >= 0);
        Assert.True(indexStart >= 0);

        string appendixSegment = appendixHtml[appendixStart..];
        Assert.Contains("snippets", appendixSegment, StringComparison.Ordinal);
        Assert.Contains("snippets/lore", appendixSegment, StringComparison.Ordinal);
        Assert.Contains("Unused Snippet", appendixSegment, StringComparison.Ordinal);
        Assert.Contains("Appendix snippet text.", appendixSegment, StringComparison.Ordinal);
        Assert.Contains("Unused Lore Snippet", appendixSegment, StringComparison.Ordinal);
        Assert.Contains("Appendix lore snippet text.", appendixSegment, StringComparison.Ordinal);
        Assert.DoesNotContain("Used Snippet", appendixSegment, StringComparison.Ordinal);
        Assert.DoesNotContain("Referenced snippet text.", appendixSegment, StringComparison.Ordinal);

        string indexSegment = indexHtml[indexStart..];
        Assert.Contains("Unused Snippet", indexSegment, StringComparison.Ordinal);
        Assert.Contains("Unused Lore Snippet", indexSegment, StringComparison.Ordinal);
        Assert.Contains("chapter-appendix-snippets.html#ref-unused", indexSegment, StringComparison.Ordinal);
        Assert.Contains("chapter-appendix-snippets.html#ref-unused-lore", indexSegment, StringComparison.Ordinal);
        Assert.Contains("Used Snippet", indexSegment, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that inline markdown includes render as continuous material.
    /// </summary>
    [Fact]
    public async Task CompileAsyncRendersInlineIncludeAsContinuousMaterialAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "locations"));

        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Inline Chapter
            ---
            ![District Details](../locations/degolburg.md?inline)
            """);
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "locations", "degolburg.md"),
            """
            ---
            title: Degolburg Village Square
            ---
            The bustling village square is filled with merchants.
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string stylesheet = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "styles.css"));
        Assert.Contains("class=\"material-inline-anchor\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("material-entry material-inline", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"infobox\"", html, StringComparison.Ordinal);
        Assert.Contains(".material-inline-anchor{display:block;margin:.2rem 0 .6rem;}", stylesheet, StringComparison.Ordinal);
        Assert.DoesNotContain(".material-inline{", stylesheet, StringComparison.Ordinal);
        Assert.DoesNotContain("<hr class=\"material-divider\" />", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that sibling markdown include links honor the inline query flag.
    /// </summary>
    [Fact]
    public async Task CompileAsyncHonorsInlineIncludeQueryFlagForSiblingMarkdownAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Inline Query Chapter
            ---
            ![included](markdown-files.md?inline)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "markdown-files.md"),
            """
            ---
            title: Included Section
            ---
            This is inlined content.
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string stylesheet = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "styles.css"));
        Assert.Contains("This is inlined content.", html, StringComparison.Ordinal);
        Assert.Contains("class=\"material-inline-anchor\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("material-entry material-inline", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"infobox\"", html, StringComparison.Ordinal);
        Assert.Contains(".material-inline-anchor{display:block;margin:.2rem 0 .6rem;}", stylesheet, StringComparison.Ordinal);
        Assert.DoesNotContain(".material-inline{", stylesheet, StringComparison.Ordinal);
        Assert.DoesNotContain("<hr class=\"material-divider\" />", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that GitHub alert blockquote syntax renders as styled callouts.
    /// </summary>
    [Fact]
    public async Task CompileAsyncRendersGithubAlertSyntaxAsCalloutAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Alert Chapter
            ---
            > [!TIP]
            > Use cover and concealment whenever possible.

            > [!NOTE]
            > Standard marching order keeps travel safe.

            > A normal quote still uses default blockquote styling.
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string styles = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "styles.css"));
        Assert.Contains("<blockquote class=\"alert alert-tip\">", html, StringComparison.Ordinal);
        Assert.Contains("<blockquote class=\"alert alert-note\">", html, StringComparison.Ordinal);
        Assert.Contains("<blockquote>", html, StringComparison.Ordinal);
        Assert.Contains("A normal quote still uses default blockquote styling.", html, StringComparison.Ordinal);
        Assert.Contains("blockquote.alert.alert-tip", styles, StringComparison.Ordinal);
        Assert.Contains("blockquote.alert.alert-note", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("[!TIP]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[!NOTE]", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that JSON character previews use the character sheet hero image.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncShowsCharacterSheetHeroImageForJsonAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "players"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "players", "hero.json"),
            """
            {
              "ddb": {
                "character": {
                  "name": "Hero",
                  "decorations": {
                    "avatarUrl": "https://example.test/avatar.png"
                  }
                }
              },
              "description": "Heroic preview body."
            }
            """);

        SourcebookCompiler compiler = new();
        SourcebookPreviewResult preview = await compiler.RenderPreviewAsync(inputDirectory, "players/hero.json", CancellationToken.None);

        Assert.Contains("material-hero", preview.Html, StringComparison.Ordinal);
        Assert.Contains("https://example.test/avatar.png", preview.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that creature previews prefer the large avatar image when available.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncPrefersCreatureLargeAvatarAsHeroImageAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "creatures"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "goblin.json"),
            """
            {
              "name": "Goblin Scout",
              "largeAvatarUrl": "https://example.test/creature-large.png",
              "avatarUrl": "https://example.test/creature-avatar.png",
              "characteristicsDescription": "A sneaky creature."
            }
            """);

        SourcebookCompiler compiler = new();
        SourcebookPreviewResult preview = await compiler.RenderPreviewAsync(inputDirectory, "creatures/goblin.json", CancellationToken.None);

        Assert.Contains("material-hero", preview.Html, StringComparison.Ordinal);
        Assert.Contains("https://example.test/creature-large.png", preview.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.test/creature-avatar.png", preview.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that unfenced front-matter dividers remain part of markdown body content.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncTreatsUnfencedDividerAsMarkdownBodyAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            Lore: [unclosed
            ---
            Second paragraph remains.
            """);

        SourcebookCompiler compiler = new();
        SourcebookPreviewResult preview = await compiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);

        Assert.Contains("Lore: [unclosed", preview.Html, StringComparison.Ordinal);
        Assert.Contains("Second paragraph remains.", preview.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that previews resolve dynamic substitutions and local asset URIs.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncResolvesDynamicSubstitutionsAndLocalAssetUrisAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "global.yml"),
            """
            project:
              title: Preview Atlas
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Preview Chapter
            ---
            {{macro.title}}
            ![Map](./map.png)
            """);
        await File.WriteAllBytesAsync(Path.Combine(inputDirectory, "content", "map.png"), [1, 2, 3, 4]);

        SourcebookCompiler compiler = new();
        SourcebookPreviewResult preview = await compiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);

        Assert.Contains("Preview Atlas", preview.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("@@GRIMOIRE_PROJECT_DYNAMIC__", preview.Html, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", preview.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that included inline asset URIs are rewritten against the project root.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncRewritesIncludedInlineAssetUrisAgainstProjectRootAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets", "images"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Included Asset
            ---
            ![Lore](../snippets/lore.md?inline)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "snippets", "lore.md"),
            """
            ---
            title: Lore
            ---
            ![Sigil](./images/sigil.png)
            """);
        await File.WriteAllBytesAsync(Path.Combine(inputDirectory, "snippets", "images", "sigil.png"), [9, 8, 7, 6]);

        SourcebookCompiler compiler = new();
        SourcebookPreviewResult preview = await compiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);

        Assert.Contains("data:image/png;base64,", preview.Html, StringComparison.Ordinal);
        Assert.Contains("material-inline-anchor", preview.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that dictionary mention candidates refresh after preview cache invalidation.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncRefreshesDictionaryMentionCandidatesAfterCacheInvalidationAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "global.yml"),
            """
            compiler:
              autoLink: true
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Preview Chapter
            ---
            Old Entity appears in the story.
            """);
        string snippetPath = Path.Combine(inputDirectory, "snippets", "entity.json");
        await File.WriteAllTextAsync(snippetPath, """{"title":"Old Entity","content":"Original details."}""");

        SourcebookCompiler compiler = new();
        SourcebookPreviewResult firstPreview = await compiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);
        Assert.Contains("grimoire://open?path=snippets%2Fentity.json", firstPreview.Html, StringComparison.Ordinal);
        Assert.Contains(">Old Entity<", firstPreview.Html, StringComparison.OrdinalIgnoreCase);

        await File.WriteAllTextAsync(snippetPath, """{"title":"New Entity","content":"Updated details."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Preview Chapter
            ---
            New Entity appears in the story.
            """);
        compiler.InvalidatePreviewCache();

        SourcebookPreviewResult secondPreview = await compiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);
        Assert.Contains("grimoire://open?path=snippets%2Fentity.json", secondPreview.Html, StringComparison.Ordinal);
        Assert.Contains(">New Entity<", secondPreview.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Old Entity<", secondPreview.Html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that preview generation writes and reuses disk cache artifacts when content hashes match.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncWritesAndReusesDiskGeneratedCacheWhenHashesMatchAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Cached Chapter
            ---
            Preview cache line.
            """);

        SourcebookCompiler compiler = new();
        SourcebookPreviewResult firstPreview = await compiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);
        Assert.Contains("Preview cache line.", firstPreview.Html, StringComparison.Ordinal);

        string cacheRoot = Path.Combine(inputDirectory, ".caches");
        Assert.True(File.Exists(Path.Combine(cacheRoot, "hashes.json")));
        Assert.True(File.Exists(Path.Combine(cacheRoot, "topics.json")));
        Assert.True(File.Exists(Path.Combine(cacheRoot, "state.json")));
        Assert.True(Directory.Exists(Path.Combine(cacheRoot, "generated")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(cacheRoot, "generated"), "*.json", SearchOption.TopDirectoryOnly));

        SourcebookCompiler secondCompiler = new();
        SourcebookPreviewResult secondPreview = await secondCompiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);
        Assert.Contains("Preview cache line.", secondPreview.Html, StringComparison.Ordinal);
        Assert.NotNull(secondPreview.Diagnostics);
        Assert.True(secondPreview.Diagnostics!.FileCacheHit);
    }

    /// <summary>
    /// Verifies that preview disk cache entries rebuild when content hashes change.
    /// </summary>
    [Fact]
    public async Task RenderPreviewAsyncRebuildsDiskCacheWhenContentHashesChangeAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        string contentPath = Path.Combine(inputDirectory, "content", "001.md");
        await File.WriteAllTextAsync(
            contentPath,
            """
            ---
            title: Cache Drift
            ---
            First version.
            """);

        SourcebookCompiler firstCompiler = new();
        SourcebookPreviewResult firstPreview = await firstCompiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);
        Assert.Contains("First version.", firstPreview.Html, StringComparison.Ordinal);
        string hashesPath = Path.Combine(inputDirectory, ".caches", "hashes.json");
        Dictionary<string, string> firstHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(hashesPath)) ?? [];
        Assert.True(firstHashes.TryGetValue("content/001.md", out string? firstHash));
        Assert.False(string.IsNullOrWhiteSpace(firstHash));

        await File.WriteAllTextAsync(
            contentPath,
            """
            ---
            title: Cache Drift
            ---
            Second version.
            """);

        SourcebookCompiler secondCompiler = new();
        SourcebookPreviewResult secondPreview = await secondCompiler.RenderPreviewAsync(inputDirectory, "content/001.md", CancellationToken.None);
        Assert.Contains("Second version.", secondPreview.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("First version.", secondPreview.Html, StringComparison.Ordinal);
        Assert.NotNull(secondPreview.Diagnostics);
        Assert.False(secondPreview.Diagnostics!.FileCacheHit);

        Dictionary<string, string> secondHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(hashesPath)) ?? [];
        Assert.True(secondHashes.TryGetValue("content/001.md", out string? secondHash));
        Assert.NotEqual(firstHash, secondHash);
    }

    /// <summary>
    /// Verifies that known preview links are rewritten in a single rendered HTML pass.
    /// </summary>
    [Fact]
    public void RewritePreviewLinksRewritesKnownHrefsInSingleRenderedHtmlPass()
    {
        string html =
            """
            <p>
              <a href="chapter.html#known">Known</a>
              <a href="chapter.html?x=1&amp;y=2#escaped">Escaped</a>
              <a HREF="CHAPTER.HTML#CASE">Case</a>
              <a href="chapter.html#unknown">Unknown</a>
            </p>
            """;
        Dictionary<string, string> targets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chapter.html#known"] = "content/known.md",
            ["chapter.html?x=1&y=2#escaped"] = "content/with space.md",
            ["chapter.html#case"] = "content/case.md",
        };

        string rewritten = (string)InvokePrivateStatic(typeof(SourcebookCompiler), "RewritePreviewLinks", html, targets)!;

        Assert.Contains("href=\"grimoire://open?path=content%2Fknown.md\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"grimoire://open?path=content%2Fwith%20space.md\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"grimoire://open?path=content%2Fcase.md\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"chapter.html#unknown\"", rewritten, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that creature identifier fields render as names and false boolean rows are omitted.
    /// </summary>
    [Fact]
    public async Task CompileAsyncFormatsCreatureIdsAsNamesAndHidesFalseBooleanRowsAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "creatures"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Creature Formatting
            ---
            ![Test Creature](../creatures/test-creature.json?inline)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "TEMPLATE.md"),
            """
            # {{name}}

            | Property         | Value               |
            |------------------|---------------------|
            | Challenge Rating | {{challengeRatingId}} |
            | Size             | {{sizeId}}          |
            | Creature Type    | {{typeId}}          |
            | Alignment        | {{alignmentId}}     |
            | Legendary        | {{isLegendary}}     |
            | Mythic           | {{isMythic}}        |
            | Has Lair         | {{hasLair}}         |
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "test-creature.json"),
            """
            {
              "name": "Formatting Beast",
              "challengeRatingId": 4,
              "sizeId": 3,
              "typeId": 13,
              "alignmentId": 8,
              "isLegendary": false,
              "isMythic": true,
              "hasLair": false
            }
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("1/2", html, StringComparison.Ordinal);
        Assert.Contains("Small", html, StringComparison.Ordinal);
        Assert.Contains("Monstrosity", html, StringComparison.Ordinal);
        Assert.Contains("Neutral Evil", html, StringComparison.Ordinal);
        Assert.Contains("Mythic", html, StringComparison.Ordinal);
        Assert.Contains("Yes", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Legendary", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Has Lair", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that reference dictionary generation indexes both chapter and dictionary targets.
    /// </summary>
    [Fact]
    public async Task CompileAsyncGeneratesReferenceDictionaryAndIndexesBothReferenceTargetsAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            ![Used Snippet](../snippets/used.json)
            """);
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "snippets", "used.json"), """{"title":"Used Snippet","content":"Referenced snippet text."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));
        Assert.Contains("Used Snippet", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("Referenced snippet text.", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("chapter-001.html#001", indexHtml, StringComparison.Ordinal);
        Assert.Contains("chapter-appendix-reference-dictionary.html#dict-ref-used", indexHtml, StringComparison.Ordinal);
        Assert.Contains(">Chapter One</a>", indexHtml, StringComparison.Ordinal);
        Assert.Contains(">Used Snippet</a>", indexHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that referenced creature entities appear in the reference dictionary and index.
    /// </summary>
    [Fact]
    public async Task CompileAsyncIncludesReferencedCreatureEntitiesInReferenceDictionaryAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "creatures"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            ![Cockatrice](../creatures/16828-COCKATRICE.json)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "16828-COCKATRICE.json"),
            """{"name":"Cockatrice","characteristicsDescription":"A petrifying avian horror."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));
        Assert.Contains("Cockatrice", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("chapter-appendix-reference-dictionary.html#dict-ref-16828-cockatrice", indexHtml, StringComparison.Ordinal);
        Assert.Contains("chapter-001.html#001", indexHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that dictionary-enabled auto-linking adds expected chapter, dictionary, and index targets.
    /// </summary>
    [Fact]
    public async Task CompileAsyncDictionaryEnabledAutoLinksMentionedEntityAndAddsDictionaryAndIndexTargetsAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "creatures"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            The Cockatrice haunts this place.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "16828-COCKATRICE.json"),
            """{"name":"Cockatrice","characteristicsDescription":"A petrifying avian horror."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));

        Assert.Contains("href=\"chapter-appendix-reference-dictionary.html#dict-ref-16828-cockatrice\">Cockatrice</a>", chapterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cockatrice", dictionaryHtml, StringComparison.Ordinal);

        int cockatriceIndex = indexHtml.IndexOf("Cockatrice", StringComparison.OrdinalIgnoreCase);
        Assert.True(cockatriceIndex >= 0);
        string cockatriceSegment = indexHtml[cockatriceIndex..Math.Min(indexHtml.Length, cockatriceIndex + 800)];
        Assert.Contains("chapter-appendix-reference-dictionary.html#dict-ref-16828-cockatrice", cockatriceSegment, StringComparison.Ordinal);
        Assert.Contains("chapter-001.html#001", cockatriceSegment, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that mentions inside included snippets auto-link even when the entity is inline included.
    /// </summary>
    [Fact]
    public async Task CompileAsyncDictionaryEnabledAutoLinksMentionsInsideIncludedSnippetsEvenWhenEntityIsInlineIncludedAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "creatures"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            ![Lore](../snippets/lore.md?inline)
            ![Cockatrice](../creatures/16828-COCKATRICE.json?inline)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "snippets", "lore.md"),
            """
            ---
            title: Lore
            ---
            Legends fear the Cockatrice in old ruins.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "16828-COCKATRICE.json"),
            """{"name":"Cockatrice","characteristicsDescription":"A petrifying avian horror."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));

        Assert.Contains("href=\"chapter-appendix-reference-dictionary.html#dict-ref-16828-cockatrice\">Cockatrice</a>", chapterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cockatrice", dictionaryHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that configured shadow references are included in the reference dictionary when unreferenced.
    /// </summary>
    [Fact]
    public async Task CompileAsyncIncludesShadowReferencesInReferenceDictionaryEvenWhenUnreferencedAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
                shadowReferences:
                  - Cure Wounds
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            This chapter has no spell includes.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "136566-CURE-WOUNDS.json"),
            """{"name":"Cure Wounds","description":"A creature regains hit points.","content":"A creature regains hit points."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));
        Assert.Contains("Cure Wounds", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("A creature regains hit points.", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("chapter-appendix-reference-dictionary.html#dict-ref-136566-cure-wounds", indexHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that reference dictionary expansion includes transitive mentioned entries.
    /// </summary>
    [Fact]
    public async Task CompileAsyncReferenceDictionaryExpandsTransitiveMentionedEntriesAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "creatures"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            ![Drow Scout](../creatures/17133-DROW-SCOUT.md?inline)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "17133-DROW-SCOUT.md"),
            """
            ---
            title: Drow Scout
            ---
            At will: dancing lights.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "21171967-DANCING-LIGHTS.json"),
            """{"name":"Dancing Lights","description":"Create up to four torch-sized lights."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));
        Assert.Contains("id=\"dict-ref-21171967-dancing-lights\"", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("Dancing Lights", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"chapter-appendix-reference-dictionary.html#dict-ref-21171967-dancing-lights\"", dictionaryHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that page tables of contents collapse by default when entry count exceeds the threshold.
    /// </summary>
    [Fact]
    public async Task CompileAsyncCollapsesPageTocByDefaultWhenEntriesExceedThresholdAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets"));

        for (int index = 0; index < 16; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(inputDirectory, "snippets", $"entry-{index:00}.json"),
                $$"""{"title":"Entry {{index:00}}","content":"Body {{index:00}}"}""");
        }

        string chapterBody = string.Join(Environment.NewLine, Enumerable.Range(0, 16).Select(static index => $"![Entry {index:00}](../snippets/entry-{index:00}.json?inline)"));
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            $"---{Environment.NewLine}title: Dense Chapter{Environment.NewLine}---{Environment.NewLine}{chapterBody}");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("<details class=\"page-toc\">", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<details class=\"page-toc\" open>", chapterHtml, StringComparison.Ordinal);
    }

}
