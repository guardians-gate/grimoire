namespace Grimoire.Core.Tests;

/// <summary>
/// Represents tests that verify lore-query indexing and search behaviors.
/// </summary>
public sealed class LoreQueryEngineTests
{
    /// <summary>
    /// Verifies that markdown lore entries are discovered by text search and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void SearchFindsMarkdownLoreByText()
    {
        using var workspace = TestWorkspace.Create();
        string projectPath = workspace.CreateProject();
        string contentPath = Path.Combine(projectPath, "content");
        Directory.CreateDirectory(contentPath);
        File.WriteAllText(
            Path.Combine(contentPath, "001.md"),
            """
---
title: Watch Report
---
# Night Watch
The lantern wardens patrol Degolburg every dusk.
""");

        LoreQueryEngine engine = new(projectPath);
        IReadOnlyList<LoreSearchResult> results = engine.Search("Degolburg", 5);

        Assert.NotEmpty(results);
        Assert.Contains(results, static result => result.Title.Contains("Night Watch", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that lore search respects caller-provided result limits and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void SearchRespectsLimit()
    {
        using var workspace = TestWorkspace.Create();
        string projectPath = workspace.CreateProject();
        Directory.CreateDirectory(Path.Combine(projectPath, "content"));
        for (int i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(projectPath, "content", $"{i:000}.md"), $"# Entry {i}\nThe same lore phrase appears here.");
        }

        LoreQueryEngine engine = new(projectPath);
        IReadOnlyList<LoreSearchResult> results = engine.Search("lore phrase", 2);
        Assert.Equal(2, results.Count);
    }

    /// <summary>
    /// Represents a disposable temporary workspace used by lore query tests.
    /// </summary>
    private sealed class TestWorkspace : IDisposable
    {
        /// <summary>
        /// A <see cref="bool"/> indicating whether this workspace has already been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a temporary test workspace rooted at a specific directory path.
        /// </summary>
        /// <param name="rootPath">The root path representing the temporary workspace location.</param>
        private TestWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the root directory for the temporary workspace.
        /// </summary>
        string RootPath { get; }

        /// <summary>
        /// Creates a temporary workspace and returns a <see cref="TestWorkspace"/> representing the created test fixture root.
        /// </summary>
        /// <returns>A <see cref="TestWorkspace"/> representing the created temporary workspace.</returns>
        public static TestWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"grimoire-lore-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new(rootPath);
        }

        /// <summary>
        /// Creates a project directory under the workspace and returns a <see cref="string"/> representing the created project root path.
        /// </summary>
        /// <returns>A <see cref="string"/> representing the created project root path.</returns>
        public string CreateProject()
        {
            string projectPath = Path.Combine(RootPath, "project");
            Directory.CreateDirectory(projectPath);
            return projectPath;
        }

        /// <summary>
        /// Deletes the temporary workspace directory and returns <see langword="void"/>.
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
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
