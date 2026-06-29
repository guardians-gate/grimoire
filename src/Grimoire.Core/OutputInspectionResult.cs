namespace Grimoire.Core;

/// <summary>
/// Represents the inspected and normalized form of a caller-provided compilation output path.
/// </summary>
/// <param name="NormalizedPath">The absolute, normalized path representing where compiler output should be written.</param>
/// <param name="Target">The <see cref="ExportTarget"/> indicating the output format inferred from the destination path.</param>
public sealed record OutputInspectionResult(string NormalizedPath, ExportTarget Target);
