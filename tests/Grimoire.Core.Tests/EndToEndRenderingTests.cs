using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using PuppeteerSharp;
using SkiaSharp;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grimoire.Core.Tests;

/// <summary>
/// Provides end-to-end rendering coverage for website and PDF compilation flows.
/// </summary>
public sealed partial class EndToEndRenderingTests
{
    /// <summary>
    /// Enumerates project fixtures used by rendering tests.
    /// </summary>
    /// <returns>A sequence of project names and source paths.</returns>
    public static IEnumerable<object[]> ProjectCases()
    {
        string repositoryRoot = FindRepositoryRoot();
        yield return ["minimal", string.Empty];

        string projectsRoot = Path.Combine(repositoryRoot, "projects");
        if (!Directory.Exists(projectsRoot))
        {
            yield break;
        }

        string[] projectDirectories = Directory.GetDirectories(projectsRoot, "*", SearchOption.TopDirectoryOnly);
        Array.Sort(projectDirectories, StringComparer.OrdinalIgnoreCase);
        foreach (string projectDirectory in projectDirectories)
        {
            string name = Path.GetFileName(projectDirectory);
            yield return [name, projectDirectory];
        }
    }

    /// <summary>
    /// Verifies that website and PDF outputs render readable, styled content for each project fixture.
    /// </summary>
    /// <param name="projectName">The fixture name.</param>
    /// <param name="sourceProjectPath">The source project path.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Theory]
    [MemberData(nameof(ProjectCases))]
    public async Task CompileWebsiteAndPdfRendersReadableStyledOutputAsync(string projectName, string sourceProjectPath)
    {
        using TestWorkspace workspace = await TestWorkspace.CreateAsync(projectName, sourceProjectPath).ConfigureAwait(true);
        workspace.SeedValidationContent(projectName);

        CompilationPlanner planner = new(new(), new());
        SourcebookCompiler compiler = new();

        string htmlOutputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationRequest htmlRequest = planner.Plan(workspace.ProjectPath, htmlOutputDirectory);
        await compiler.CompileAsync(htmlRequest, CancellationToken.None).ConfigureAwait(true);

        string pdfOutputPath = Path.Combine(workspace.RootPath, $"{workspace.ProjectName}.pdf");
        CompilationRequest pdfRequest = planner.Plan(workspace.ProjectPath, pdfOutputPath);
        await compiler.CompileAsync(pdfRequest, CancellationToken.None).ConfigureAwait(true);

        string htmlPath = Path.Combine(htmlOutputDirectory, "index.html");
        string stylePath = Path.Combine(htmlOutputDirectory, "styles.css");
        Assert.True(File.Exists(htmlPath));
        Assert.True(File.Exists(stylePath));
        Assert.True(File.Exists(pdfOutputPath));

        string htmlContent = await File.ReadAllTextAsync(htmlPath).ConfigureAwait(true);
        Assert.Contains("Chronicle of", htmlContent, StringComparison.Ordinal);
        string[] chapterFiles = Directory.GetFiles(htmlOutputDirectory, "chapter-*.html", SearchOption.TopDirectoryOnly);
        string joinedChapterHtml = string.Join('\n', await Task.WhenAll(chapterFiles.Select(static path => File.ReadAllTextAsync(path))).ConfigureAwait(true));
        Assert.Contains("Degolburg Village Square", joinedChapterHtml, StringComparison.Ordinal);
        Assert.Contains("The bustling village square", joinedChapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"material-entry material-inline\"", joinedChapterHtml, StringComparison.Ordinal);
        bool projectUsesInlineStructuredIncludes = await ProjectContainsInlineStructuredIncludesAsync(workspace.ProjectPath).ConfigureAwait(true);
        if (projectUsesInlineStructuredIncludes)
        {
            Assert.Contains("class=\"material-inline-anchor\"", joinedChapterHtml, StringComparison.Ordinal);
        }

        string styleContent = await File.ReadAllTextAsync(stylePath).ConfigureAwait(true);
        Assert.Contains("Nodesto Caps Condensed", styleContent, StringComparison.Ordinal);
        Assert.Contains("Libre Baskerville", styleContent, StringComparison.Ordinal);
        Assert.Contains(".material-inline-anchor{display:block;margin:.2rem 0 .6rem;}", styleContent, StringComparison.Ordinal);
        Assert.DoesNotContain(".material-inline{", styleContent, StringComparison.Ordinal);

        string browserPath = await ChromiumHost.EnsureBrowserExecutableAsync(CancellationToken.None).ConfigureAwait(true);
        string screenshotPath = chapterFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? htmlPath;
        byte[] htmlPng = await RenderPageScreenshotAsync(new Uri(screenshotPath).AbsoluteUri, browserPath).ConfigureAwait(true);
        (ImageMetrics pdfMetrics, byte[] pdfPng) = RenderPdfFirstPageMetrics(pdfOutputPath);
        ImageMetrics htmlMetrics = AnalyzeImage(htmlPng);

        string artifactsDirectory = Path.Combine(workspace.RootPath, "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        await File.WriteAllBytesAsync(Path.Combine(artifactsDirectory, "html-render.png"), htmlPng).ConfigureAwait(true);
        await File.WriteAllBytesAsync(Path.Combine(artifactsDirectory, "pdf-render.png"), pdfPng).ConfigureAwait(true);

        Assert.True(htmlMetrics.Width >= 1000);
        Assert.True(htmlMetrics.Height >= 1200);
        Assert.True(htmlMetrics.NonBackgroundRatio > 0.18, htmlMetrics.ToString());
        Assert.True(htmlMetrics.DarkPixelRatio >= 0.0015, htmlMetrics.ToString());
        Assert.True(htmlMetrics.UniqueColorEstimate > 550, htmlMetrics.ToString());

        Assert.True(pdfMetrics.Width >= 1000);
        Assert.True(pdfMetrics.Height >= 1200);
        Assert.True(pdfMetrics.NonBackgroundRatio > 0.10, pdfMetrics.ToString());
        Assert.True(pdfMetrics.DarkPixelRatio > 0.001, pdfMetrics.ToString());
        Assert.True(pdfMetrics.UniqueColorEstimate > 400, pdfMetrics.ToString());

        byte[] pdfBytes = await File.ReadAllBytesAsync(pdfOutputPath).ConfigureAwait(true);
        Assert.True(pdfBytes.Length > 10_000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdfBytes.AsSpan(0, 5)));
    }

