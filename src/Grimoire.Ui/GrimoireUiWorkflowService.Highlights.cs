namespace Grimoire.Ui;

/// <summary>
/// Represents keyword-highlight helpers used by the workflow service when deriving preview navigation hints.
/// </summary>
public sealed partial class GrimoireUiWorkflowService
{
    /// <summary>
    /// Determines whether a relative project path should be ignored and returns a <see cref="bool"/> indicating exclusion status.
    /// </summary>
    /// <param name="relativePath">The relative path representing a project file candidate.</param>
    /// <returns><see langword="true"/> indicating the path should be ignored; otherwise, <see langword="false"/>.</returns>
    private static bool IsIgnoredRelativePath(string relativePath)
    {
        string normalized = ToProjectPath(relativePath);
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => IgnoredProjectDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds keyword highlights from project content and returns a <see cref="List{T}"/> representing detected keyword occurrences.
    /// </summary>
    /// <param name="root">The project root path representing the source tree to scan for entity names.</param>
    /// <param name="content">The content string representing the document text to analyze.</param>
    /// <returns>A <see cref="List{T}"/> representing highlighted keyword matches and their line numbers.</returns>
    private static List<KeywordHighlight> BuildKeywordHighlights(string root, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        HashSet<string> uniqueNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path);
            if (IsIgnoredRelativePath(relativePath))
            {
                continue;
            }

            string extension = Path.GetExtension(path);
            if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
            {
                continue;
            }

            uniqueNames.Add(name);
        }

        string[] names =
        [
            .. uniqueNames
                .OrderByDescending(static name => name.Length)
                .Take(200),
        ];

        List<KeywordHighlight> highlights = [];
        foreach (string name in names)
        {
            int index = content.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            highlights.Add(new KeywordHighlight(name, CountLineNumber(content, index)));
            if (highlights.Count >= 50)
            {
                break;
            }
        }

        return highlights;
    }
}
