namespace Grimoire.Core;

/// <summary>
/// Represents the inspected and normalized form of a caller-provided compilation input path.
/// </summary>
/// <param name="NormalizedPath">The absolute, normalized path representing the validated input location.</param>
/// <param name="SourceKind">The <see cref="InputSourceKind"/> indicating whether the input is a directory or archive.</param>
public sealed record InputInspectionResult(string NormalizedPath, InputSourceKind SourceKind);
