namespace Grimoire.Core;

/// <summary>
/// Represents the artifact format that the compilation pipeline should generate.
/// </summary>
public enum ExportTarget
{
    /// <summary>
    /// Indicates that the compiler should produce a PDF document suitable for print-oriented distribution.
    /// </summary>
    Pdf,

    /// <summary>
    /// Indicates that the compiler should produce a Foundry-compatible SQLite database.
    /// </summary>
    FoundryDb,

    /// <summary>
    /// Indicates that the compiler should produce a static website output.
    /// </summary>
    Website,
}
