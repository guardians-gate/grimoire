using Grimoire.Core.Localization;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Grimoire.Core;

/// <summary>
/// Represents a converter that upgrades Grimoire JSON entity files into front-matter Markdown documents.
/// </summary>
/// <param name="localizer">The optional localizer representing text resources for validation and error messages.</param>
/// <param name="yamlSerializer">The optional YAML serializer representing front-matter emission behavior.</param>
public sealed class JsonMarkdownUpgrader(IStringLocalizer? localizer = null, ISerializer? yamlSerializer = null)
{
    /// <summary>
    /// An <see cref="IStringLocalizer"/> representing localized messages used by upgrade validation and exceptions.
    /// </summary>
    private readonly IStringLocalizer _localizer = localizer ?? new GrimoireLocalizationFactory().CreateDefault();

    /// <summary>
    /// An <see cref="ISerializer"/> representing YAML serialization settings for generated front matter.
    /// </summary>
    private readonly ISerializer _yamlSerializer = yamlSerializer ?? new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .DisableAliases()
        .Build();

    /// <summary>
    /// Upgrades a single file, directory, or wildcard pattern and returns a <see cref="Task{TResult}"/> representing conversion counts.
    /// </summary>
    /// <param name="pathOrPattern">The path or wildcard pattern representing JSON files to upgrade.</param>
    /// <param name="cancellationToken">The cancellation token indicating when the upgrade should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="JsonMarkdownUpgradeSummary"/> indicating how many files were converted.</returns>
    public async Task<JsonMarkdownUpgradeSummary> UpgradeAsync(string pathOrPattern, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pathOrPattern))
        {
            throw new ArgumentException(Text("Core:Upgrade:Errors:MissingPath"), nameof(pathOrPattern));
        }

        string fullPath = Path.GetFullPath(pathOrPattern);
        if (Directory.Exists(fullPath))
        {
            return await UpgradeDirectoryAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(fullPath))
        {
            return await UpgradeFilesAsync([fullPath], cancellationToken).ConfigureAwait(false);
        }

        if (ContainsWildcard(pathOrPattern))
        {
            string? directory = Path.GetDirectoryName(fullPath);
            string filePattern = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Directory.GetCurrentDirectory();
            }

            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(Text("Core:Upgrade:Errors:WildcardDirectoryMissing", directory));
            }

            return await UpgradeFilesAsync(Directory.EnumerateFiles(directory, filePattern, SearchOption.TopDirectoryOnly), cancellationToken).ConfigureAwait(false);
        }

        throw new FileNotFoundException(Text("Core:Upgrade:Errors:PathMissing", fullPath), fullPath);
    }

    /// <summary>
    /// Upgrades all JSON files under a directory and returns a <see cref="Task{TResult}"/> representing conversion counts.
    /// </summary>
    /// <param name="rootDirectory">The root directory representing where recursive JSON discovery should begin.</param>
    /// <param name="cancellationToken">The cancellation token indicating when the upgrade should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="JsonMarkdownUpgradeSummary"/> indicating how many files were converted.</returns>
    public async Task<JsonMarkdownUpgradeSummary> UpgradeDirectoryAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException(Text("Core:Upgrade:Errors:MissingRoot"), nameof(rootDirectory));
        }

        string root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(Text("Core:Upgrade:Errors:DirectoryMissing", root));
        }

        return await UpgradeFilesAsync(Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Upgrades a sequence of JSON files and returns a <see cref="Task{TResult}"/> representing aggregate conversion counts.
    /// </summary>
    /// <param name="jsonPaths">The JSON paths representing candidate files to convert.</param>
    /// <param name="cancellationToken">The cancellation token indicating when the upgrade should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="JsonMarkdownUpgradeSummary"/> indicating how many files were converted.</returns>
    private async Task<JsonMarkdownUpgradeSummary> UpgradeFilesAsync(IEnumerable<string> jsonPaths, CancellationToken cancellationToken)
    {
        int converted = 0;
        foreach (string jsonPath in jsonPaths
                     .Where(static path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                     .Where(static path => !string.Equals(Path.GetFileName(path), "dndb-sync-metadata.json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await UpgradeFileAsync(jsonPath, cancellationToken).ConfigureAwait(false))
            {
                converted++;
            }
        }

        return new JsonMarkdownUpgradeSummary(converted);
    }

    /// <summary>
    /// Determines whether a path value contains wildcard characters and returns a <see cref="bool"/> indicating wildcard usage.
    /// </summary>
    /// <param name="value">The path value representing user input to inspect.</param>
    /// <returns><see langword="true"/> indicating the input contains wildcard characters; otherwise, <see langword="false"/>.</returns>
    private static bool ContainsWildcard(string value)
    {
        return value.Contains('*', StringComparison.Ordinal) || value.Contains('?', StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a localized string by key and returns a <see cref="string"/> representing the localized message value.
    /// </summary>
    /// <param name="key">The localization key representing the requested message.</param>
    /// <returns>A <see cref="string"/> representing the localized message text.</returns>
    private string Text(string key)
    {
        return _localizer[key].Value;
    }

    /// <summary>
    /// Resolves and formats a localized string by key and returns a <see cref="string"/> representing the formatted message value.
    /// </summary>
    /// <param name="key">The localization key representing the requested message template.</param>
    /// <param name="arguments">Formatting arguments representing values to substitute into the template.</param>
    /// <returns>A <see cref="string"/> representing the formatted localized message text.</returns>
    private string Text(string key, params object[] arguments)
    {
        return _localizer[key, arguments].Value;
    }

    /// <summary>
    /// Upgrades a single JSON file to Markdown and returns a <see cref="Task{TResult}"/> representing whether conversion occurred.
    /// </summary>
    /// <param name="jsonPath">The JSON file path representing the source document to upgrade.</param>
    /// <param name="cancellationToken">The cancellation token indicating when conversion should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing a <see cref="bool"/> indicating whether the file was converted.</returns>
    private async Task<bool> UpgradeFileAsync(string jsonPath, CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        Dictionary<string, object?> frontMatter = [];
        string body = string.Empty;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(property.Name, "content", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                body = property.Value.GetString() ?? string.Empty;
                continue;
            }

            frontMatter[property.Name] = ConvertJsonValue(property.Value);
        }

        string markdownPath = Path.ChangeExtension(jsonPath, ".md");
        string yaml = _yamlSerializer.Serialize(frontMatter).TrimEnd();
        string markdown = $"---{Environment.NewLine}{yaml}{Environment.NewLine}---{Environment.NewLine}{body.TrimStart()}";
        await File.WriteAllTextAsync(markdownPath, markdown, cancellationToken).ConfigureAwait(false);
        File.Delete(jsonPath);
        return true;
    }

    /// <summary>
    /// Converts a JSON element into a serializer-friendly CLR value and returns an <see cref="object"/> representing the converted value.
    /// </summary>
    /// <param name="element">The JSON element representing the value to convert.</param>
    /// <returns>An <see cref="object"/> representing the converted scalar, array, or object value.</returns>
    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(static property => property.Name, static property => ConvertJsonValue(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => (object?[])[.. element.EnumerateArray().Select(ConvertJsonValue)],
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out long longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out decimal decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
    }
}
