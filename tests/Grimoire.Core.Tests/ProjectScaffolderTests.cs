using Grimoire.Core;

namespace Grimoire.Core.Tests;

/// <summary>
/// Represents tests that validate baseline project scaffolding output.
/// </summary>
public sealed class ProjectScaffolderTests
{
    /// <summary>
    /// Verifies that scaffolding creates the expected directories, templates, and metadata files and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void ScaffoldCreatesExpectedBaselineFiles()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string root = Path.Combine(workspace.RootPath, "new-project");
        ProjectScaffolder.Scaffold(root);

        Assert.True(File.Exists(Path.Combine(root, "TITLE.md")));
        Assert.True(File.Exists(Path.Combine(root, "AUTHORS.md")));
        Assert.True(File.Exists(Path.Combine(root, "LICENSE.md")));
        Assert.True(File.Exists(Path.Combine(root, "README.md")));
        Assert.True(File.Exists(Path.Combine(root, "SOURCES.md")));
        Assert.True(File.Exists(Path.Combine(root, "content", "001_intro.md")));
        Assert.True(File.Exists(Path.Combine(root, "settings", "global.yml")));
        Assert.True(File.Exists(Path.Combine(root, "creatures", "TEMPLATE.md")));
        Assert.True(File.Exists(Path.Combine(root, "factions", "TEMPLATE.md")));
        Assert.True(File.Exists(Path.Combine(root, "items", "TEMPLATE.md")));
        Assert.True(File.Exists(Path.Combine(root, "locations", "TEMPLATE.md")));
        Assert.True(File.Exists(Path.Combine(root, "snippets", "TEMPLATE.md")));
        Assert.True(File.Exists(Path.Combine(root, "spells", "TEMPLATE.md")));
        Assert.True(File.Exists(Path.Combine(root, "players", "TEMPLATE.md")));
        Assert.True(File.Exists(Path.Combine(root, "settings", "fonts", "Libre Baskerville.ttf")));
        Assert.True(File.Exists(Path.Combine(root, "settings", "fonts", "Nodesto Caps Condensed.otf")));
        Assert.True(File.Exists(Path.Combine(root, ".gitignore")));
        Assert.Contains(".caches/", File.ReadAllText(Path.Combine(root, ".gitignore")), StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(root, ".caches")));
        Assert.True(Directory.Exists(Path.Combine(root, ".caches", "generated")));
        Assert.True(File.Exists(Path.Combine(root, ".caches", "hashes.json")));
        Assert.True(File.Exists(Path.Combine(root, ".caches", "topics.json")));
        Assert.True(File.Exists(Path.Combine(root, ".caches", "state.json")));
    }

    /// <summary>
    /// Represents a disposable temporary directory used by scaffolding tests.
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
        public string RootPath { get; }

        /// <summary>
        /// Creates a temporary workspace and returns a <see cref="TestWorkspace"/> representing the created test fixture root.
        /// </summary>
        /// <returns>A <see cref="TestWorkspace"/> representing the created temporary workspace.</returns>
        public static TestWorkspace Create()
        {
            string path = Path.Combine(Path.GetTempPath(), $"grimoire-scaffold-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TestWorkspace(path);
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
