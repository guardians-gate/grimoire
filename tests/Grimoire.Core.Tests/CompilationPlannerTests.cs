namespace Grimoire.Core.Tests;

/// <summary>
/// Represents tests that verify compilation-plan target inference and validation behavior.
/// </summary>
public sealed class CompilationPlannerTests
{
    /// <summary>
    /// A <see cref="CompilationPlanner"/> representing the planner fixture used by these tests.
    /// </summary>
    private readonly CompilationPlanner _planner = new(new(), new());

    /// <summary>
    /// Verifies that a PDF extension resolves to the PDF export target and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void PlanInfersPdfTargetFromPdfOutput()
    {
        using var inputDirectory = TempDirectory.Create();
        string outputPath = Path.Combine(inputDirectory.Path, "book.pdf");

        CompilationRequest request = _planner.Plan(inputDirectory.Path, outputPath);

        Assert.Equal(ExportTarget.Pdf, request.Target);
        Assert.Equal(Path.GetFullPath(outputPath), request.OutputPath);
    }

    /// <summary>
    /// Verifies that a DB extension resolves to the Foundry DB export target and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void PlanInfersFoundryDbTargetFromDbOutput()
    {
        using var inputDirectory = TempDirectory.Create();
        string outputPath = Path.Combine(inputDirectory.Path, "book.db");

        CompilationRequest request = _planner.Plan(inputDirectory.Path, outputPath);

        Assert.Equal(ExportTarget.FoundryDb, request.Target);
        Assert.Equal(Path.GetFullPath(outputPath), request.OutputPath);
    }

    /// <summary>
    /// Verifies that directory-style output resolves to website export and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void PlanInfersWebsiteTargetFromDirectoryStyleOutput()
    {
        using var inputDirectory = TempDirectory.Create();
        string outputPath = Path.Combine(inputDirectory.Path, "site-output");

        CompilationRequest request = _planner.Plan(inputDirectory.Path, outputPath);

        Assert.Equal(ExportTarget.Website, request.Target);
        Assert.Equal(Path.GetFullPath(outputPath), request.OutputPath);
    }

    /// <summary>
    /// Verifies that planning fails when the input path is missing and returns <see langword="void"/>.
    /// </summary>
    [Fact]
    public void PlanRejectsMissingInputPath()
    {
        using var workspace = TempDirectory.Create();
        string missingInput = Path.Combine(workspace.Path, "missing");
        string outputPath = Path.Combine(workspace.Path, "book.pdf");

        ArgumentException ex = Assert.Throws<ArgumentException>(() => _planner.Plan(missingInput, outputPath));
        Assert.Equal("inputPath", ex.ParamName);
    }

    /// <summary>
    /// Represents a disposable temporary directory fixture used by planner tests.
    /// </summary>
    private sealed class TempDirectory : IDisposable
    {
        /// <summary>
        /// A <see cref="bool"/> indicating whether this fixture has already been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a temporary directory fixture rooted at a specific path.
        /// </summary>
        /// <param name="path">The path representing the temporary directory location.</param>
        private TempDirectory(string path)
        {
            Path = path;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the path of the temporary directory fixture.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Creates a temporary directory fixture and returns a <see cref="TempDirectory"/> representing the created workspace.
        /// </summary>
        /// <returns>A <see cref="TempDirectory"/> representing the created temporary directory fixture.</returns>
        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"grimoire-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new(path);
        }

        /// <summary>
        /// Deletes the temporary directory fixture and returns <see langword="void"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
