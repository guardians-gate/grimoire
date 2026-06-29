using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Represents a project scaffold generator that creates the default Grimoire folder layout, configuration files, and templates.
/// </summary>
/// <param name="inputLogger">The optional logger representing diagnostics for scaffold operations.</param>
public sealed partial class ProjectScaffolder(ILogger<ProjectScaffolder>? inputLogger = null)
{
    /// <summary>
    /// A <see cref="string"/> representing the canonical BLAKE2b-512 hash value for an empty indexed-name payload.
    /// </summary>
    private const string EmptyBlake2b512Hash = "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce";

    /// <summary>
    /// A <see cref="ILogger{TCategoryName}"/> representing diagnostics for scaffold lifecycle events.
    /// </summary>
    private readonly ILogger<ProjectScaffolder> logger = inputLogger ?? NullLogger<ProjectScaffolder>.Instance;

    /// <summary>
    /// A <see cref="DefaultCategoryTemplates"/> representing bundled default markdown templates by content category.
    /// </summary>
    private readonly DefaultCategoryTemplates _defaultCategoryTemplates = new();

    /// <summary>
    /// A <see cref="DefaultFontAssets"/> representing bundled default font assets copied into new projects.
    /// </summary>
    private readonly DefaultFontAssets _defaultFontAssets = new();

    /// <summary>
    /// Creates a scaffold in the target directory and returns <see langword="void"/>.
    /// </summary>
    /// <param name="targetDirectory">The target directory representing where the scaffold should be created.</param>
    /// <param name="overwriteExisting">The value indicating whether existing files should be overwritten.</param>
    public static void Scaffold(string targetDirectory, bool overwriteExisting = false)
    {
        new ProjectScaffolder().ScaffoldProject(targetDirectory, overwriteExisting);
    }

    /// <summary>
    /// Creates a scaffold in the target directory with an explicit logger and returns <see langword="void"/>.
    /// </summary>
    /// <param name="targetDirectory">The target directory representing where the scaffold should be created.</param>
    /// <param name="overwriteExisting">The value indicating whether existing files should be overwritten.</param>
    /// <param name="logger">The logger representing where scaffold diagnostics should be emitted.</param>
    public static void Scaffold(string targetDirectory, bool overwriteExisting, ILogger<ProjectScaffolder>? logger)
    {
        new ProjectScaffolder(logger).ScaffoldProject(targetDirectory, overwriteExisting);
    }

