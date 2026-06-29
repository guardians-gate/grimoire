using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Represents the validator that normalizes an output path and infers the compilation export target from its extension.
/// </summary>
public sealed partial class OutputInspector
{
    /// <summary>
    /// A <see cref="StringComparison"/> indicating how output extensions should be compared when selecting export targets.
    /// </summary>
    private const StringComparison ExtensionComparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// A <see cref="ILogger{TCategoryName}"/> representing the diagnostic sink for output-path inspection activity.
    /// </summary>
    private readonly ILogger<OutputInspector> _logger;

    /// <summary>
    /// Initializes a new inspector for compilation output paths.
    /// </summary>
    /// <param name="logger">The logger representing where inspection diagnostics should be emitted.</param>
    public OutputInspector(ILogger<OutputInspector>? logger = null)
    {
        _logger = logger ?? NullLogger<OutputInspector>.Instance;
    }

    /// <summary>
    /// Validates and normalizes the supplied output path and returns an <see cref="OutputInspectionResult"/> representing the resolved destination and target format.
    /// </summary>
    /// <param name="outputPath">The caller-provided output path to inspect.</param>
    /// <returns>An <see cref="OutputInspectionResult"/> representing the normalized output path and inferred export target.</returns>
    public OutputInspectionResult Inspect(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        string normalizedPath = Path.GetFullPath(outputPath.Trim());
        string extension = Path.GetExtension(normalizedPath);
        ExportTarget target = ExportTarget.Website;

        if (string.Equals(extension, ".pdf", ExtensionComparison))
        {
            target = ExportTarget.Pdf;
        }
        else if (string.Equals(extension, ".db", ExtensionComparison))
        {
            target = ExportTarget.FoundryDb;
        }

        OutputResolved(normalizedPath, target);
        return new(normalizedPath, target);
    }

    /// <summary>
    /// Logs the export target inferred from a normalized output path.
    /// </summary>
    /// <param name="path">The normalized output path that was resolved.</param>
    /// <param name="target">The export target inferred from <paramref name="path"/>.</param>
    [LoggerMessage(EventId = 2064, Level = LogLevel.Debug, Message = "Output path resolved to target {target}: {path}.")]
    private partial void OutputResolved(string path, ExportTarget target);
}
