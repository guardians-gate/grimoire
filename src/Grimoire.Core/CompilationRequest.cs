namespace Grimoire.Core;

/// <summary>
/// Represents a normalized compilation request that binds an inspected input source to a resolved output target path.
/// </summary>
/// <param name="InputPath">The absolute input path representing the source directory or archive to compile.</param>
/// <param name="SourceKind">The <see cref="InputSourceKind"/> indicating how <paramref name="InputPath"/> should be interpreted.</param>
/// <param name="OutputPath">The absolute output path representing where compiled artifacts should be written.</param>
/// <param name="Target">The <see cref="ExportTarget"/> indicating which output format should be produced.</param>
public sealed record CompilationRequest(
    string InputPath,
    InputSourceKind SourceKind,
    string OutputPath,
    ExportTarget Target);