    /// <summary>
    /// Verifies appendix snippet entries map to valid PDF pages for the NR project.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task CompilePdfRendersAllAppendixMaterialsToPdfPagesForNrAsync()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceProjectPath = Path.Combine(repositoryRoot, "projects", "nr");
        Assert.True(Directory.Exists(sourceProjectPath));

        using TestWorkspace workspace = await TestWorkspace.CreateAsync("nr-appendix-proof", sourceProjectPath).ConfigureAwait(true);
        EnsureAppendixSettingsEnabled(workspace.ProjectPath);

        CompilationPlanner planner = new(new(), new());
        SourcebookCompiler compiler = new();

        string htmlOutputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationRequest htmlRequest = planner.Plan(workspace.ProjectPath, htmlOutputDirectory);
        await compiler.CompileAsync(htmlRequest, CancellationToken.None).ConfigureAwait(true);

        string pdfOutputPath = Path.Combine(workspace.RootPath, "nr-appendix-proof.pdf");
        CompilationRequest pdfRequest = planner.Plan(workspace.ProjectPath, pdfOutputPath);
        await compiler.CompileAsync(pdfRequest, CancellationToken.None).ConfigureAwait(true);

        string htmlPath = Path.Combine(htmlOutputDirectory, "chapter-appendix-snippets.html");
        Assert.True(File.Exists(htmlPath));
        Assert.True(File.Exists(pdfOutputPath));

        string browserPath = await ChromiumHost.EnsureBrowserExecutableAsync(CancellationToken.None).ConfigureAwait(true);
        List<(string Id, int Page)> materialPageMappings = await ResolveAppendixMaterialPagesAsync(new Uri(htmlPath).AbsoluteUri, browserPath).ConfigureAwait(true);
        Assert.True(materialPageMappings.Count > 500, $"Expected hundreds of appendix entries, got {materialPageMappings.Count}.");

