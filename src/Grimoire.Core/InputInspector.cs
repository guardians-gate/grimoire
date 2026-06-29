using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Represents the validator that normalizes a user-provided input path and classifies its source packaging.
/// </summary>
public sealed partial class InputInspector
{
    /// <summary>
    /// A <see cref="StringComparison"/> indicating how file extensions should be compared when identifying ZIP archives.
    /// </summary>
    private const StringComparison ExtensionComparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// A <see cref="ILogger{TCategoryName}"/> representing the diagnostic sink for input-path inspection activity.
    /// </summary>
    private readonly ILogger<InputInspector> _logger;

    /// <summary>
    /// Initializes a new inspector for compilation input paths.
    /// </summary>
    /// <param name="logger">The logger representing where inspection diagnostics should be emitted.</param>
    public InputInspector(ILogger<InputInspector>? logger = null)
    {
        _logger = logger ?? NullLogger<InputInspector>.Instance;
    }

    /// <summary>
    /// Validates and normalizes the supplied input path and returns an <see cref="InputInspectionResult"/> representing the resolved source location and kind.
    /// </summary>
    /// <param name="inputPath">The caller-provided path to inspect as a compilation source.</param>
    /// <returns>An <see cref="InputInspectionResult"/> representing the normalized path and inferred source type.</returns>
    public InputInspectionResult Inspect(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        string normalizedPath = Path.GetFullPath(inputPath.Trim());
        InspectingPath(normalizedPath);
        if (Directory.Exists(normalizedPath))
        {
            InputResolved(normalizedPath, InputSourceKind.Directory);
            return new(normalizedPath, InputSourceKind.Directory);
        }

        if (File.Exists(normalizedPath) && string.Equals(Path.GetExtension(normalizedPath), ".zip", ExtensionComparison))
        {
            InputResolved(normalizedPath, InputSourceKind.ZipArchive);
            return new(normalizedPath, InputSourceKind.ZipArchive);
        }

        throw new ArgumentException("Input must be an existing directory or .zip file.", nameof(inputPath));
    }

    /// <summary>
    /// Logs that inspection has started for a normalized input path.
    /// </summary>
    /// <param name="path">The normalized path currently being inspected.</param>
    [LoggerMessage(EventId = 2062, Level = LogLevel.Debug, Message = "Inspecting input path {path}.")]
    private partial void InspectingPath(string path);

    /// <summary>
    /// Logs the source kind resolved from an inspected input path.
    /// </summary>
    /// <param name="path">The normalized input path that was resolved.</param>
    /// <param name="sourceKind">The source kind inferred for <paramref name="path"/>.</param>
    [LoggerMessage(EventId = 2063, Level = LogLevel.Debug, Message = "Input path resolved to {sourceKind}: {path}.")]
    private partial void InputResolved(string path, InputSourceKind sourceKind);
}
