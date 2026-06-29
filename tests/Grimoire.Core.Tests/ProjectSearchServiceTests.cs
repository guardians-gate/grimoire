using Grimoire.Core;

namespace Grimoire.Core.Tests;

/// <summary>
/// Represents integration tests for catalog and advanced project-search behaviors.
/// </summary>
public sealed class ProjectSearchServiceTests
{
    /// <summary>
    /// Verifies catalog search honors CLI format templates and path filters and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncUsesCliFormatAndPathFilterAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "items"));
        Directory.CreateDirectory(Path.Combine(root, "spells"));

        await File.WriteAllTextAsync(Path.Combine(root, "items", "TEMPLATE.md"),
            """
            ---
            cliFormat: |
              - Type: {{type}}
              - Rarity: {{rarity}}
            ---
            # {{name}}
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "items", "arrows.json"), """{"name":"Arrows","type":"Gear","rarity":"Common"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "spells", "spark.json"), """{"name":"Spark","level":1}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(root, ["items"]), CancellationToken.None).ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Equal("Arrows", entry.Name);
        Assert.Equal("items/arrows.json", entry.RelativePath);
        Assert.Contains("- Type: Gear", entry.Details, StringComparison.Ordinal);
        Assert.Contains("- Rarity: Common", entry.Details, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies CLI format fallback resolves nested substitutions and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncCliFormatFallbackSupportsNestedSubstitutionsAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "items"));

        await File.WriteAllTextAsync(Path.Combine(root, "items", "TEMPLATE.md"),
            """
            ---
            cliFormat: |
              - Summary: {{description::-{{content}}}}
            ---
            # {{name}}
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "items", "ring.json"), """{"name":"Ring","content":"Echoes in silver"}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(root, ["items"]), CancellationToken.None).ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Contains("- Summary: Echoes in silver", entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("{{content}}", entry.Details, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies catalog search falls back to property-table formatting when CLI format is absent and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncFallsBackToPropertyTableWhenCliFormatMissingAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "spells"));
        await File.WriteAllTextAsync(Path.Combine(root, "spells", "spark.json"), """{"name":"Spark","level":2,"school":"Divination"}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(root, ["spells"]), CancellationToken.None).ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Equal("Spark", entry.Name);
        Assert.Contains("- level: 2nd", entry.Details, StringComparison.Ordinal);
        Assert.Contains("- school: Divination", entry.Details, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies catalog search recurses nested directories and records include ancestry for nested content and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncRecursesEntityDirectoriesAndAnnotatesNestedContentIncludesAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "creatures"));
        Directory.CreateDirectory(Path.Combine(root, "creatures", "nested"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(root, "content", "assets"));

        await File.WriteAllTextAsync(Path.Combine(root, "creatures", "goblin.json"), """{"name":"Goblin"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "creatures", "nested", "dragon.json"), """{"name":"Dragon"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(
            Path.Combine(root, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            ![Hidden](./assets/hidden.md?inline)
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "content", "assets", "hidden.md"), "---\ntitle: Hidden Asset\n---\nNested content asset page.").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> creatureEntries = await ProjectSearchService.SearchAsync(new(root, ["creatures"]), CancellationToken.None).ConfigureAwait(true);
        IReadOnlyList<ProjectSearchEntry> contentEntries = await ProjectSearchService.SearchAsync(new(root, ["content"]), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, creatureEntries.Count);
        Assert.Contains(creatureEntries, static item => string.Equals(item.RelativePath, "creatures/goblin.json", StringComparison.Ordinal));
        Assert.Contains(creatureEntries, static item => string.Equals(item.RelativePath, "creatures/nested/dragon.json", StringComparison.Ordinal));

        Assert.Equal(2, contentEntries.Count);
        ProjectSearchEntry hiddenEntry = Assert.Single(contentEntries, static item => string.Equals(item.RelativePath, "content/assets/hidden.md", StringComparison.Ordinal));
        Assert.Contains("content/001.md", hiddenEntry.IncludedBy);
    }

    /// <summary>
    /// Verifies player name fallback uses D&amp;D Beyond filename conventions and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncUsesPlayerFileNameFallbackForDndBeyondPatternAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "players"));
        await File.WriteAllTextAsync(Path.Combine(root, "players", "12345-jane-doe.json"), """{"ddb":{"character":{"baseHitPoints":30}}}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(root, ["players"]), CancellationToken.None).ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Equal("jane doe", entry.Name);
    }

    /// <summary>
    /// Verifies spell name fallback uses D&amp;D Beyond filename conventions and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncUsesSpellFileNameFallbackForDndBeyondPatternAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "spells"));
        await File.WriteAllTextAsync(Path.Combine(root, "spells", "5678-fireball.json"), """{"level":3}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(root, ["spells"]), CancellationToken.None).ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Equal("fireball", entry.Name);
    }

    /// <summary>
    /// Verifies catalog queries filter entries by provided search text and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncCatalogFiltersByQueryWhenProvidedAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "players"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "players", "12345-nektreor.json"),
            """{"ddb":{"character":{"name":"NEKTREOR MAUGHTHAR"}}}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(
            Path.Combine(root, "players", "99999-other.json"),
            """{"ddb":{"character":{"name":"ALARIA VALE"}}}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(
                new(root, ["players"], "Nektreor Maughthar"),
                CancellationToken.None)
            .ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Equal("Nektreor Maughthar", entry.Name);
        Assert.Equal("players/12345-nektreor.json", entry.RelativePath);
    }

    /// <summary>
    /// Verifies player search prefers embedded D&amp;D Beyond character names over filename fallbacks and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncPrefersDdbCharacterNameOverPlayerFilenameAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "players"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "players", "12345-placeholder.json"),
            """{"ddb":{"character":{"name":"NEKTREOR MAUGHTHAR"}}}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(root, ["players"]), CancellationToken.None).ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Equal("Nektreor Maughthar", entry.Name);
    }

    /// <summary>
    /// Verifies spell search prefers embedded definition names over filename fallbacks and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAsyncPrefersSpellDefinitionNameOverFilenameAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "spells"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "spells", "5678-placeholder.json"),
            """{"definition":{"name":"ARCANE LANCE"}}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(root, ["spells"]), CancellationToken.None).ConfigureAwait(true);

        ProjectSearchEntry entry = Assert.Single(entries);
        Assert.Equal("Arcane Lance", entry.Name);
    }

    /// <summary>
    /// Verifies advanced full-text search includes nested content matches and include ancestry and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAdvancedAsyncFindsFullTextAndAnnotatesNestedContentIncludesAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(root, "content", "assets"));
        Directory.CreateDirectory(Path.Combine(root, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(root, "content", "001.md"),
            """
            ---
            title: Chapter
            ---
            Cure Wounds is prepared today.
            ![Hidden](./assets/hidden.md?inline)
            """).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "content", "assets", "hidden.md"), "---\ntitle: Hidden\n---\nCure Wounds appears in nested content page.").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "spells", "cure.json"), """{"name":"Cure Wounds","description":"Healing spell."}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
            new(root, ProjectSearchMode.FullText, Query: "Cure Wounds", Limit: 20),
            CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(matches, static match => string.Equals(match.RelativePath, "content/001.md", StringComparison.Ordinal));
        Assert.Contains(matches, static match => string.Equals(match.RelativePath, "spells/cure.json", StringComparison.Ordinal));
        Assert.Contains(matches, static match => string.Equals(match.RelativePath, "content/assets/hidden.md", StringComparison.Ordinal));
        Assert.Contains(matches, static match =>
            string.Equals(match.RelativePath, "content/assets/hidden.md", StringComparison.Ordinal) &&
            match.IncludedBy.Contains("content/001.md", StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies advanced full-text search matches terms crossing streaming-buffer boundaries and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAdvancedAsyncFindsFullTextAcrossBufferBoundariesAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "content"));
        string payload = new string('A', 32766) + "Cure Wounds" + new string('B', 64);
        await File.WriteAllTextAsync(Path.Combine(root, "content", "001.md"), payload).ConfigureAwait(true);

        IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
            new(root, ProjectSearchMode.FullText, Query: "Cure Wounds", Limit: 20),
            CancellationToken.None).ConfigureAwait(true);

        ProjectSearchMatch match = Assert.Single(matches, static item => string.Equals(item.RelativePath, "content/001.md", StringComparison.Ordinal));
        Assert.Equal(1, match.LineNumber);
        Assert.Contains("Cure Wounds", match.Snippet, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies advanced property-mode search finds JSON property matches and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAdvancedAsyncFindsJsonPropertyMatchesAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "spells"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "spells", "cure.json"),
            """{"definition":{"name":"Cure Wounds","school":"Evocation"}}""").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
            new(root, ProjectSearchMode.Property, Query: "Cure", PropertyPath: "definition.name", Limit: 20),
            CancellationToken.None).ConfigureAwait(true);

        ProjectSearchMatch match = Assert.Single(matches);
        Assert.Equal("spells/cure.json", match.RelativePath);
        Assert.Equal("definition.name", match.PropertyPath);
    }

    /// <summary>
    /// Verifies keyword-usage mode binds mentions to indexed entities and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAdvancedAsyncFindsKeywordUsageBackedByEntityAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(root, "spells"));
        await File.WriteAllTextAsync(Path.Combine(root, "spells", "cure.json"), """{"name":"Cure Wounds","description":"A spell."}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "content", "001.md"), "---\ntitle: Chapter\n---\nThe cleric casts Cure Wounds.").ConfigureAwait(true);

        IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
            new(root, ProjectSearchMode.KeywordUsage, Query: "cure", Limit: 20),
            CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(matches, static match =>
            string.Equals(match.EntityName, "Cure Wounds", StringComparison.Ordinal) &&
            string.Equals(match.EntityPath, "spells/cure.json", StringComparison.Ordinal) &&
            string.Equals(match.RelativePath, "content/001.md", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies cross-reference mode reports include and macro target links and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAdvancedAsyncListsCrossReferencesAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(root, "spells"));
        Directory.CreateDirectory(Path.Combine(root, "snippets"));
        await File.WriteAllTextAsync(Path.Combine(root, "spells", "cure.json"), """{"name":"Cure Wounds"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(root, "snippets", "note.json"), """{"title":"Note","content":"Tip"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(
            Path.Combine(root, "content", "001.md"),
            """
            ---
            title: Chapter
            ---
            ![Cure](../spells/cure.json)
            ${../snippets/note.json}
            """).ConfigureAwait(true);

        IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
            new(root, ProjectSearchMode.CrossReference, Limit: 20),
            CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(matches, static match =>
            string.Equals(match.RelativePath, "content/001.md", StringComparison.Ordinal) &&
            string.Equals(match.TargetPath, "spells/cure.json", StringComparison.Ordinal) &&
            string.Equals(match.MatchKind, "include", StringComparison.Ordinal));
        Assert.Contains(matches, static match =>
            string.Equals(match.RelativePath, "content/001.md", StringComparison.Ordinal) &&
            string.Equals(match.TargetPath, "snippets/note.json", StringComparison.Ordinal) &&
            string.Equals(match.MatchKind, "macro", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies cross-reference query mode emits mention matches linked to resolved entity files and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task SearchAdvancedAsyncCrossReferenceQueryFindsMentionBackedByEntityAsync()
    {
        using SearchWorkspace workspace = SearchWorkspace.Create();
        string root = workspace.RootPath;
        Directory.CreateDirectory(Path.Combine(root, "content"));
        Directory.CreateDirectory(Path.Combine(root, "players"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "players", "12345-nektreor.json"),
            """{"ddb":{"character":{"name":"NEKTREOR MAUGHTHAR"}}}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(
            Path.Combine(root, "content", "001.md"),
            """
            ---
            title: Chapter
            ---
            Nektreor Maughthar convened the council.
            """).ConfigureAwait(true);

        IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
                new(root, ProjectSearchMode.CrossReference, Query: "Nektreor Maughthar", Limit: 20),
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Contains(matches, static match =>
            string.Equals(match.RelativePath, "content/001.md", StringComparison.Ordinal) &&
            string.Equals(match.MatchKind, "mention", StringComparison.Ordinal) &&
            string.Equals(match.TargetPath, "players/12345-nektreor.json", StringComparison.Ordinal) &&
            string.Equals(match.EntityName, "Nektreor Maughthar", StringComparison.Ordinal));
    }

    /// <summary>
    /// Represents a disposable temporary workspace fixture used by project-search tests.
    /// </summary>
    private sealed class SearchWorkspace : IDisposable
    {
        /// <summary>
        /// A <see cref="bool"/> indicating whether this workspace fixture has already been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a temporary workspace rooted at a specific directory path.
        /// </summary>
        /// <param name="rootPath">The root path representing the temporary workspace location.</param>
        private SearchWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the root directory for the temporary workspace fixture.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Creates a temporary workspace and returns a <see cref="SearchWorkspace"/> representing the created fixture root.
        /// </summary>
        /// <returns>A <see cref="SearchWorkspace"/> representing the created temporary workspace.</returns>
        public static SearchWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"grimoire-search-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new SearchWorkspace(rootPath);
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
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