        HashSet<string> expectedAnchorIds = await GetExpectedUnreferencedMaterialAnchorIdsAsync(workspace.ProjectPath).ConfigureAwait(true);
        HashSet<string> renderedAnchorIds = materialPageMappings.Select(static mapping => mapping.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectedAnchorIds.Count, renderedAnchorIds.Count);
        Assert.Empty(expectedAnchorIds.Except(renderedAnchorIds, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(renderedAnchorIds.Except(expectedAnchorIds, StringComparer.OrdinalIgnoreCase));

        using IDocReader documentReader = DocLib.Instance.GetDocReader(pdfOutputPath, new PageDimensions(1400, 1900));
        int pageCount = documentReader.GetPageCount();
        Assert.True(pageCount > 0);

        foreach ((string id, int page) in materialPageMappings)
        {
            Assert.True(page >= 1, $"Expected positive mapped page number for '{id}', got {page}.");
        }
    }

    /// <summary>
    /// Captures a PNG screenshot of a rendered HTML page.
    /// </summary>
    /// <param name="url">The page URL.</param>
    /// <param name="browserPath">The Chromium executable path.</param>
    /// <returns>A task that represents the asynchronous operation and yields PNG bytes.</returns>
    private static async Task<byte[]> RenderPageScreenshotAsync(string url, string browserPath)
    {
        LaunchOptions options = new()
        {
            Headless = true,
            ExecutablePath = browserPath,
            Args = ["--allow-file-access-from-files", "--font-render-hinting=medium"],
        };

        IBrowser? browser = null;
        IPage? page = null;
        try
        {
            browser = await Puppeteer.LaunchAsync(options).ConfigureAwait(false);
            page = await browser.NewPageAsync().ConfigureAwait(false);
            await page.SetViewportAsync(new() { Width = 1400, Height = 1900 }).ConfigureAwait(false);
            await page.GoToAsync(url, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle0] }).ConfigureAwait(false);
            await page.EvaluateExpressionAsync("document.fonts ? document.fonts.ready : Promise.resolve()").ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);

