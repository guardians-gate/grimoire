using Microsoft.Extensions.Localization;
using System.Collections;
using System.Globalization;
using System.Reflection;
using YamlDotNet.Serialization;

namespace Grimoire.Core.Localization;

/// <summary>
/// Represents a factory that creates Grimoire localization providers backed by embedded YAML resources.
/// </summary>
public sealed class GrimoireLocalizationFactory(Assembly? assembly = null)
{
    /// <summary>
    /// An <see cref="Assembly"/> representing the source of embedded localization resources.
    /// </summary>
    private readonly Assembly _assembly = assembly ?? typeof(GrimoireLocalizationFactory).Assembly;

    /// <summary>
    /// Creates the default localizer and returns an <see cref="IStringLocalizer"/> representing English YAML-backed localization.
    /// </summary>
    /// <returns>An <see cref="IStringLocalizer"/> representing the default Grimoire localization provider.</returns>
    public IStringLocalizer CreateDefault()
    {
        return new YamlStringLocalizer(_assembly, "Grimoire.Core.Resources.Localization.en.yml");
    }
}

/// <summary>
/// Represents an <see cref="IStringLocalizer"/> implementation that flattens YAML documents into colon-delimited keys.
/// </summary>
public sealed class YamlStringLocalizer : IStringLocalizer
{
    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing resolved localization entries keyed by normalized path.
    /// </summary>
    private readonly Dictionary<string, string> _strings;

    /// <summary>
    /// Initializes a YAML-backed localizer from an embedded resource stream.
    /// </summary>
    /// <param name="assembly">The assembly representing where the YAML resource should be loaded from.</param>
    /// <param name="resourceName">The resource name representing the YAML localization document.</param>
    /// <param name="deserializer">The optional YAML deserializer representing parser configuration.</param>
    public YamlStringLocalizer(Assembly assembly, string resourceName, IDeserializer? deserializer = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        IDeserializer deserializer1 = deserializer ?? new DeserializerBuilder().Build();

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            using StreamReader reader = new(stream);
            object? yaml = deserializer1.Deserialize(reader);
            _strings = new(StringComparer.OrdinalIgnoreCase);
            Flatten(null, yaml, _strings);
        }
        else
        {
            throw new FileNotFoundException($"Localization resource not found: {resourceName}", resourceName);
        }
    }

    /// <summary>
    /// Gets a <see cref="LocalizedString"/> representing the matching translation value for a localization key.
    /// </summary>
    /// <param name="name">The localization key representing the requested message.</param>
    /// <returns>A <see cref="LocalizedString"/> representing the resolved value or a resource-not-found fallback.</returns>
    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            return _strings.TryGetValue(name, out string? value)
                ? new LocalizedString(name, value, resourceNotFound: false)
                : new(name, name, resourceNotFound: true);
        }
    }

    /// <summary>
    /// Gets a <see cref="LocalizedString"/> representing the formatted translation value for a localization key and argument set.
    /// </summary>
    /// <param name="name">The localization key representing the requested message template.</param>
    /// <param name="arguments">Formatting arguments representing values to substitute into the template.</param>
    /// <returns>A <see cref="LocalizedString"/> representing the formatted localized text.</returns>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            LocalizedString localized = this[name];
            string value = string.Format(CultureInfo.CurrentCulture, localized.Value, arguments);
            return new(name, value, localized.ResourceNotFound);
        }
    }

    /// <summary>
    /// Enumerates all available localized strings and returns an <see cref="IEnumerable{T}"/> representing key/value localization pairs.
    /// </summary>
    /// <param name="includeParentCultures">The value indicating whether parent-culture resources should be included.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> representing all resolved localization entries.</returns>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return _strings
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new LocalizedString(pair.Key, pair.Value, resourceNotFound: false));
    }

    /// <summary>
    /// Recursively flattens nested YAML nodes into key/value entries and returns <see langword="void"/>.
    /// </summary>
    /// <param name="prefix">The current key prefix representing the node's hierarchical path.</param>
    /// <param name="node">The YAML node representing either a dictionary branch or scalar value.</param>
    /// <param name="strings">The destination dictionary representing flattened localization entries.</param>
    private static void Flatten(string? prefix, object? node, Dictionary<string, string> strings)
    {
        if (node is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                string key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                string path = string.IsNullOrWhiteSpace(prefix) ? key : $"{prefix}:{key}";
                Flatten(path, entry.Value, strings);
            }

            return;
        }

        if (node is not null && !string.IsNullOrWhiteSpace(prefix))
        {
            strings[prefix.Replace('.', ':')] = Convert.ToString(node, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
