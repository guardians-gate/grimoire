using PuppeteerSharp.Media;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Builds stylesheets and renders website-oriented sourcebook documents.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Loads effective font settings from target configuration files.
    /// </summary>
    private FontSettings LoadFontSettings(string sourceRoot)
    {
        string htmlSettingsPath = Path.Combine(sourceRoot, "settings", "html.yml");
        string pdfSettingsPath = Path.Combine(sourceRoot, "settings", "pdf.yml");
        Dictionary<string, string> htmlSettings = LoadYamlSettings(htmlSettingsPath);
        Dictionary<string, string> pdfSettings = LoadYamlSettings(pdfSettingsPath);

        string headingFont = FirstNonEmpty(
            GetValue(htmlSettings, FontHeadingFamilySettingKey),
            GetValue(pdfSettings, FontHeadingFamilySettingKey),
            _defaultFontAssets.HeadingFontFamily);
        string bodyFont = FirstNonEmpty(
            GetValue(htmlSettings, FontBodyFamilySettingKey),
            GetValue(pdfSettings, FontBodyFamilySettingKey),
            _defaultFontAssets.BodyFontFamily);
        string accentColor = FirstNonEmpty(
            GetValue(htmlSettings, FontHeadingColorSettingKey),
            GetValue(pdfSettings, FontHeadingColorSettingKey),
            "#7a0d0d");

        return new(headingFont, bodyFont, accentColor);
    }

    /// <summary>
    /// Parses configured print page size into a supported paper format.
    /// </summary>
    private static PaperFormat ParsePaperFormat(string? configuredPageSize)
    {
        if (string.IsNullOrWhiteSpace(configuredPageSize))
        {
            return PaperFormat.Letter;
        }

        return configuredPageSize.Trim().ToUpperInvariant() switch
        {
            "A3" => PaperFormat.A3,
            "A4" => PaperFormat.A4,
            "A5" => PaperFormat.A5,
            "LEGAL" => PaperFormat.Legal,
            "TABLOID" => PaperFormat.Tabloid,
            _ => PaperFormat.Letter,
        };
    }

    /// <summary>
    /// Copies project font assets into the output directory.
    /// </summary>
    private List<FontAsset> CopyFontAssets(string fontsSourcePath, string fontsOutputPath)
    {
        List<FontAsset> assets = [];
        Directory.CreateDirectory(fontsOutputPath);
        if (Directory.Exists(fontsSourcePath))
        {
            string[] fontFiles = Directory.GetFiles(fontsSourcePath, "*.*", SearchOption.TopDirectoryOnly);
            Array.Sort(fontFiles, StringComparer.OrdinalIgnoreCase);

            foreach (string file in fontFiles)
            {
                string extension = Path.GetExtension(file);
                if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".woff", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fileName = Path.GetFileName(file);
                string destination = Path.Combine(fontsOutputPath, fileName);
                File.Copy(file, destination, overwrite: true);
                assets.Add(new(Path.GetFileNameWithoutExtension(fileName), $"assets/fonts/{fileName.Replace("\\", "/", StringComparison.Ordinal)}", extension));
            }
        }

        EnsureBundledDefaultFontAssets(fontsOutputPath, assets);
        return assets;
    }

    /// <summary>
    /// Ensures bundled default fonts are available when project fonts do not include them.
    /// </summary>
    private void EnsureBundledDefaultFontAssets(string fontsOutputPath, List<FontAsset> assets)
    {
        foreach (DefaultFontAsset bundledFont in _defaultFontAssets.Assets)
        {
            bool alreadyIncluded = assets.Any(asset => string.Equals(asset.Name, bundledFont.FontFamilyName, StringComparison.OrdinalIgnoreCase));
            if (alreadyIncluded)
            {
                continue;
            }

            string targetPath = Path.Combine(fontsOutputPath, bundledFont.FileName);
            if (!File.Exists(targetPath))
            {
                using Stream source = _defaultFontAssets.OpenRead(bundledFont);
                using FileStream destination = File.Create(targetPath);
                source.CopyTo(destination);
            }

            string relativePath = $"assets/fonts/{bundledFont.FileName.Replace("\\", "/", StringComparison.Ordinal)}";
            assets.Add(new(bundledFont.FontFamilyName, relativePath, bundledFont.Extension));
        }
    }

    /// <summary>
    /// Builds the stylesheet used for rendered website and PDF HTML.
    /// </summary>
    private static string BuildStylesheet(RenderContext context)
    {
        StringBuilder builder = new();
        foreach (FontAsset font in context.Fonts)
        {
            builder.Append("@font-face{font-family:'")
                .Append(font.Name)
                .Append("';src:url('")
                .Append(font.RelativePath)
                .Append("') format('")
                .Append(ToCssFontFormat(font.Extension))
                .AppendLine("');font-display:swap;}");
        }

        builder.Append(":root{--heading-font:'")
            .Append(context.Settings.HeadingFont)
            .Append("',serif;--body-font:'")
            .Append(context.Settings.BodyFont)
            .Append("',serif;--accent:")
            .Append(context.Settings.AccentColor)
            .AppendLine(";--paper:#f8efe1;--ink:#2a1f1a;--line:#d5c5ac;}");
        builder.AppendLine("*{box-sizing:border-box;}");
        builder.AppendLine("body{margin:0;font-family:var(--body-font);background:var(--paper);color:var(--ink);line-height:1.62;}");
        builder.AppendLine(".layout{display:grid;grid-template-columns:minmax(240px,320px) minmax(0,1fr);max-width:1520px;margin:0 auto;min-height:100vh;}");
        builder.AppendLine(".sidebar{padding:1.1rem 1rem 1.5rem;border-right:1px solid var(--line);position:sticky;top:0;align-self:start;height:100vh;overflow:auto;background:#f7efe0;}");
        builder.AppendLine(".sidebar h1{font-family:var(--heading-font);letter-spacing:.04em;font-size:1.7rem;margin:.2rem 0 1rem;color:var(--accent);}");
        builder.AppendLine(".sidebar a{display:block;padding:.32rem .45rem;margin:.14rem 0;border-radius:4px;color:inherit;text-decoration:none;}");
        builder.AppendLine(".sidebar a:hover{background:#efe2cb;}");
        builder.AppendLine("main{padding:2rem 2.6rem 2.6rem;min-width:0;}");
        builder.AppendLine("section{margin-bottom:2.6rem;padding-bottom:1.8rem;border-bottom:1px solid var(--line);}");
        builder.AppendLine("h1,h2,h3{font-family:var(--heading-font);color:var(--accent);line-height:1.15;margin:0 0 .85rem;}");
        builder.AppendLine("section>h2{font-size:2.2rem;letter-spacing:.01em;}");
        builder.AppendLine("h3{font-size:1.3rem;}");
        builder.AppendLine("main p,main ul,main ol,main blockquote{max-width:86ch;}");
        builder.AppendLine("blockquote{margin:.7rem 0 1.1rem;padding:.65rem .9rem;border-left:4px solid #c3ad87;background:#ead9bb;border-radius:4px;}");
        builder.AppendLine("blockquote.alert{border-left-width:6px;padding:.72rem .95rem;}");
        builder.AppendLine("blockquote.alert .alert-title{display:inline-block;font-size:.88em;letter-spacing:.02em;text-transform:uppercase;}");
        builder.AppendLine("blockquote.alert.alert-note{background:#dce9fb;border-left-color:#3a67ad;}");
        builder.AppendLine("blockquote.alert.alert-tip{background:#dff0dc;border-left-color:#3a7d3f;}");
        builder.AppendLine("blockquote.alert.alert-important{background:#ece2f7;border-left-color:#6b4aa5;}");
        builder.AppendLine("blockquote.alert.alert-warning{background:#f9e6c8;border-left-color:#b97810;}");
        builder.AppendLine("blockquote.alert.alert-caution{background:#f4d9d9;border-left-color:#b22f2f;}");
        builder.AppendLine(".cover-page{display:flex;align-items:center;justify-content:center;min-height:70vh;text-align:center;border-bottom:2px solid var(--line);margin-bottom:2rem;}");
        builder.AppendLine(".cover-page .cover-inner{max-width:760px;padding:2rem;}");
        builder.AppendLine(".cover-page h1{font-size:3rem;margin-bottom:.6rem;}");
        builder.AppendLine(".cover-page.jumbotron{background-size:cover;background-position:center;color:#fff;text-shadow:0 2px 8px rgba(0,0,0,.7);}");
        builder.AppendLine(".cover-page.jumbotron .cover-inner{background:rgba(0,0,0,.35);border-radius:12px;}");
        builder.AppendLine(".author-page{border-bottom:1px solid var(--line);padding-bottom:1rem;margin-bottom:2rem;}");
        builder.AppendLine(".page-break-before{break-before:page;page-break-before:always;}");
        builder.AppendLine(".toc-list,.index-list{columns:2;column-gap:2rem;padding-left:1rem;}");
        builder.AppendLine(".toc-item,.index-item{display:flex;justify-content:space-between;gap:.8rem;break-inside:avoid;}");
        builder.AppendLine(".toc-item .toc-page,.index-item .index-page{min-width:1.5rem;text-align:right;font-variant-numeric:tabular-nums;}");
        builder.AppendLine(".index-pages{display:inline-flex;align-items:center;gap:.18rem;flex-wrap:wrap;}");
        builder.AppendLine(".index-separator{opacity:.65;}");
        builder.AppendLine(".page-toc{margin:0 0 1.1rem;padding:.7rem .9rem;border:1px solid var(--line);border-radius:6px;background:#f5ebd9;}");
        builder.AppendLine(".page-toc>summary{cursor:pointer;font-family:var(--heading-font);font-size:1.1rem;list-style:none;}");
        builder.AppendLine(".page-toc>summary::-webkit-details-marker{display:none;}");
        builder.AppendLine(".page-toc[open]>summary{margin-bottom:.5rem;}");
        builder.AppendLine(".page-toc ul{margin:0;padding-left:1.1rem;}");
        builder.AppendLine(".page-toc li{margin:.15rem 0;}");
        builder.AppendLine(".chapter-jumbotron{height:240px;border-radius:8px;background-size:cover;background-position:center;margin:0 0 1rem;}");
        builder.AppendLine(".infobox{float:right;width:min(44%,420px);margin:.2rem 0 1rem 1rem;border:1px solid #9e8c73;background:#f4ead4;border-radius:6px;}");
        builder.AppendLine(".infobox header{font-family:var(--heading-font);background:#ead7b4;color:#522;padding:.45rem .7rem;border-bottom:1px solid #9e8c73;}");
        builder.AppendLine(".infobox .infobox-content{padding:.6rem .8rem;font-size:.95rem;}");
        builder.AppendLine(".appendix-materials .materials-group{margin-bottom:1rem;}");
        builder.Append(".appendix-materials .materials-columns{columns:")
            .Append(context.Options.ScreenAppendixColumns.ToString(CultureInfo.InvariantCulture))
            .AppendLine(";}");
        builder.AppendLine(".material-entry{break-inside:avoid;page-break-inside:avoid;margin:0 0 .75rem;padding:0;}");
        builder.AppendLine(".material-entry h4,.material-inline-anchor h4{font-family:var(--heading-font);font-size:1.45rem;line-height:1.1;margin:0 0 .3rem;color:var(--accent);}");
        builder.AppendLine(".material-hero{display:block;max-width:100%;height:auto;margin:0 auto .45rem;}");
        builder.AppendLine(".infobox .material-hero{margin:0 auto;border-bottom:1px solid #9e8c73;}");
        builder.AppendLine(".material-inline-anchor{display:block;margin:.2rem 0 .6rem;}");
        builder.AppendLine(".material-inline-anchor .material-content{font-size:.95rem;}");
        builder.AppendLine(".material-divider{border:0;border-top:1px solid var(--line);margin:.5rem 0 .9rem;break-inside:avoid;}");
        builder.AppendLine("main table{width:100%;table-layout:auto;border-collapse:collapse;margin:.35rem 0 1rem;border:1px solid #9e8c73;background:#f4ead4;}");
        builder.AppendLine("main th,main td{border:1px solid #bda786;padding:.35rem .55rem;vertical-align:top;word-wrap:break-word;overflow-wrap:anywhere;font-size:.9rem;}");
        builder.AppendLine("main th{font-family:var(--heading-font);font-size:.95rem;background:#ead7b4;color:#522;text-align:left;}");
        builder.AppendLine("main tbody tr:nth-child(even) td{background:#f8f0df;}");
        builder.AppendLine("img{display:block;max-width:100%;height:auto;margin:0 auto;}");
        builder.AppendLine("@media (max-width:980px){.layout{grid-template-columns:1fr}.sidebar{position:static;height:auto;border-right:none;border-bottom:1px solid var(--line)}main{padding:1rem}.infobox{float:none;width:auto;margin:1rem 0;}main table{display:block;overflow-x:auto;}}");
        builder.Append("@media print{.layout{display:block;max-width:none}.sidebar{display:none}main{padding:0}.infobox{break-inside:avoid-page;}section>h2{font-size:26pt;}h3{font-size:18pt;}.material-entry h4{font-size:20pt;}.appendix-materials .materials-columns{columns:")
            .Append(context.Options.PrintAppendixColumns.ToString(CultureInfo.InvariantCulture))
            .AppendLine(";column-gap:1.5rem;}}");
        return builder.ToString();
    }

    /// <summary>
    /// Writes split website documents for home, chapters, index, and bibliography pages.
    /// </summary>
    private async Task WriteWebsiteDocumentsAsync(RenderContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        var sectionPageById = context.Sections.ToDictionary(
            section => section.Id,
            section => BuildSectionPageFileName(section.Id),
            StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> targetPageByAnchor = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> targetLabelByAnchor = new(StringComparer.OrdinalIgnoreCase);
        foreach (ContentSection section in context.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sectionPage = sectionPageById[section.Id];
            foreach (InPageTocEntry entry in BuildSectionPageTocEntries(section))
            {
                cancellationToken.ThrowIfCancellationRequested();
                targetPageByAnchor.TryAdd(entry.AnchorId, sectionPage);
                targetLabelByAnchor.TryAdd(entry.AnchorId, entry.Title);
            }

            foreach (string htmlId in ExtractHtmlElementIds(section.Html))
            {
                cancellationToken.ThrowIfCancellationRequested();
                targetPageByAnchor.TryAdd(htmlId, sectionPage);
            }
        }

        List<NavLink> navigation =
        [
            new("Cover", "index.html#cover"),
            new("Author", "index.html#author"),
            new("Contents", "index.html#toc"),
        ];

        foreach (ContentSection section in context.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            navigation.Add(new(section.Title, $"{sectionPageById[section.Id]}#{section.Id}"));
        }

        navigation.Add(new("Index", "index-topics.html#index"));
        if (!string.IsNullOrWhiteSpace(context.BibliographyHtml))
        {
            navigation.Add(new("Bibliography", "bibliography.html#bibliography"));
        }

        StringBuilder homeBody = new();
        if (context.Options.IncludePageLevelToc)
        {
            homeBody.Append(BuildInPageTocHtml(
                [
                    new("cover", "Cover"),
                    new("author", "Author"),
                    new("toc", "Contents"),
                ]));
        }
        string titlePageClass = string.IsNullOrWhiteSpace(context.Metadata.CoverJumbotron) ? "cover-page" : "cover-page jumbotron";
        string titlePageStyle = string.IsNullOrWhiteSpace(context.Metadata.CoverJumbotron)
            ? string.Empty
            : $" style=\"background-image:url('{EscapeHtml(context.Metadata.CoverJumbotron)}')\"";

        homeBody.Append("<section id=\"cover\" class=\"").Append(titlePageClass).Append('"').Append(titlePageStyle).Append("><div class=\"cover-inner\">")
            .Append("<h1>").Append(EscapeHtml(context.Metadata.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(context.Metadata.Author))
        {
            homeBody.Append("<p>").Append(EscapeHtml(context.Metadata.Author)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.CoverHtml))
        {
            homeBody.Append(context.CoverHtml);
        }

        homeBody.AppendLine("</div></section>");
        homeBody.Append("<section id=\"author\" class=\"author-page\"><h2>")
            .Append(EscapeHtml(context.Metadata.Title))
            .Append("</h2>");
        if (!string.IsNullOrWhiteSpace(context.Metadata.Author))
        {
            homeBody.Append("<p><strong>By:</strong> ").Append(EscapeHtml(context.Metadata.Author)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.Metadata.Description))
        {
            homeBody.Append("<p><strong>Description:</strong> ").Append(EscapeHtml(context.Metadata.Description)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.Metadata.Copyright))
        {
            homeBody.Append("<p><strong>Copyright:</strong> ").Append(EscapeHtml(context.Metadata.Copyright)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(context.Metadata.License))
        {
            homeBody.Append("<p><strong>License:</strong> ").Append(EscapeHtml(context.Metadata.License)).Append("</p>");
        }

        homeBody.AppendLine("</section>");
        homeBody.AppendLine("<section id=\"toc\"><h2>Table of Contents</h2><div class=\"toc-list\">");
        foreach (ContentSection section in context.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            homeBody.Append("<div class=\"toc-item\"><a href=\"")
                .Append(EscapeHtml(sectionPageById[section.Id]))
                .Append('#')
                .Append(EscapeHtml(section.Id))
                .Append("\">")
                .Append(EscapeHtml(section.Title))
                .AppendLine("</a></div>");
        }
        homeBody.AppendLine("</div></section>");

        string homeHtml = BuildWebsiteDocument(
            context,
            navigation,
            homeBody.ToString(),
            context.Title,
            "styles.css");
        await WriteTextFileAsync(Path.Combine(outputDirectory, "index.html"), homeHtml, cancellationToken).ConfigureAwait(false);

        foreach (ContentSection section in context.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StringBuilder sectionBody = new();
            if (context.Options.IncludePageLevelToc)
            {
                sectionBody.Append(BuildInPageTocHtml(BuildSectionPageTocEntries(section)));
            }
            sectionBody.Append("<section id=\"")
                .Append(EscapeHtml(section.Id))
                .Append("\"><h2>")
                .Append(EscapeHtml(section.Title))
                .Append("</h2>");
            if (!string.IsNullOrWhiteSpace(section.Jumbotron))
            {
                sectionBody.Append("<div class=\"chapter-jumbotron\" style=\"background-image:url('")
                    .Append(EscapeHtml(section.Jumbotron))
                    .AppendLine("')\"></div>");
            }

            sectionBody.Append(section.Html).AppendLine("</section>");
            string sectionHtml = BuildWebsiteDocument(
                context,
                navigation,
                sectionBody.ToString(),
                $"{context.Title} - {section.Title}",
                "styles.css");
            await WriteTextFileAsync(Path.Combine(outputDirectory, sectionPageById[section.Id]), sectionHtml, cancellationToken).ConfigureAwait(false);
        }

        StringBuilder indexBody = new();
        if (context.Options.IncludePageLevelToc)
        {
            indexBody.Append(BuildInPageTocHtml([new("index", "Index")]));
        }
        indexBody.AppendLine("<section id=\"index\"><h2>Index</h2><div class=\"index-list\">");
        foreach (IndexTopic topic in context.IndexTopics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            indexBody.Append("<div class=\"index-item\" id=\"")
                .Append(EscapeHtml(topic.Id))
                .Append("\"><span>")
                .Append(EscapeHtml(topic.Title))
                .Append("</span><span class=\"index-pages\">");
            HashSet<string> emittedTargets = new(StringComparer.OrdinalIgnoreCase);
            int renderedLinkCount = 0;
            foreach (string t in topic.TargetIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string targetId = t;
                bool hasMappedPage = targetPageByAnchor.TryGetValue(targetId, out string? pageFile);
                string dedupeKey = hasMappedPage && !string.IsNullOrWhiteSpace(pageFile)
                    ? pageFile
                    : $"id:{targetId}";
                if (!emittedTargets.Add(dedupeKey))
                {
                    continue;
                }

                if (renderedLinkCount > 0)
                {
                    indexBody.Append("<span class=\"index-separator\">,</span>");
                }

                string href = hasMappedPage
                    ? $"{pageFile}#{targetId}"
                    : $"index.html#{targetId}";
                string linkLabel = targetLabelByAnchor.TryGetValue(targetId, out string? label) && !string.IsNullOrWhiteSpace(label)
                    ? label
                    : (renderedLinkCount + 1).ToString(CultureInfo.InvariantCulture);
                indexBody.Append("<a class=\"index-page\" href=\"")
                    .Append(EscapeHtml(href))
                    .Append("\">")
                    .Append(EscapeHtml(linkLabel))
                    .Append("</a>");
                renderedLinkCount++;
            }

            indexBody.AppendLine("</span></div>");
        }
        indexBody.AppendLine("</div></section>");
        string indexHtml = BuildWebsiteDocument(
            context,
            navigation,
            indexBody.ToString(),
            $"{context.Title} - Index",
            "styles.css");
        await WriteTextFileAsync(Path.Combine(outputDirectory, "index-topics.html"), indexHtml, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(context.BibliographyHtml))
        {
            StringBuilder bibliographyBody = new();
            if (context.Options.IncludePageLevelToc)
            {
                bibliographyBody.Append(BuildInPageTocHtml([new("bibliography", "Bibliography")]));
            }
            bibliographyBody.Append("<section id=\"bibliography\"><h2>Bibliography</h2>")
                .Append(context.BibliographyHtml)
                .AppendLine("</section>");
            string bibliographyHtml = BuildWebsiteDocument(
                context,
                navigation,
                bibliographyBody.ToString(),
                $"{context.Title} - Bibliography",
                "styles.css");
            await WriteTextFileAsync(Path.Combine(outputDirectory, "bibliography.html"), bibliographyHtml, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the website file name for a section page.
    /// </summary>
    private static string BuildSectionPageFileName(string sectionId)
    {
        return $"chapter-{sectionId}.html";
    }

    /// <summary>
    /// Extracts HTML element identifiers from rendered HTML content.
    /// </summary>
    private static IEnumerable<string> ExtractHtmlElementIds(string html)
    {
        foreach (Match match in HtmlIdRegex.Matches(html))
        {
            string id = match.Groups["id"].Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    /// <summary>
    /// Builds in-page table-of-contents entries for a rendered section.
    /// </summary>
    private static InPageTocEntry[] BuildSectionPageTocEntries(ContentSection section)
    {
        List<InPageTocEntry> entries = [new(section.Id, section.Title)];
        foreach (Match match in MaterialEntryRegex.Matches(section.Html))
        {
            string id = match.Groups["id"].Value;
            string title = StripHtmlTags(match.Groups["title"].Value).Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            entries.Add(new(id, title));
        }

        foreach (Match match in InlineAnchorEntryRegex.Matches(section.Html))
        {
            string id = match.Groups["id"].Value;
            string title = StripHtmlTags(match.Groups["title"].Value).Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            entries.Add(new(id, title));
        }

        return
        [
            .. entries
                .GroupBy(static entry => entry.AnchorId, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First()),
        ];
    }

    /// <summary>
    /// Builds HTML for an on-page table of contents.
    /// </summary>
    private static string BuildInPageTocHtml(IReadOnlyCollection<InPageTocEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        bool openByDefault = entries.Count <= 15;
        StringBuilder builder = new();
        builder.Append("<details class=\"page-toc\"");
        if (openByDefault)
        {
            builder.Append(" open");
        }

        builder.AppendLine("><summary>On this page</summary><ul>");
        foreach (InPageTocEntry entry in entries)
        {
            builder.Append("<li><a href=\"#")
                .Append(EscapeHtml(entry.AnchorId))
                .Append("\">")
                .Append(EscapeHtml(entry.Title))
                .AppendLine("</a></li>");
        }

        builder.AppendLine("</ul></details>");
        return builder.ToString();
    }

    /// <summary>
    /// Determines whether any render-context content contains project substitution placeholders.
    /// </summary>
    private static bool ContainsProjectSubstitutionPlaceholders(RenderContext context)
    {
        if (context.Sections.Any(static section => ContainsProjectSubstitutionPlaceholders(section.Html)))
        {
            return true;
        }

        return ContainsProjectSubstitutionPlaceholders(context.CoverHtml) ||
               ContainsProjectSubstitutionPlaceholders(context.BibliographyHtml);
    }

    /// <summary>
    /// Determines whether a content block contains project substitution placeholders.
    /// </summary>
    private static bool ContainsProjectSubstitutionPlaceholders(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains(ProjectPageCountPlaceholder, StringComparison.Ordinal) ||
               content.Contains(ProjectSeeAlsoPlaceholderPrefix, StringComparison.Ordinal) ||
               content.Contains(ProjectDynamicPlaceholderPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies computed page substitutions across all render-context HTML content.
    /// </summary>
    private static RenderContext ApplyProjectPageSubstitutions(RenderContext context, ProjectPageSubstitutionValues substitutions)
    {
        List<ContentSection> sections =
        [
            .. context.Sections.Select(section => section with { Html = ReplaceProjectSubstitutionPlaceholders(section.Html, substitutions) }),
        ];
        string? coverHtml = context.CoverHtml is null
            ? null
            : ReplaceProjectSubstitutionPlaceholders(context.CoverHtml, substitutions);
        string? bibliographyHtml = context.BibliographyHtml is null
            ? null
            : ReplaceProjectSubstitutionPlaceholders(context.BibliographyHtml, substitutions);
        return context with
        {
            Sections = sections,
            CoverHtml = coverHtml,
            BibliographyHtml = bibliographyHtml,
        };
    }

    /// <summary>
    /// Replaces project placeholder tokens with computed substitution values.
    /// </summary>
    private static string ReplaceProjectSubstitutionPlaceholders(string content, ProjectPageSubstitutionValues substitutions)
    {
        if (string.IsNullOrWhiteSpace(content) || !ContainsProjectSubstitutionPlaceholders(content))
        {
            return content;
        }

        string replaced = content.Replace(ProjectPageCountPlaceholder, substitutions.PageCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        replaced = ProjectSeeAlsoPlaceholderRegex.Replace(replaced, match =>
        {
            string encoded = match.Groups["title"].Value;
            string title = DecodePlaceholderToken(encoded);
            return substitutions.SeeAlsoPages.TryGetValue(title, out int page)
                ? page.ToString(CultureInfo.InvariantCulture)
                : "#REF";
        });
        replaced = ProjectDynamicPlaceholderRegex.Replace(replaced, match =>
        {
            string encoded = match.Groups["key"].Value;
            string key = DecodePlaceholderToken(encoded);
            return substitutions.DynamicValues.TryGetValue(key, out string? value)
                ? value
                : $"{{{{{key}}}}}";
        });
        return replaced;
    }
}
