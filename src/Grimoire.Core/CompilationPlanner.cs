using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Represents the coordinator that validates input/output paths and assembles a normalized <see cref="CompilationRequest"/>.
/// </summary>
/// <param name="inputInspector">The input inspector representing the strategy for validating and normalizing source paths.</param>
/// <param name="outputInspector">The output inspector representing the strategy for validating destination paths.</param>
/// <param name="inputLogger">The logger representing where planning diagnostics should be emitted.</param>
public sealed partial class CompilationPlanner(
    InputInspector inputInspector,
    OutputInspector outputInspector,
    ILogger<CompilationPlanner>? inputLogger = null)
{
    /// <summary>
    /// A <see cref="InputInspector"/> representing the input-path inspection dependency used to validate compilation sources.
    /// </summary>
    private readonly InputInspector _inputInspector = inputInspector ?? throw new ArgumentNullException(nameof(inputInspector));

    /// <summary>
    /// A <see cref="OutputInspector"/> representing the output-path inspection dependency used to infer destination targets.
    /// </summary>
    private readonly OutputInspector _outputInspector = outputInspector ?? throw new ArgumentNullException(nameof(outputInspector));

    /// <summary>
    /// A <see cref="ILogger{TCategoryName}"/> representing the diagnostic sink for planning activity and resolution details.
    /// </summary>
    private readonly ILogger<CompilationPlanner> _logger = inputLogger ?? NullLogger<CompilationPlanner>.Instance;

    /// <summary>
    /// Resolves and validates the supplied source and destination paths and returns a <see cref="CompilationRequest"/> representing the normalized compilation plan.
    /// </summary>
    /// <param name="inputPath">The caller-supplied input path to inspect.</param>
    /// <param name="outputPath">The caller-supplied output path to inspect.</param>
    /// <returns>A <see cref="CompilationRequest"/> representing the validated source kind and output target.</returns>
    public CompilationRequest Plan(string inputPath, string outputPath)
    {
        PlanRequested(inputPath, outputPath);
        InputInspectionResult input = _inputInspector.Inspect(inputPath);
        OutputInspectionResult output = _outputInspector.Inspect(outputPath);
        PlanResolved(input.NormalizedPath, input.SourceKind, output.NormalizedPath, output.Target);
        return new(
            input.NormalizedPath,
            input.SourceKind,
            output.NormalizedPath,
            output.Target);
    }

    /// <summary>
    /// Logs that planning has started for the supplied input and output paths.
    /// </summary>
    /// <param name="inputPath">The input path received from the caller.</param>
    /// <param name="outputPath">The output path received from the caller.</param>
    [LoggerMessage(EventId = 2060, Level = LogLevel.Debug, Message = "Planning compilation request for input {inputPath} and output {outputPath}.")]
    private partial void PlanRequested(string inputPath, string outputPath);

    /// <summary>
    /// Logs the normalized plan produced after input and output inspection.
    /// </summary>
    /// <param name="sourcePath">The normalized source path selected for compilation.</param>
    /// <param name="sourceKind">The input source kind resolved from <paramref name="sourcePath"/>.</param>
    /// <param name="outputPath">The normalized output path selected for generated artifacts.</param>
    /// <param name="target">The export target resolved from <paramref name="outputPath"/>.</param>
    [LoggerMessage(EventId = 2061, Level = LogLevel.Debug, Message = "Resolved compilation plan: source={sourcePath} ({sourceKind}) output={outputPath} ({target}).")]
    private partial void PlanResolved(string sourcePath, InputSourceKind sourceKind, string outputPath, ExportTarget target);
}
