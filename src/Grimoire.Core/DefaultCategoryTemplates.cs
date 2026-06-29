using System.Reflection;
using System.Collections.Immutable;

namespace Grimoire.Core;

/// <summary>
/// Represents a loader for bundled category markdown templates used when scaffolding new projects.
/// </summary>
internal sealed class DefaultCategoryTemplates
{
    /// <summary>
    /// An <see cref="Assembly"/> representing the resource container that stores embedded template files.
    /// </summary>
    private readonly Assembly _assembly;

    /// <summary>
    /// An <see cref="ImmutableDictionary{TKey, TValue}"/> representing category-to-resource filename mappings.
    /// </summary>
    private readonly ImmutableDictionary<string, string> _resourceFileByCategory =
        ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["creatures"] = "creatures.md",
                ["factions"] = "factions.md",
                ["items"] = "items.md",
                ["locations"] = "locations.md",
                ["snippets"] = "snippets.md",
                ["spells"] = "spells.md",
                ["players"] = "players.md",
            });

    /// <summary>
    /// A <see cref="Lazy{T}"/> representing deferred loading of template text keyed by category name.
    /// </summary>
    private readonly Lazy<ImmutableDictionary<string, string>> _templates;

    /// <summary>
    /// Initializes a template loader for bundled category defaults.
    /// </summary>
    /// <param name="assembly">The optional assembly representing where embedded template resources should be read from.</param>
    internal DefaultCategoryTemplates(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(DefaultCategoryTemplates).Assembly;
        _templates = new(LoadTemplates);
    }

    /// <summary>
    /// Looks up a template by category name and returns a <see cref="string"/> representing template markdown when available.
    /// </summary>
    /// <param name="categoryName">The category name representing the template to retrieve.</param>
    /// <returns>A <see cref="string"/> representing template content, or <see langword="null"/> when no template exists for the category.</returns>
    internal string? TryGetTemplate(string? categoryName)
    {
        return string.IsNullOrWhiteSpace(categoryName)
            ? null
            : _templates.Value.GetValueOrDefault(categoryName);
    }

    /// <summary>
    /// Loads all configured template resources and returns an <see cref="ImmutableDictionary{TKey, TValue}"/> representing category-to-template content mappings.
    /// </summary>
    /// <returns>An <see cref="ImmutableDictionary{TKey, TValue}"/> representing loaded template text keyed by category name.</returns>
    private ImmutableDictionary<string, string> LoadTemplates()
    {
        Dictionary<string, string> templates = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string categoryName, string fileName) in _resourceFileByCategory)
        {
            string resourceName = $"Grimoire.Core.Resources.Templates.{fileName}";
            using Stream? stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using StreamReader reader = new(stream);
                templates[categoryName] = reader.ReadToEnd();
            }
            else
            {
                throw new FileNotFoundException($"Bundled template resource not found: {resourceName}", resourceName);
            }
        }

        return ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, templates);
    }
}
