using PuppeteerSharp;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Grimoire.Core;

/// <summary>
/// Computes page-aware substitutions and shared HTML rendering helpers.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// A <see cref="int"/> indicating the maximum number of index topics rendered into consolidated PDF/probe HTML.
    /// </summary>
    private const int MaximumConsolidatedIndexTopicCount = 1200;

    /// <summary>
    /// A <see cref="string"/> representing the browser script that maps HTML identifiers to page numbers.
    /// </summary>
    private const string ComputeProjectPageMapScript =
        """
        JSON.stringify((() => {
          const pageHeightPx = 980;
          const pages = {};
          for (const el of document.querySelectorAll('[id]')) {
            if (!el.id) continue;
            const top = el.getBoundingClientRect().top + window.scrollY;
            pages[el.id] = Math.floor(top / pageHeightPx) + 1;
          }

          const scrollHeight = Math.max(document.body.scrollHeight, document.documentElement.scrollHeight);
          const totalPages = Math.max(1, Math.floor((scrollHeight - 1) / pageHeightPx) + 1);
          return { totalPages, pages };
        })())
        """;

    /// <summary>
    /// Computes dynamic project substitution values by probing rendered HTML page positions.
    /// </summary>
    private async Task<ProjectPageSubstitutionValues> ComputeProjectPageSubstitutionValuesAsync(RenderContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        string probeHtmlPath = Path.Combine(outputDirectory, "__grimoire_page_probe__.html");
        await WriteTextFileAsync(probeHtmlPath, BuildHtmlDocument(context), cancellationToken).ConfigureAwait(false);

        string browserPath = await ChromiumHost.EnsureBrowserExecutableAsync(_logger, cancellationToken).ConfigureAwait(false);
        string probeUri = new Uri(probeHtmlPath).AbsoluteUri;
        LaunchOptions launchOptions = new()
        {
            Headless = true,
            ExecutablePath = browserPath,
            Args = ["--allow-file-access-from-files", "--font-render-hinting=medium"],
        };

        Dictionary<string, int> pageById = new(StringComparer.OrdinalIgnoreCase);
        int pageCount = 1;
        IBrowser? browser = null;
        IPage? page = null;
        try
        {
            browser = await Puppeteer.LaunchAsync(launchOptions).ConfigureAwait(false);
            page = await browser.NewPageAsync().ConfigureAwait(false);
            await page.GoToAsync(probeUri, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle0] }).ConfigureAwait(false);
            await page.EvaluateExpressionAsync("document.fonts.ready").ConfigureAwait(false);

            string rawJson = await page.EvaluateExpressionAsync<string>(ComputeProjectPageMapScript).ConfigureAwait(false);

            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.TryGetProperty("totalPages", out JsonElement totalPagesElement) &&
                totalPagesElement.TryGetInt32(out int parsedPages))
            {
                pageCount = Math.Max(1, parsedPages);
            }

            if (document.RootElement.TryGetProperty("pages", out JsonElement pagesElement) &&
                pagesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in pagesElement.EnumerateObject())
                {
                    if (property.Value.TryGetInt32(out int pageNumber))
                    {
                        pageById[property.Name] = pageNumber;
                    }
                }
            }
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

            if (File.Exists(probeHtmlPath))
            {
                File.Delete(probeHtmlPath);
            }
        }

        Dictionary<string, int> seeAlsoPages = new(StringComparer.OrdinalIgnoreCase);
        foreach (IndexTopic topic in context.IndexTopics)
        {
            string? dictionaryTarget = topic.TargetIds.FirstOrDefault(static targetId =>
                targetId.StartsWith("dict-ref-", StringComparison.OrdinalIgnoreCase) &&
                !targetId.Contains("-mention-", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(dictionaryTarget))
            {
                continue;
            }

            if (pageById.TryGetValue(dictionaryTarget, out int topicPage))
            {
                seeAlsoPages[topic.Title] = topicPage;
            }
        }

        Dictionary<string, string> dynamicValues = BuildDynamicProjectSubstitutionValues(context);
        return new ProjectPageSubstitutionValues(pageCount, seeAlsoPages, dynamicValues);
    }

    /// <summary>
    /// Computes project page substitutions by rendering a temporary PDF-target context.
    /// </summary>
    private async Task<ProjectPageSubstitutionValues> ComputeProjectPageSubstitutionValuesAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        string temporaryOutputPath = CreateDeterministicTempPath("grimoire-page-substitutions");
        Directory.CreateDirectory(temporaryOutputPath);

        AssetRewriteState? previousAssetRewriteState = _assetRewriteState;
        _assetRewriteState = new AssetRewriteState(
            Path.GetFullPath(sourceRoot),
            Path.GetFullPath(temporaryOutputPath),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        try
        {
            RenderContext context = await BuildRenderContextAsync(sourceRoot, temporaryOutputPath, ExportTarget.Pdf, cancellationToken).ConfigureAwait(false);
            string stylePath = Path.Combine(temporaryOutputPath, "styles.css");
            await WriteTextFileAsync(stylePath, BuildStylesheet(context), cancellationToken).ConfigureAwait(false);
            CopyRegisteredAssets();
            return await ComputeProjectPageSubstitutionValuesAsync(context, temporaryOutputPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _assetRewriteState = previousAssetRewriteState;
            if (Directory.Exists(temporaryOutputPath))
            {
                Directory.Delete(temporaryOutputPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Builds dynamic substitution macro values from rendered context metadata.
    /// </summary>
    private static Dictionary<string, string> BuildDynamicProjectSubstitutionValues(RenderContext context)
    {
        int referenceCount = context.IndexTopics.Count(static topic =>
            topic.TargetIds.Any(static targetId =>
                targetId.StartsWith("dict-ref-", StringComparison.OrdinalIgnoreCase) &&
                !targetId.Contains("-mention-", StringComparison.OrdinalIgnoreCase)));
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["macro.title"] = context.Metadata.Title,
            ["macro.author"] = context.Metadata.Author ?? string.Empty,
            ["macro.license"] = context.Metadata.License ?? string.Empty,
            ["macro.chapterCount"] = context.Sections.Count.ToString(CultureInfo.InvariantCulture),
            ["macro.indexTopicCount"] = context.IndexTopics.Count.ToString(CultureInfo.InvariantCulture),
            ["macro.referenceCount"] = referenceCount.ToString(CultureInfo.InvariantCulture),
            ["macro.dateUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["macro.generatedUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Removes HTML tags from the supplied text value.
    /// </summary>
    private static string StripHtmlTags(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : HtmlTagRegex.Replace(value, string.Empty);
    }

    /// <summary>
    /// Builds a complete website HTML document from navigation and body content.
    /// </summary>
    private static string BuildWebsiteDocument(RenderContext context, IEnumerable<NavLink> navigation, string body, string title, string stylesheetPath)
    {
        StringBuilder nav = new();
        foreach (NavLink link in navigation)
        {
            nav.Append("<a href=\"")
                .Append(EscapeHtml(link.Href))
                .Append("\">")
                .Append(EscapeHtml(link.Label))
                .AppendLine("</a>");
        }

        return $"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="generator" content="Grimoire">
  <title>{EscapeHtml(title)}</title>
  <link rel="stylesheet" href="{EscapeHtml(stylesheetPath)}">
</head>
<body>
  <div class="layout">
    <aside class="sidebar">
      <h1>{EscapeHtml(context.Title)}</h1>
      <nav>
        {nav}
      </nav>
    </aside>
    <main>
      {body}
    </main>
  </div>
</body>
</html>
""";
    }

    /// <summary>
    /// Builds the consolidated HTML document used for PDF generation and probing.
    /// </summary>
    private static string BuildHtmlDocument(RenderContext context)
    {
        StringBuilder nav = new();
        StringBuilder body = new();

        nav.AppendLine("<a href=\"#cover\">Cover</a>");
        nav.AppendLine("<a href=\"#author\">Author</a>");
        nav.AppendLine("<a href=\"#foreword\">Foreword</a>");
        nav.AppendLine("<a href=\"#toc\">Contents</a>");

        string titlePageClass = string.IsNullOrWhiteSpace(context.Metadata.CoverJumbotron) ? "cover-page" : "cover-page jumbotron";
        string titlePageStyle = string.IsNullOrWhiteSpace(context.Metadata.CoverJumbotron)
            ? string.Empty
            : $" style=\"background-image:url('{EscapeHtml(context.Metadata.CoverJumbotron)}')\"";

        body.Append("<section id=\"cover\" class=\"").Append(titlePageClass).Append('"').Append(titlePageStyle).Append("><div class=\"cover-inner\">")
            .Append("<h1>").Append(EscapeHtml(context.Metadata.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(context.Metadata.Author))
        {
            body.Append("<p>").Append(EscapeHtml(context.Metadata.Author)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.CoverHtml))
        {
            body.Append(context.CoverHtml);
        }

        body.AppendLine("</div></section>");

        body.Append("<section id=\"author\" class=\"author-page page-break-before\"><h2>")
            .Append(EscapeHtml(context.Metadata.Title))
            .Append("</h2>");
        if (!string.IsNullOrWhiteSpace(context.Metadata.Author))
        {
            body.Append("<p><strong>By:</strong> ").Append(EscapeHtml(context.Metadata.Author)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.Metadata.Description))
        {
            body.Append("<p><strong>Description:</strong> ").Append(EscapeHtml(context.Metadata.Description)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.Metadata.Copyright))
        {
            body.Append("<p><strong>Copyright:</strong> ").Append(EscapeHtml(context.Metadata.Copyright)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.Metadata.License))
        {
            body.Append("<p><strong>License:</strong> ").Append(EscapeHtml(context.Metadata.License)).Append("</p>");
        }

        body.AppendLine("</section>");

        ContentSection? foreword = context.Sections.FirstOrDefault(static section => string.Equals(section.Id, "foreword", StringComparison.Ordinal));
        if (foreword is not null)
        {
            body.Append("<section id=\"foreword\" class=\"page-break-before\"><h2>Foreword</h2>")
                .Append(foreword.Html)
                .AppendLine("</section>");
        }

        body.AppendLine("<section id=\"toc\"><h2>Table of Contents</h2><div class=\"toc-list\">");
        foreach (ContentSection section in context.Sections.Where(static section => !string.Equals(section.Id, "foreword", StringComparison.Ordinal)))
        {
            body.Append("<div class=\"toc-item\"><a href=\"#")
                .Append(section.Id)
                .Append("\">")
                .Append(EscapeHtml(section.Title))
                .Append("</a><span class=\"toc-page\" data-page-ref=\"")
                .Append(section.Id)
                .AppendLine("\"></span></div>");
        }
        body.AppendLine("</div></section>");

        foreach (ContentSection section in context.Sections)
        {
            if (string.Equals(section.Id, "foreword", StringComparison.Ordinal))
            {
                continue;
            }

            nav.Append("<a href=\"#").Append(section.Id).Append("\">").Append(EscapeHtml(section.Title)).AppendLine("</a>");
            body.Append("<section id=\"").Append(section.Id).Append("\" class=\"page-break-before\"><h2>").Append(EscapeHtml(section.Title)).Append("</h2>");
            if (!string.IsNullOrWhiteSpace(section.Jumbotron))
            {
                body.Append("<div class=\"chapter-jumbotron\" style=\"background-image:url('")
                    .Append(EscapeHtml(section.Jumbotron))
                    .AppendLine("')\"></div>");
            }

            body.Append(section.Html).AppendLine("</section>");
        }

        nav.AppendLine("<a href=\"#index\">Index</a>");
        body.AppendLine("<section id=\"index\" class=\"page-break-before\"><h2>Index</h2><div class=\"index-list\">");
        int renderedIndexTopicCount = Math.Min(context.IndexTopics.Count, MaximumConsolidatedIndexTopicCount);
        for (int topicIndex = 0; topicIndex < renderedIndexTopicCount; topicIndex++)
        {
            IndexTopic topic = context.IndexTopics[topicIndex];
            body.Append("<div class=\"index-item\" id=\"")
                .Append(topic.Id)
                .Append("\"><span>")
                .Append(EscapeHtml(topic.Title))
                .Append("</span><span class=\"index-pages\">");
            string? primaryTargetId = topic.TargetIds.FirstOrDefault(static targetId => !string.IsNullOrWhiteSpace(targetId));
            if (!string.IsNullOrWhiteSpace(primaryTargetId))
            {
                body.Append("<span class=\"index-page\" data-page-ref=\"")
                    .Append(primaryTargetId)
                    .Append("\"></span>");
            }

            body.AppendLine("</span></div>");
        }

        if (context.IndexTopics.Count > renderedIndexTopicCount)
        {
            body.Append("<p class=\"index-truncated-note\">Index truncated for print performance. Showing ")
                .Append(renderedIndexTopicCount.ToString(CultureInfo.InvariantCulture))
                .Append(" of ")
                .Append(context.IndexTopics.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" entries.</p>");
        }

        body.AppendLine("</div></section>");

        if (!string.IsNullOrWhiteSpace(context.BibliographyHtml))
        {
            nav.AppendLine("<a href=\"#bibliography\">Bibliography</a>");
            body.Append("<section id=\"bibliography\" class=\"page-break-before\"><h2>Bibliography</h2>")
                .Append(context.BibliographyHtml)
                .AppendLine("</section>");
        }

        return $"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="generator" content="Grimoire">
  <title>{EscapeHtml(context.Title)}</title>
  <link rel="stylesheet" href="styles.css">
</head>
<body>
  <div class="layout">
    <aside class="sidebar">
      <h1>{EscapeHtml(context.Title)}</h1>
      <nav>
        {nav}
      </nav>
    </aside>
    <main>
      {body}
    </main>
  </div>
</body>
</html>
""";
    }

    /// <summary>
    /// Builds a section identifier from a file path or name.
    /// </summary>
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
    /// Resolves the display title for a markdown section.
    /// </summary>
    private static string ResolveSectionTitle(string path, ParsedMarkdown parsed)
    {
        if (parsed.FrontMatter.TryGetValue("title", out string? title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(NormalizeDashedToken(Path.GetFileNameWithoutExtension(path)));
    }

    /// <summary>
    /// Resolves the preferred title from JSON material content.
    /// </summary>
    private static string? ResolvePreferredJsonTitle(JsonElement root, string filePath)
    {
        string? directory = Path.GetFileName(Path.GetDirectoryName(filePath));
        bool isItem = string.Equals(directory, "items", StringComparison.OrdinalIgnoreCase);
        bool isSpell = string.Equals(directory, "spells", StringComparison.OrdinalIgnoreCase);

        if (isItem)
        {
            string? itemName = TryGetJsonStringValue(root, "name");
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                return NormalizeEntityTitle(itemName);
            }
        }

        if (isSpell)
        {
            string? definitionName = TryGetJsonStringValue(root, "definition.name");
            if (!string.IsNullOrWhiteSpace(definitionName))
            {
                return NormalizeEntityTitle(definitionName);
            }
        }

        string? title = TryGetJsonStringValue(root, "title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            return NormalizeEntityTitle(title);
        }

        string? name = TryGetJsonStringValue(root, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return NormalizeEntityTitle(name);
        }

        string? characterName = TryGetJsonStringValue(root, "ddb.character.name");
        return !string.IsNullOrWhiteSpace(characterName) ? NormalizeEntityTitle(characterName) : null;
    }

    /// <summary>
    /// Tries to resolve a formatted JSON string value at the specified path.
    /// </summary>
    private static string? TryGetJsonStringValue(JsonElement root, string path)
    {
        if (!TryResolveJsonPath(root, path, out JsonElement value))
        {
            return null;
        }

        string text = JsonValueFormatter.ToDisplayString(value, path);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Normalizes an entity title for consistent display formatting.
    /// </summary>
    private static string NormalizeEntityTitle(string value)
    {
        return JsonValueFormatter.NormalizeEntityText(value, "title");
    }

    /// <summary>
    /// Encodes a placeholder token using hexadecimal UTF-8 text.
    /// </summary>
    private static string EncodePlaceholderToken(string value)
    {
        return Convert.ToHexString(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Decodes a hexadecimal placeholder token to plain text.
    /// </summary>
    private static string DecodePlaceholderToken(string encoded)
    {
        return string.IsNullOrWhiteSpace(encoded) ? string.Empty : Encoding.UTF8.GetString(Convert.FromHexString(encoded));
    }

    /// <summary>
    /// Escapes text for safe insertion into HTML attributes and content.
    /// </summary>
    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts a font file extension to its CSS <c>format()</c> value.
    /// </summary>
    private static string ToCssFontFormat(string extension)
    {
        if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase))
        {
            return "truetype";
        }

        if (extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
        {
            return "opentype";
        }

        if (extension.Equals(".woff", StringComparison.OrdinalIgnoreCase))
        {
            return "woff";
        }

        return extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase) ? "woff2" : "truetype";
    }

    /// <summary>
    /// Creates a deterministic-named temporary path for intermediate output.
    /// </summary>
    private static string CreateDeterministicTempPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Writes text content to a file using the compiler's UTF-8 encoding settings.
    /// </summary>
    private async Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        StreamWriter writer = new(stream, _utf8Encoding);
        await using (stream)
        await using (writer)
            await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}
