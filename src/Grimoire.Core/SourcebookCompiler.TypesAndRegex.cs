using PuppeteerSharp.Media;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Represents internal compiler data contracts and generated regular expressions used across rendering and compilation phases.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Represents parsed markdown content split into front matter and body text.
    /// </summary>
    /// <param name="FrontMatter">The front-matter map representing parsed metadata keys and values.</param>
    /// <param name="Body">The body string representing markdown content after front-matter extraction.</param>
    private sealed record ParsedMarkdown(Dictionary<string, string> FrontMatter, string Body);

    /// <summary>
    /// Represents resolved font settings for heading/body families and accent color.
    /// </summary>
    /// <param name="HeadingFont">The heading font family representing heading typography.</param>
    /// <param name="BodyFont">The body font family representing body typography.</param>
    /// <param name="AccentColor">The accent color representing themed visual emphasis.</param>
    private sealed record FontSettings(string HeadingFont, string BodyFont, string AccentColor);

    /// <summary>
    /// Represents normalized rendering options used by website, preview, and PDF flows.
    /// </summary>
    private sealed record RenderOptions(
        bool IncludeUnreferencedSnippetsInAppendix,
        bool GenerateReferenceDictionary,
        bool IncludePageLevelToc,
        int ScreenAppendixColumns,
        int PrintAppendixColumns,
        bool AutoLinkEntityMentions,
        ImmutableArray<string> ShadowReferences,
        PaperFormat PdfPageFormat);

    /// <summary>
    /// Represents metadata for a font asset copied into rendered output.
    /// </summary>
    /// <param name="Name">The asset name representing logical font identity.</param>
    /// <param name="RelativePath">The relative path representing the copied asset location.</param>
    /// <param name="Extension">The extension representing the expected font format.</param>
    private sealed record FontAsset(string Name, string RelativePath, string Extension);

    /// <summary>
    /// Represents a navigation link entry for generated site structures.
    /// </summary>
    /// <param name="Label">The label representing link display text.</param>
    /// <param name="Href">The href representing link destination.</param>
    private sealed record NavLink(string Label, string Href);

    /// <summary>
    /// Represents an in-page table-of-contents entry.
    /// </summary>
    /// <param name="AnchorId">The anchor identifier representing the target heading.</param>
    /// <param name="Title">The title representing heading display text.</param>
    private sealed record InPageTocEntry(string AnchorId, string Title);

    /// <summary>
    /// Represents a rendered content section ready for site or PDF composition.
    /// </summary>
    private sealed record ContentSection(string Title, string Html, string Id, string? Jumbotron);

    /// <summary>
    /// Represents dynamic project substitution values derived from rendered output.
    /// </summary>
    private sealed record ProjectPageSubstitutionValues(int PageCount, Dictionary<string, int> SeeAlsoPages, Dictionary<string, string> DynamicValues);

    /// <summary>
    /// Represents in-memory preview cache state for expensive rendering context components.
    /// </summary>
    private sealed record PreviewRenderCache(
        RenderOptions RenderOptions,
        List<IndexTopic> IndexTopics,
        Dictionary<string, string> LinkTargets,
        ProjectMetadata Metadata,
        ProjectPageSubstitutionValues Substitutions);

    /// <summary>
    /// Represents a per-file preview cache entry keyed by source timestamp.
    /// </summary>
    private sealed record PreviewFileRenderCacheEntry(long SourceWriteUtcTicks, string Html, IReadOnlyDictionary<string, string> LinkTargets);

    /// <summary>
    /// Represents disk paths used by preview cache persistence.
    /// </summary>
    private sealed record PreviewDiskCachePaths(string CacheRoot, string GeneratedRoot, string HashesPath, string StatePath, string TopicsPath);

    /// <summary>
    /// Represents the result of preview disk-cache validation.
    /// </summary>
    private sealed record PreviewDiskCacheValidation(PreviewDiskCachePaths Paths, Dictionary<string, string> ContentHashes, PreviewCacheStateDocument? State, bool IsValid);

    /// <summary>
    /// Represents summarized topic/index metrics persisted for preview caching.
    /// </summary>
    private sealed record PreviewTopicState(int IndexedTopicCount, int IndexedEntityCount, string IndexedNamesHashBlake2b512);

    /// <summary>
    /// Represents serialized preview cache state metadata stored on disk.
    /// </summary>
    private sealed record PreviewCacheStateDocument(int IndexedTopicCount, int IndexedEntityCount, string CachedUtc, string IndexedNamesHashBlake2b512);

    /// <summary>
    /// Represents serialized generated preview content stored in disk cache.
    /// </summary>
    private sealed record PreviewGeneratedCacheDocument(string RelativePath, string SourceContentHashBlake2b512, string Html, Dictionary<string, string> LinkTargets, string CachedUtc);

    /// <summary>
    /// Represents hydrated generated preview cache content.
    /// </summary>
    private sealed record PreviewGeneratedCacheEntry(string Html, IReadOnlyDictionary<string, string> LinkTargets);

    /// <summary>
    /// Represents an entity mention candidate for reference-dictionary generation.
    /// </summary>
    private sealed record ReferenceDictionaryMentionCandidate(string Title, string SourcePath, string TargetId);

    /// <summary>
    /// Represents cached material-title data keyed by source timestamp.
    /// </summary>
    private sealed record MaterialTitleCacheEntry(long SourceWriteUtcTicks, string Title);

    /// <summary>
    /// Represents rendered markdown text plus template-application metadata.
    /// </summary>
    private readonly record struct RenderedTemplateMarkdown(string Markdown, bool TemplateApplied);

    /// <summary>
    /// Represents all context required for downstream rendering passes.
    /// </summary>
    private sealed record RenderContext(
        string Title,
        string SourceRoot,
        List<ContentSection> Sections,
        string? CoverHtml,
        ProjectMetadata Metadata,
        List<IndexTopic> IndexTopics,
        string? BibliographyHtml,
        FontSettings Settings,
        List<FontAsset> Fonts,
        RenderOptions Options);

    /// <summary>
    /// Represents project metadata used in generated content headers and attribution blocks.
    /// </summary>
    private sealed record ProjectMetadata(string Title, string? Author, string? Description, string? Copyright, string? License, string? CoverJumbotron);

    /// <summary>
    /// Represents an index topic and the set of target identifiers it references.
    /// </summary>
    private sealed record IndexTopic(string Title, string SourcePath, string Id, ImmutableArray<string> TargetIds);

    /// <summary>
    /// Represents a Foundry export entry prepared for database insertion.
    /// </summary>
    private sealed record FoundryEntry(string Id, string Name, string Type, string ContentHtml, string SourcePath);

    /// <summary>
    /// Represents mutable state used while rewriting asset links during output generation.
    /// </summary>
    private sealed record AssetRewriteState(string SourceRoot, string OutputRoot, Dictionary<string, string> RegisteredAssets);

    /// <summary>
    /// Gets a <see cref="Regex"/> representing Handlebars-style each-block token parsing.
    /// </summary>
    [GeneratedRegex(@"\{\{#each\s+(?<path>[^}]+)\}\}(?<body>[\s\S]*?)\{\{\/each\}\}", RegexOptions.Compiled)]
    static partial Regex EachBlockRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing HTML id-attribute extraction.
    /// </summary>
    [GeneratedRegex("""
                    \sid\s*=\s*"(?<id>[^"]+)"
                    """, RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    static partial Regex HtmlIdRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing material entry extraction from rendered HTML sections.
    /// </summary>
    [GeneratedRegex("""<(?:article|aside)\s+id="(?<id>[^"]+)"[^>]*>[\s\S]*?(?:<h4>(?<title>[\s\S]*?)</h4>|<header>(?<title>[\s\S]*?)</header>)""", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    static partial Regex MaterialEntryRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing inline material-anchor extraction.
    /// </summary>
    [GeneratedRegex("""<div\s+id="(?<id>[^"]+)"\s+class="material-inline-anchor">\s*<h4>(?<title>[\s\S]*?)</h4>""", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    static partial Regex InlineAnchorEntryRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing HTML tag stripping.
    /// </summary>
    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    static partial Regex HtmlTagRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing empty template table rows.
    /// </summary>
    [GeneratedRegex(@"^\|\s*[^|]+\s*\|\s*\|\s*$", RegexOptions.Compiled)]
    static partial Regex EmptyTemplateTableRowRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing slash-only template table rows.
    /// </summary>
    [GeneratedRegex(@"^\|\s*[^|]+\s*\|\s*/\s*\|\s*$", RegexOptions.Compiled)]
    static partial Regex EmptyTemplateSlashOnlyRowRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing empty template bullet placeholders.
    /// </summary>
    [GeneratedRegex(@"^- \*\*[^*]+\*\*:\s*$", RegexOptions.Compiled)]
    static partial Regex EmptyTemplateBulletRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown heading lines.
    /// </summary>
    [GeneratedRegex(@"^(?<level>#{1,6})\s+\S+", RegexOptions.Compiled)]
    static partial Regex MarkdownHeadingRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing runs of excess blank lines.
    /// </summary>
    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    static partial Regex ExtraBlankLinesRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown table row lines.
    /// </summary>
    [GeneratedRegex(@"^\|.*\|$", RegexOptions.Compiled)]
    static partial Regex MarkdownTableRowRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown table separator lines.
    /// </summary>
    [GeneratedRegex(@"^\|\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)+\|?\s*$", RegexOptions.Compiled)]
    static partial Regex MarkdownTableSeparatorRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing D&amp;D Beyond character file naming convention parsing.
    /// </summary>
    [GeneratedRegex(@"^(?<id>\d+)-(?<name>.+)$", RegexOptions.Compiled)]
    static partial Regex DndBeyondCharacterFileRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing protected markdown regions excluded from auto-link substitution.
    /// </summary>
    [GeneratedRegex(@"(`[^`]*`|!?\[[^\]]+\]\([^)]+\)|<a\b[^>]*>[\s\S]*?<\/a>|<[^>]+>)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    static partial Regex ProtectedMarkdownRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing D&amp;D Beyond bracket-tag entity mentions.
    /// </summary>
    [GeneratedRegex(@"\[(?:spell|item|creature|monster)\](?<name>.+?)\[/[a-z]+\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    static partial Regex DdbTagMentionRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing project/file substitution token parsing.
    /// </summary>
    [GeneratedRegex(@"\{\{(?<token>[^{}]+)\}\}", RegexOptions.Compiled)]
    static partial Regex ProjectAndFileSubstitutionRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing ATX heading lines in markdown content.
    /// </summary>
    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+.*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    static partial Regex MarkdownAtxHeadingLineRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing setext heading blocks in markdown content.
    /// </summary>
    [GeneratedRegex(@"^(?<heading>[^\r\n]+)\r?\n(?<underline>\s*(?:=+|-+)\s*)$", RegexOptions.Compiled | RegexOptions.Multiline)]
    static partial Regex MarkdownSetextHeadingBlockRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing GitHub alert header lines in markdown blockquotes.
    /// </summary>
    [GeneratedRegex(@"^(?<prefix>\s{0,3}>\s*)\[!(?<type>[A-Za-z]+)\](?<tail>[^\r\n]*)$", RegexOptions.Compiled | RegexOptions.Multiline)]
    static partial Regex GithubAlertHeaderRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing rendered HTML blockquotes that originated from GitHub alerts.
    /// </summary>
    [GeneratedRegex("""<blockquote>\s*<p>\s*<strong>(?<type>Note|Tip|Important|Warning|Caution):</strong>""", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    static partial Regex GithubAlertBlockquoteRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing temporary mention placeholders used during auto-linking.
    /// </summary>
    [GeneratedRegex(@"@@GRIMOIRE(?<index>\d+)@@", RegexOptions.Compiled)]
    static partial Regex MentionPlaceholderRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing project see-also placeholder tokens.
    /// </summary>
    [GeneratedRegex("@@GRIMOIRE_PROJECT_SEEALSO__(?<title>[0-9A-F]+)@@", RegexOptions.Compiled)]
    static partial Regex ProjectSeeAlsoPlaceholderRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing project dynamic-value placeholder tokens.
    /// </summary>
    [GeneratedRegex("@@GRIMOIRE_PROJECT_DYNAMIC__(?<key>[0-9A-F]+)@@", RegexOptions.Compiled)]
    static partial Regex ProjectDynamicPlaceholderRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing href-attribute extraction from HTML anchors.
    /// </summary>
    [GeneratedRegex("href=\"(?<href>[^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    static partial Regex HtmlHrefAttributeRegex { get; }

    /// <summary>
    /// Creates a <see cref="Regex"/> representing strict fenced front-matter parsing with optional body capture.
    /// </summary>
    /// <returns>A <see cref="Regex"/> representing fenced markdown front-matter parsing.</returns>
    [GeneratedRegex(@"\A---\r?\n(?<yaml>[\s\S]*?)\r?\n---(?:\r?\n(?<body>[\s\S]*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownFrontMatterFencedRegex();

    /// <summary>
    /// Creates a <see cref="Regex"/> representing loose front-matter parsing when the opening fence is omitted.
    /// </summary>
    /// <returns>A <see cref="Regex"/> representing loose markdown front-matter parsing.</returns>
    [GeneratedRegex(@"\A(?<yaml>[\s\S]*?)\r?\n---\r?\n(?<body>[\s\S]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownFrontMatterLooseRegex();
}