    /// <summary>
    /// Creates the complete scaffolded project structure and returns <see langword="void"/>.
    /// </summary>
    /// <param name="targetDirectory">The target directory representing where the scaffold should be created.</param>
    /// <param name="overwriteExisting">The value indicating whether existing files should be overwritten.</param>
    public void ScaffoldProject(string targetDirectory, bool overwriteExisting = false)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Target directory is required.", nameof(targetDirectory));
        }

        string root = Path.GetFullPath(targetDirectory);
        ScaffoldStarted(root, overwriteExisting);
        Directory.CreateDirectory(root);

        CreateDirectory(root, "content");
        CreateDirectory(root, "creatures");
        CreateDirectory(root, "factions");
        CreateDirectory(root, "items");
        CreateDirectory(root, "locations");
        CreateDirectory(root, "snippets");
        CreateDirectory(root, "spells");
        CreateDirectory(root, "players");
        CreateDirectory(root, "maps");
        CreateDirectory(root, "settings");
        CreateDirectory(root, Path.Combine("settings", "fonts"));
        CreateDirectory(root, ".caches");
        CreateDirectory(root, Path.Combine(".caches", "generated"));

        WriteIfMissing(root, ".gitignore", ".caches/\n", overwriteExisting: false);
        WriteIfMissing(root, Path.Combine(".caches", "hashes.json"), "{}\n", overwriteExisting);
        WriteIfMissing(root, Path.Combine(".caches", "topics.json"), "{}\n", overwriteExisting);
        WriteIfMissing(root, Path.Combine(".caches", "state.json"),
            $$"""
            {
              "indexedTopicCount": 0,
              "indexedEntityCount": 0,
              "cachedUtc": "",
              "indexedNamesHashBlake2b512": "{{EmptyBlake2b512Hash}}"
            }
            """,
            overwriteExisting);

        WriteIfMissing(root, "TITLE.md",
            """
            ---
            title: Your Sourcebook Title
            author: Your Name
            description: A short description of your project.
            ---
            # Your Sourcebook Title
            """,
            overwriteExisting);
        WriteIfMissing(root, "AUTHORS.md", "# Authors\nPrimary Author", overwriteExisting);
        WriteIfMissing(root, "LICENSE.md", "# License\nMIT", overwriteExisting);
        WriteIfMissing(root, "README.md", "# Foreword\nWelcome to your sourcebook.", overwriteExisting);
        WriteIfMissing(root, "SOURCES.md", "# Bibliography\n- Source 1", overwriteExisting);

        WriteIfMissing(root, Path.Combine("settings", "global.yml"),
            """
            project:
              title: Your Sourcebook Title
              author: Your Name
            compiler:
              dictionary:
                enabled: false
                unreferenced: false
                shadowReferences: []
              screen:
                columns: 1
              print:
                columns: 2
                pageSize: letter
            """,
            overwriteExisting);
        WriteIfMissing(root, Path.Combine("settings", "html.yml"),
            """
            compiler:
              autoLink: false
              screen:
                pageLevelToc: true
            fonts:
              headings:
                color: "#7a0d0d"
                family: Nodesto Caps Condensed
              body:
                family: Libre Baskerville
            """,
            overwriteExisting);
        WriteIfMissing(root, Path.Combine("settings", "pdf.yml"),
            """
            compiler:
              autoLink: false
            fonts:
              headings:
                color: "#7a0d0d"
                family: Nodesto Caps Condensed
              body:
                family: Libre Baskerville
            """,
            overwriteExisting);
        WriteIfMissing(root, Path.Combine("settings", "foundry.yml"),
            """
            compiler:
              autoLink: false
            foundry:
              packName: your-pack
            """,
            overwriteExisting);
        _defaultFontAssets.CopyMissingToDirectory(Path.Combine(root, "settings", "fonts"));

        WriteIfMissing(root, Path.Combine("content", "001_intro.md"),
            """
            ---
            title: Introduction
            ---
            Welcome to your campaign compendium.
            """,
            overwriteExisting);

        string[] templateCategories =
        [
            "creatures",
            "factions",
            "items",
            "locations",
            "snippets",
            "spells",
            "players",
        ];

        foreach (string category in templateCategories)
        {
            string template = _defaultCategoryTemplates.TryGetTemplate(category)
                ?? throw new InvalidOperationException($"Missing embedded default template for '{category}'.");
            WriteIfMissing(root, Path.Combine(category, "TEMPLATE.md"), template, overwriteExisting);
        }

        ScaffoldCompleted(root);
    }

    /// <summary>
    /// Creates a relative directory under the scaffold root and returns <see langword="void"/>.
    /// </summary>
    /// <param name="root">The scaffold root directory representing the base path.</param>
    /// <param name="relativePath">The relative path representing the directory to create.</param>
    private static void CreateDirectory(string root, string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(root, relativePath));
    }

    /// <summary>
    /// Writes a file when missing, or optionally overwrites existing content, and returns <see langword="void"/>.
    /// </summary>
    /// <param name="root">The scaffold root directory representing the base path.</param>
    /// <param name="relativePath">The relative file path representing the destination file.</param>
    /// <param name="content">The text content representing file contents to write.</param>
    /// <param name="overwriteExisting">The value indicating whether existing files should be overwritten.</param>
    private static void WriteIfMissing(string root, string relativePath, string content, bool overwriteExisting = false)
    {
        string fullPath = Path.Combine(root, relativePath);
        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (!overwriteExisting && File.Exists(fullPath))
        {
            return;
        }

        File.WriteAllText(fullPath, content);
    }

    /// <summary>
    /// Logs that project scaffolding has started for a target directory.
    /// </summary>
    /// <param name="targetDirectory">The target directory representing scaffold destination.</param>
    /// <param name="overwriteExisting">The value indicating whether overwrite mode is enabled.</param>
    [LoggerMessage(EventId = 2120, Level = LogLevel.Debug, Message = "Scaffolding project at {targetDirectory} (overwriteExisting={overwriteExisting}).")]
    private partial void ScaffoldStarted(string targetDirectory, bool overwriteExisting);

    /// <summary>
    /// Logs that project scaffolding has completed for a target directory.
    /// </summary>
    /// <param name="targetDirectory">The target directory representing scaffold destination.</param>
    [LoggerMessage(EventId = 2121, Level = LogLevel.Debug, Message = "Project scaffolding completed at {targetDirectory}.")]
    private partial void ScaffoldCompleted(string targetDirectory);
}