            return await page.ScreenshotDataAsync(new() { FullPage = true, Type = ScreenshotType.Png }).ConfigureAwait(false);
        }
        finally
        {
            if (page is not null)
            {
                await page.DisposeAsync().ConfigureAwait(false);
            }

            if (browser is not null)
            {
                await browser.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Computes sampled image metrics from PNG bytes.
    /// </summary>
    /// <param name="png">The PNG byte content.</param>
    /// <returns>An <see cref="ImageMetrics"/> value describing image density and color usage.</returns>
    private static ImageMetrics AnalyzeImage(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        int width = bitmap.Width;
        int height = bitmap.Height;
        int nonBackground = 0;
        int dark = 0;
        HashSet<int> samples = [];

        for (int y = 0; y < height; y += 2)
        {
            for (int x = 0; x < width; x += 2)
            {
                SKColor color = bitmap.GetPixel(x, y);
                int luminance = (int)Math.Round((0.2126 * color.Red) + (0.7152 * color.Green) + (0.0722 * color.Blue), MidpointRounding.AwayFromZero);
                if (luminance < 245)
                {
                    nonBackground++;
                }

                if (luminance < 85)
                {
                    dark++;
                }

                samples.Add((color.Red << 16) | (color.Green << 8) | color.Blue);
            }
        }

        int sampleTotal = ((height + 1) / 2) * ((width + 1) / 2);
        double nonBackgroundRatio = (double)nonBackground / sampleTotal;
        double darkRatio = (double)dark / sampleTotal;
        return new(width, height, nonBackgroundRatio, darkRatio, samples.Count);
    }

    /// <summary>
    /// Renders the first PDF page into PNG and computes image metrics.
    /// </summary>
    /// <param name="pdfPath">The PDF path.</param>
    /// <returns>A tuple containing computed metrics and the PNG bytes.</returns>
    private static (ImageMetrics Metrics, byte[] Png) RenderPdfFirstPageMetrics(string pdfPath)
    {
        using IDocReader? documentReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1400, 1900));
        using IPageReader? pageReader = documentReader.GetPageReader(0);

        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();
        byte[] bgra = pageReader.GetImage();
        using SKBitmap bitmap = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        int pixelIndex = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte b = bgra[pixelIndex];
                byte g = bgra[pixelIndex + 1];
                byte r = bgra[pixelIndex + 2];
                byte a = bgra[pixelIndex + 3];
                bitmap.SetPixel(x, y, new SKColor(r, g, b, a));
                pixelIndex += 4;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        byte[] png = new byte[encoded.Size];
        encoded.AsSpan().CopyTo(png);
        return (AnalyzeImage(png), png);
    }

    /// <summary>
    /// Ensures appendix unreferenced material settings are enabled for HTML and PDF outputs.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    private static void EnsureAppendixSettingsEnabled(string projectPath)
    {
        string settingsPath = Path.Combine(projectPath, "settings");
        Directory.CreateDirectory(settingsPath);

        foreach (string fileName in new[] { "html.yml", "pdf.yml" })
        {
            string path = Path.Combine(settingsPath, fileName);
            string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (existing.Contains("unreferenced:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.WriteAllText(
                path,
                """
                compiler:
                  dictionary:
                    unreferenced: true
                """);
        }
    }

    /// <summary>
    /// Resolves appendix material anchors and their page numbers from rendered HTML.
    /// </summary>
    /// <param name="url">The appendix chapter URL.</param>
    /// <param name="browserPath">The Chromium executable path.</param>
    /// <returns>A task that represents the asynchronous operation and yields anchor-page mappings.</returns>
    private static async Task<List<(string Id, int Page)>> ResolveAppendixMaterialPagesAsync(string url, string browserPath)
    {
        LaunchOptions options = new()
        {
            Headless = true,
            ExecutablePath = browserPath,
            Args = ["--allow-file-access-from-files", "--font-render-hinting=medium"],
        };

        IBrowser? browser = null;
        IPage? page = null;
        try
        {
            browser = await Puppeteer.LaunchAsync(options).ConfigureAwait(false);
            page = await browser.NewPageAsync().ConfigureAwait(false);
            await page.SetViewportAsync(new() { Width = 1400, Height = 1900 }).ConfigureAwait(false);
            await page.GoToAsync(url, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle0] }).ConfigureAwait(false);
            await page.EvaluateExpressionAsync("document.fonts ? document.fonts.ready : Promise.resolve()").ConfigureAwait(false);

            string rawJson = await page.EvaluateExpressionAsync<string>(
                """
                JSON.stringify((() => {
                  const pageHeightPx = 980;
                  const appendix = document.getElementById('appendix-snippets');
                  if (!appendix) return [];
                  const entries = [...appendix.querySelectorAll('.material-entry[id]')];
                  return entries.map((entry) => {
                    const top = entry.getBoundingClientRect().top + window.scrollY;
                    return { id: entry.id, page: Math.floor(top / pageHeightPx) + 1 };
                  });
                })())
                """).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(rawJson);
            List<(string Id, int Page)> result = [];
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    string? id = item.TryGetProperty("id", out JsonElement idProp) ? idProp.GetString() : null;
                    int pageNumber = item.TryGetProperty("page", out JsonElement pageProp) && pageProp.TryGetInt32(out int value) ? value : 0;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result.Add((id, pageNumber));
                    }
                }

                return result;
            }
            finally
            {
                if (page is not null)
                {
                    await page.DisposeAsync().ConfigureAwait(false);
            }

            if (browser is not null)
            {
                await browser.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Computes expected unreferenced material anchor identifiers for a project.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <returns>A task that represents the asynchronous operation and yields expected anchor IDs.</returns>
    private static async Task<HashSet<string>> GetExpectedUnreferencedMaterialAnchorIdsAsync(string projectPath)
    {
        HashSet<string> allMaterials = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories))
        {
            if (!IsReferenceableMaterialPath(projectPath, path))
            {
                continue;
            }

            allMaterials.Add(Path.GetFullPath(path));
        }

        HashSet<string> referenced = await GetReferencedMaterialPathsAsync(projectPath).ConfigureAwait(true);
        HashSet<string> unreferenced = new(allMaterials.Where(path => !referenced.Contains(path)), StringComparer.OrdinalIgnoreCase);
        return unreferenced
            .Select(path => BuildMaterialAnchorId(projectPath, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves structured include references found in project markdown files.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <returns>A task that represents the asynchronous operation and yields referenced material paths.</returns>
    private static async Task<HashSet<string>> GetReferencedMaterialPathsAsync(string projectPath)
    {
        HashSet<string> referenced = new(StringComparer.OrdinalIgnoreCase);
        List<string> markdownFiles = [];
        foreach (string rootFile in new[] { "README.md", "TITLE.md", "SOURCES.md" })
        {
            string path = Path.Combine(projectPath, rootFile);
            if (File.Exists(path))
            {
                markdownFiles.Add(path);
            }
        }

        string contentPath = Path.Combine(projectPath, "content");
        if (Directory.Exists(contentPath))
        {
            markdownFiles.AddRange(Directory.GetFiles(contentPath, "*.md", SearchOption.AllDirectories));
        }

        foreach (string markdownFile in markdownFiles)
        {
            string raw = await File.ReadAllTextAsync(markdownFile).ConfigureAwait(true);
            string body = ExtractMarkdownBody(raw);

            foreach (Match includeMatch in IncludeRegex.Matches(body))
            {
                string includePath = includeMatch.Groups["path"].Value.Trim();
                string normalized = NormalizeIncludePath(includePath);
                if (!IsStructuredInclude(normalized))
                {
                    continue;
                }

                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownFile) ?? projectPath, normalized));
                if (IsReferenceableMaterialPath(projectPath, resolved))
                {
                    referenced.Add(resolved);
                }
            }

            foreach (Match macroMatch in MacroRegex.Matches(body))
            {
                string macroPath = macroMatch.Groups["path"].Value.Trim();
                if (!IsStructuredInclude(macroPath))
                {
                    continue;
                }

                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownFile) ?? projectPath, macroPath));
                if (IsReferenceableMaterialPath(projectPath, resolved))
                {
                    referenced.Add(resolved);
                }
            }
        }

        return referenced;
    }

    /// <summary>
    /// Determines whether a file path points to referenceable material content.
    /// </summary>
    /// <param name="sourceRoot">The source project root path.</param>
    /// <param name="path">The candidate material path.</param>
    /// <returns><see langword="true"/> when the path points to referenceable material; otherwise, <see langword="false"/>.</returns>
    private static bool IsReferenceableMaterialPath(string sourceRoot, string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(Path.GetFileName(path), "TEMPLATE.md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relativePath = Path.GetRelativePath(sourceRoot, path);
        if (!relativePath.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relativePath.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (normalized.StartsWith($".git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($".caches{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith($"settings{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"content{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes a query string from an include path.
    /// </summary>
    /// <param name="includePath">The include path.</param>
    /// <returns>The include path without query parameters.</returns>
    private static string NormalizeIncludePath(string includePath)
    {
        int queryStart = includePath.IndexOf('?', StringComparison.Ordinal);
        return queryStart < 0 ? includePath : includePath[..queryStart];
    }

    /// <summary>
    /// Determines whether an include path targets structured markdown or JSON content.
    /// </summary>
    /// <param name="includePath">The include path.</param>
    /// <returns><see langword="true"/> when the include is structured; otherwise, <see langword="false"/>.</returns>
    private static bool IsStructuredInclude(string includePath)
    {
        string extension = Path.GetExtension(includePath);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a material anchor identifier from a project-relative path.
    /// </summary>
    /// <param name="sourceRoot">The source project root path.</param>
    /// <param name="path">The material path.</param>
    /// <returns>A material anchor identifier string.</returns>
    private static string BuildMaterialAnchorId(string sourceRoot, string path)
    {
        string relativePath = Path.GetRelativePath(sourceRoot, path);
        return $"ref-{BuildSectionId(relativePath)}";
    }

    /// <summary>
    /// Builds a normalized section identifier from a path value.
    /// </summary>
    /// <param name="path">The source path.</param>
    /// <returns>A slug-style section identifier.</returns>
    private static string BuildSectionId(string path)
    {
        string raw = Path.GetFileNameWithoutExtension(path);
        StringBuilder builder = new(raw.Length);
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// Extracts markdown body content after optional YAML front matter.
    /// </summary>
    /// <param name="raw">The raw markdown text.</param>
    /// <returns>The markdown body content.</returns>
    private static string ExtractMarkdownBody(string raw)
    {
        Match fenced = MarkdownFrontMatterRegex.Match(raw);
        return fenced.Success ? fenced.Groups["body"].Value : raw;
    }

    /// <summary>
    /// Determines whether a project contains inline structured include references.
    /// </summary>
    /// <param name="projectPath">The project root path.</param>
    /// <returns>A task that represents the asynchronous operation and yields whether inline includes are present.</returns>
    private static async Task<bool> ProjectContainsInlineStructuredIncludesAsync(string projectPath)
    {
        foreach (string markdownFile in Directory.GetFiles(projectPath, "*.md", SearchOption.AllDirectories))
        {
            string raw = await File.ReadAllTextAsync(markdownFile).ConfigureAwait(true);
            string body = ExtractMarkdownBody(raw);
            foreach (Match includeMatch in IncludeRegex.Matches(body))
            {
                string includePath = includeMatch.Groups["path"].Value.Trim();
                if (!ContainsInlineQueryFlag(includePath))
                {
                    continue;
                }

                string normalized = NormalizeIncludePath(includePath);
                if (IsStructuredInclude(normalized))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an include path contains an inline query flag.
    /// </summary>
    /// <param name="includePath">The include path.</param>
    /// <returns><see langword="true"/> when the include path requests inline rendering; otherwise, <see langword="false"/>.</returns>
    private static bool ContainsInlineQueryFlag(string includePath)
    {
        return includePath.Contains("?inline", StringComparison.OrdinalIgnoreCase) ||
               includePath.Contains("&inline", StringComparison.OrdinalIgnoreCase) ||
               includePath.Contains(";inline", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown image include syntax.
    /// </summary>
    [GeneratedRegex(@"!\[(?<title>[^\]]*)\]\((?<path>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex IncludeRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing macro include syntax.
    /// </summary>
    [GeneratedRegex(@"\$\{(?<path>[^}!]+)(!(?<prop>[^}]+))?\}", RegexOptions.Compiled)]
    private static partial Regex MacroRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown front matter boundaries.
    /// </summary>
    [GeneratedRegex(@"\A---\r?\n(?<yaml>[\s\S]*?)\r?\n---\r?\n(?<body>[\s\S]*)\z", RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex MarkdownFrontMatterRegex { get; }

    /// <summary>
    /// Locates the repository root by traversing parent directories from the test output folder.
    /// </summary>
    /// <returns>The repository root path.</returns>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Grimoire.Core");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    /// <summary>
    /// Represents sampled image metrics used by rendering assertions.
    /// </summary>
    private sealed record ImageMetrics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ImageMetrics"/> record.
        /// </summary>
        /// <param name="width">The image width in pixels.</param>
        /// <param name="height">The image height in pixels.</param>
        /// <param name="nonBackgroundRatio">The ratio of non-background pixels.</param>
        /// <param name="darkPixelRatio">The ratio of dark pixels.</param>
        /// <param name="uniqueColorEstimate">The sampled unique color count estimate.</param>
        public ImageMetrics(int width, int height, double nonBackgroundRatio, double darkPixelRatio, int uniqueColorEstimate)
        {
            Width = width;
            Height = height;
            NonBackgroundRatio = nonBackgroundRatio;
            DarkPixelRatio = darkPixelRatio;
            UniqueColorEstimate = uniqueColorEstimate;
        }

        /// <summary>
        /// Gets or sets a <see cref="int"/> indicating the sampled image width in pixels.
        /// </summary>
        public int Width { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="int"/> indicating the sampled image height in pixels.
        /// </summary>
        public int Height { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="double"/> indicating the sampled ratio of non-background pixels.
        /// </summary>
        public double NonBackgroundRatio { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="double"/> indicating the sampled ratio of dark pixels.
        /// </summary>
        public double DarkPixelRatio { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="int"/> indicating the estimated sampled unique color count.
        /// </summary>
        public int UniqueColorEstimate { get; init; }

        /// <summary>
        /// Formats image metrics for assertion diagnostics.
        /// </summary>
        /// <returns>A compact metrics string.</returns>
        public override string ToString() => FormattableString.Invariant(
            $"WxH={Width}x{Height}, nonBg={NonBackgroundRatio:F3}, dark={DarkPixelRatio:F3}, colors={UniqueColorEstimate}");
    }

    /// <summary>
    /// Creates and cleans isolated filesystem fixtures for rendering tests.
    /// </summary>
    private sealed class TestWorkspace : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestWorkspace"/> class.
        /// </summary>
        /// <param name="projectName">The normalized project name.</param>
        /// <param name="rootPath">The artifact root path.</param>
        /// <param name="projectPath">The copied source project path.</param>
        /// <param name="temporaryProjectRoot">The temporary source root path.</param>
        private TestWorkspace(string projectName, string rootPath, string projectPath, string temporaryProjectRoot)
        {
            ProjectName = projectName;
            RootPath = rootPath;
            ProjectPath = projectPath;
            TemporaryProjectRoot = temporaryProjectRoot;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the normalized project name.
        /// </summary>
        public string ProjectName { get; }

        /// <summary>
        /// Gets a <see cref="string"/> representing the artifact root path for the test run.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Gets a <see cref="string"/> representing the copied source project path used for compilation.
        /// </summary>
        public string ProjectPath { get; }

        /// <summary>
        /// Gets a <see cref="string"/> representing the temporary source project root.
        /// </summary>
        private string TemporaryProjectRoot { get; }

        /// <summary>
        /// Creates a workspace for a project fixture and copies source files when provided.
        /// </summary>
        /// <param name="projectName">The fixture project name.</param>
        /// <param name="sourceProjectPath">The source project path to copy.</param>
        /// <returns>A task that represents the asynchronous operation and yields a workspace instance.</returns>
        public static async Task<TestWorkspace> CreateAsync(string projectName, string sourceProjectPath)
        {
            string repositoryRoot = FindRepositoryRoot();
            string runId = $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            string safeProjectName = SanitizeFolderName(projectName);
            string rootPath = Path.Combine(repositoryRoot, "dist", safeProjectName, runId);
            string temporaryProjectRoot = Path.Combine(Path.GetTempPath(), "grimoire-e2e-source", safeProjectName, runId);
            string destinationProject = Path.Combine(temporaryProjectRoot, "source");
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(temporaryProjectRoot);
            Directory.CreateDirectory(destinationProject);

            if (!string.IsNullOrWhiteSpace(sourceProjectPath) && Directory.Exists(sourceProjectPath))
            {
                CopyDirectory(sourceProjectPath, destinationProject);
            }

            return await Task.FromResult(new TestWorkspace(safeProjectName, rootPath, destinationProject, temporaryProjectRoot)).ConfigureAwait(true);
        }

        /// <summary>
        /// Seeds required content and settings files used by rendering validation tests.
        /// </summary>
        /// <param name="projectName">The project name to embed in seeded files.</param>
        public void SeedValidationContent(string projectName)
        {
            Directory.CreateDirectory(Path.Combine(ProjectPath, "content"));
            Directory.CreateDirectory(Path.Combine(ProjectPath, "locations"));
            Directory.CreateDirectory(Path.Combine(ProjectPath, "settings"));

            File.WriteAllText(
                Path.Combine(ProjectPath, "settings", "html.yml"),
                """
                fonts:
                  headings:
                    color: "#8c1d1d"
                    family: Nodesto Caps Condensed
                  body:
                    family: Libre Baskerville
                """);
            File.WriteAllText(
                Path.Combine(ProjectPath, "settings", "pdf.yml"),
                """
                fonts:
                  headings:
                    family: Nodesto Caps Condensed
                  body:
                    family: Libre Baskerville
                """);
            File.WriteAllText(Path.Combine(ProjectPath, "TITLE.md"), $"# Chronicle of {projectName}\nAn illustrated sourcebook dossier.");
            File.WriteAllText(Path.Combine(ProjectPath, "README.md"), $"# {projectName}\nA generated validation project fixture.");

            File.WriteAllText(
                Path.Combine(ProjectPath, "locations", "degolburg.md"),
                """
name: Degolburg Village Square
description: The bustling village square is filled with merchants, travelers, and citizens.
area: 25
---
# More content can go here.
The granite fountain is ringed by lantern posts and market tents.
""");

            File.WriteAllText(
                Path.Combine(ProjectPath, "content", "001_validation_chapter.md"),
                """
---
title: In the Lantern Roads
---
The city of ${../locations/degolburg.md} is a sprawling ${../locations/degolburg.md!area} square miles.

${../locations/degolburg.md!description}

![District Details](../locations/degolburg.md)
""");
        }

        /// <summary>
        /// Deletes temporary copied project files created for the test workspace.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(TemporaryProjectRoot))
            {
                try
                {
                    Directory.Delete(TemporaryProjectRoot, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            // ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Replaces invalid filesystem characters in a project name.
        /// </summary>
        /// <param name="value">The raw project name.</param>
        /// <returns>A safe folder name.</returns>
        private static string SanitizeFolderName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (Array.IndexOf(invalid, characters[i]) >= 0)
                {
                    characters[i] = '_';
                }
            }

            string sanitized = new string(characters).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "project" : sanitized;
        }

        /// <summary>
        /// Recursively copies all files and directories from a source path to a destination path.
        /// </summary>
        /// <param name="sourcePath">The source directory.</param>
        /// <param name="destinationPath">The destination directory.</param>
        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            foreach (string directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(Path.Combine(destinationPath, relative));
            }

            foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourcePath, file);
                string target = Path.Combine(destinationPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destinationPath);
                File.Copy(file, target, overwrite: true);
            }
        }
    }
}
