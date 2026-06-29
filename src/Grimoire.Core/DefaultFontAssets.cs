using System.Reflection;
using System.Collections.Immutable;

namespace Grimoire.Core;

/// <summary>
/// Represents a loader for bundled default font assets used by scaffolded project settings.
/// </summary>
internal sealed class DefaultFontAssets
{
    /// <summary>
    /// An <see cref="Assembly"/> representing the resource container that stores embedded font files.
    /// </summary>
    private readonly Assembly _assembly;

    /// <summary>
    /// Initializes the default font catalog and embedded-resource bindings.
    /// </summary>
    /// <param name="assembly">The optional assembly representing where embedded font resources should be read from.</param>
    internal DefaultFontAssets(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(DefaultFontAssets).Assembly;
        HeadingFontFamily = "Nodesto Caps Condensed";
        BodyFontFamily = "Libre Baskerville";
        Assets =
        [
            new("Nodesto Caps Condensed.otf", HeadingFontFamily, ".otf"),
            new("Libre Baskerville.ttf", BodyFontFamily, ".ttf"),
        ];
    }

    /// <summary>
    /// Gets a <see cref="string"/> representing the default heading font family name.
    /// </summary>
    internal string HeadingFontFamily { get; }

    /// <summary>
    /// Gets a <see cref="string"/> representing the default body font family name.
    /// </summary>
    internal string BodyFontFamily { get; }

    /// <summary>
    /// Gets an <see cref="ImmutableArray{DefaultFontAsset}"/> representing the bundled font assets copied into new project scaffolds.
    /// </summary>
    internal ImmutableArray<DefaultFontAsset> Assets { get; }

    /// <summary>
    /// Copies missing bundled font files into a destination directory and returns <see langword="void"/>.
    /// </summary>
    /// <param name="destinationDirectory">The destination directory representing where font files should be written.</param>
    internal void CopyMissingToDirectory(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        foreach (DefaultFontAsset asset in Assets)
        {
            string targetPath = Path.Combine(destinationDirectory, asset.FileName);
            if (File.Exists(targetPath))
            {
                continue;
            }

            using Stream fontStream = OpenRead(asset);
            using FileStream targetStream = File.Create(targetPath);
            fontStream.CopyTo(targetStream);
        }
    }

    /// <summary>
    /// Opens a bundled font asset stream and returns a <see cref="Stream"/> representing the embedded resource content.
    /// </summary>
    /// <param name="asset">The font asset representing the embedded resource to open.</param>
    /// <returns>A <see cref="Stream"/> representing the requested font file content.</returns>
    internal Stream OpenRead(DefaultFontAsset asset)
    {
        Stream? stream = _assembly.GetManifestResourceStream(asset.ResourceName);
        return stream ?? throw new FileNotFoundException($"Bundled font resource not found: {asset.ResourceName}", asset.ResourceName);
    }
}

/// <summary>
/// Represents metadata describing a bundled default font asset.
/// </summary>
/// <param name="FileName">The file name representing the embedded font resource.</param>
/// <param name="FontFamilyName">The display family name representing this font in scaffolded settings.</param>
/// <param name="Extension">The file extension representing the expected font format.</param>
internal readonly record struct DefaultFontAsset(string FileName, string FontFamilyName, string Extension)
{
    /// <summary>
    /// Gets a <see cref="string"/> representing the fully qualified manifest resource name for this font.
    /// </summary>
    internal string ResourceName => $"Grimoire.Core.Resources.Fonts.{FileName}";
}
