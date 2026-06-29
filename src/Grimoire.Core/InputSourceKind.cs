namespace Grimoire.Core;

/// <summary>
/// Represents the physical packaging style of the inspected compilation input.
/// </summary>
public enum InputSourceKind
{
    /// <summary>
    /// Indicates that the input path points to a filesystem directory tree.
    /// </summary>
    Directory,

    /// <summary>
    /// Indicates that the input path points to a ZIP archive that must be extracted before compilation.
    /// </summary>
    ZipArchive,
}
