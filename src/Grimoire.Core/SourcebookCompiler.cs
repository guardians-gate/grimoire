using Markdig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;
using System.Diagnostics;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Compiles sourcebook projects into export artifacts and preview output.
/// </summary>
/// <param name="logger">A <see cref="ILogger{TCategoryName}"/> representing the logger used for compilation diagnostics.</param>
public sealed partial class SourcebookCompiler(ILogger<SourcebookCompiler>? logger = null)
{
    /// <summary>
    /// A <see cref="string"/> representing the project setting key that enables dictionary generation.
    /// </summary>
    private const string CompilerDictionaryEnabledSettingKey = "compiler.dictionary.enabled";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key that controls unreferenced dictionary entries.
    /// </summary>
    private const string CompilerDictionaryUnreferencedSettingKey = "compiler.dictionary.unreferenced";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key that controls dictionary shadow-reference reporting.
    /// </summary>
    private const string CompilerDictionaryShadowReferencesSettingKey = "compiler.dictionary.shadowReferences";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key that controls screen page-level table-of-contents output.
    /// </summary>
    private const string CompilerScreenPageLevelTocSettingKey = "compiler.screen.pageLevelToc";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key that controls appendix column count for screen output.
    /// </summary>
    private const string CompilerScreenAppendixColumnsSettingKey = "compiler.screen.columns";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key that controls appendix column count for print output.
    /// </summary>
    private const string CompilerPrintAppendixColumnsSettingKey = "compiler.print.columns";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key that controls print page size.
    /// </summary>
    private const string CompilerPrintPageSizeSettingKey = "compiler.print.pageSize";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key that controls automatic entity mention linking.
    /// </summary>
    private const string CompilerAutoLinkSettingKey = "compiler.autoLink";

    /// <summary>
    /// A <see cref="string"/> representing the placeholder token replaced with the rendered project page count.
    /// </summary>
    private const string ProjectPageCountPlaceholder = "@@GRIMOIRE_PROJECT_PAGECOUNT@@";

    /// <summary>
    /// A <see cref="string"/> representing the prefix used for generated project see-also placeholders.
    /// </summary>
    private const string ProjectSeeAlsoPlaceholderPrefix = "@@GRIMOIRE_PROJECT_SEEALSO__";

    /// <summary>
    /// A <see cref="string"/> representing the suffix used for generated project see-also placeholders.
    /// </summary>
    private const string ProjectSeeAlsoPlaceholderSuffix = "@@";

    /// <summary>
    /// A <see cref="string"/> representing the prefix used for generated dynamic project placeholders.
    /// </summary>
    private const string ProjectDynamicPlaceholderPrefix = "@@GRIMOIRE_PROJECT_DYNAMIC__";

    /// <summary>
    /// A <see cref="string"/> representing the suffix used for generated dynamic project placeholders.
    /// </summary>
    private const string ProjectDynamicPlaceholderSuffix = "@@";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for the project title.
    /// </summary>
    private const string ProjectTitleSettingKey = "project.title";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for a single project author value.
    /// </summary>
    private const string ProjectAuthorSettingKey = "project.author";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for multiple project author values.
    /// </summary>
    private const string ProjectAuthorsSettingKey = "project.authors";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for the project description.
    /// </summary>
    private const string ProjectDescriptionSettingKey = "project.description";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for the copyright statement.
    /// </summary>
    private const string ProjectCopyrightSettingKey = "project.copyright";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for license text.
    /// </summary>
    private const string ProjectLicenseSettingKey = "project.license";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for jumbotron configuration.
    /// </summary>
    private const string ProjectJumbotronSettingKey = "project.jumbotron";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for heading font family selection.
    /// </summary>
    private const string FontHeadingFamilySettingKey = "fonts.headings.family";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for body font family selection.
    /// </summary>
    private const string FontBodyFamilySettingKey = "fonts.body.family";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for heading font color selection.
    /// </summary>
    private const string FontHeadingColorSettingKey = "fonts.headings.color";

    /// <summary>
    /// A <see cref="string"/> representing the project setting key for foundry pack name configuration.
    /// </summary>
    private const string FoundryPackNameSettingKey = "foundry.packName";

    /// <summary>
    /// A <see cref="int"/> indicating the maximum number of bytes permitted for embedded preview assets.
    /// </summary>
    private const int MaxEmbeddedPreviewAssetBytes = 4 * 1024 * 1024;

    /// <summary>
    /// A <see cref="string"/> representing the preview cache directory name under a project root.
    /// </summary>
    private const string PreviewCachesDirectoryName = ".caches";

    /// <summary>
    /// A <see cref="string"/> representing the preview cache file name that stores source content hashes.
    /// </summary>
    private const string PreviewCacheHashesFileName = "hashes.json";

    /// <summary>
    /// A <see cref="string"/> representing the preview cache file name that stores project cache state.
    /// </summary>
    private const string PreviewCacheStateFileName = "state.json";

    /// <summary>
    /// A <see cref="string"/> representing the preview cache file name that stores indexed topics.
    /// </summary>
    private const string PreviewCacheTopicsFileName = "topics.json";

    /// <summary>
    /// A <see cref="string"/> representing the preview cache subdirectory name that stores generated preview fragments.
    /// </summary>
    private const string PreviewCacheGeneratedDirectoryName = "generated";

    /// <summary>
    /// A <see cref="ImmutableArray{T}"/> representing directory names ignored during preview cache hashing.
    /// </summary>
    private static readonly ImmutableArray<string> PreviewIgnoredDirectories = [".git", "bin", "obj", ".caches"];

    /// <summary>
    /// Gets a <see cref="Regex"/> representing the macro token syntax used during markdown preprocessing.
    /// </summary>
    [GeneratedRegex(@"\$\{(?<path>[^}!]+)(!(?<prop>[^}]+))?\}", RegexOptions.Compiled)]
    static partial Regex MacroRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing markdown include directives.
    /// </summary>
    [GeneratedRegex(@"!\[(?<title>[^\]]*)\]\((?<path>[^)]+)\)", RegexOptions.Compiled)]
    static partial Regex IncludeRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing a permissive front matter key prefix matcher.
    /// </summary>
    [GeneratedRegex(@"^[a-z][A-Za-z0-9_.-]*\s*:", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    static partial Regex LooseFrontMatterKeyRegex { get; }

    /// <summary>
    /// A <see cref="IDeserializer"/> representing the YAML parser used to read project metadata.
    /// </summary>
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    /// <summary>
    /// A <see cref="MarkdownPipeline"/> representing the Markdig pipeline used to render markdown.
    /// </summary>
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>
    /// A <see cref="JsonSerializerOptions"/> representing JSON serialization options for Foundry export artifacts.
    /// </summary>
    private static readonly JsonSerializerOptions FoundryJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// A <see cref="JsonSerializerOptions"/> representing indented JSON serialization options for preview cache files.
    /// </summary>
    private static readonly JsonSerializerOptions PreviewCacheJsonIndentedOptions = new() { WriteIndented = true };

    /// <summary>
    /// A <see cref="JsonSerializerOptions"/> representing compact JSON serialization options for preview cache files.
    /// </summary>
    private static readonly JsonSerializerOptions PreviewCacheJsonCompactOptions = new() { WriteIndented = false };

    /// <summary>
    /// A <see cref="ImmutableHashSet{T}"/> representing supported markdown alert titles.
    /// </summary>
    private static readonly ImmutableHashSet<string> SupportedAlertTitles =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "Note", "Tip", "Important", "Warning", "Caution");

    /// <summary>
    /// A <see cref="ImmutableHashSet{T}"/> representing supported dynamic project substitution tokens.
    /// </summary>
    private static readonly ImmutableHashSet<string> DynamicProjectSubstitutionTokens =
    [
        "macro.title",
        "macro.author",
        "macro.license",
        "macro.chapterCount",
        "macro.indexTopicCount",
        "macro.referenceCount",
        "macro.dateUtc",
        "macro.generatedUtc",
    ];

    /// <summary>
    /// A <see cref="ImmutableArray{T}"/> representing Foundry collection names generated by export.
    /// </summary>
    private static readonly ImmutableArray<string> FoundryCollections = ["journal"];

    /// <summary>
    /// A <see cref="ILogger{TCategoryName}"/> representing the logger used by this compiler instance.
    /// </summary>
    private readonly ILogger<SourcebookCompiler> _logger = logger ?? NullLogger<SourcebookCompiler>.Instance;

    /// <summary>
    /// A <see cref="DefaultFontAssets"/> representing bundled default font asset metadata.
    /// </summary>
    private readonly DefaultFontAssets _defaultFontAssets = new();

    /// <summary>
    /// A <see cref="Encoding"/> representing UTF-8 text encoding without a byte-order mark.
    /// </summary>
    private readonly Encoding _utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing resolved entity mention links keyed by mention text.
    /// </summary>
    private readonly Dictionary<string, string> _entityMentionLinks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing possible entity mention link targets keyed by mention text.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _entityMentionTargets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing preferred target identifiers keyed by mention text.
    /// </summary>
    private readonly Dictionary<string, string> _entityMentionPreferredTargetIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing source paths associated with entity mention link keys.
    /// </summary>
    private readonly Dictionary<string, string> _entityMentionSourcePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing entity lookup target identifiers keyed by entity name.
    /// </summary>
    private readonly Dictionary<string, string> _entityLookupByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing compiled link exclusion regular expressions keyed by material path.
    /// </summary>
    private readonly Dictionary<string, Regex?> _excludeLinksRegexCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing heading-link enablement by material path.
    /// </summary>
    private readonly Dictionary<string, bool> _headingLinksEnabledCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing cached referenceable material paths keyed by source root.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _referenceableMaterialPathsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing cached reference dictionary mention candidates keyed by source root.
    /// </summary>
    private readonly Dictionary<string, List<ReferenceDictionaryMentionCandidate>> _referenceDictionaryMentionCandidatesCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing cached material titles keyed by absolute material path.
    /// </summary>
    private readonly Dictionary<string, MaterialTitleCacheEntry> _materialTitleCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="IReadOnlyDictionary{TKey, TValue}"/> representing active project substitution values for the current render scope.
    /// </summary>
    private IReadOnlyDictionary<string, string> _activeProjectSubstitutionSettings = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A <see cref="string"/> representing the source root currently associated with entity lookup caches.
    /// </summary>
    private string? _entityLookupSourceRoot;

    /// <summary>
    /// A <see cref="AssetRewriteState"/> representing cached state for rewriting asset references.
    /// </summary>
    private AssetRewriteState? _assetRewriteState;

    /// <summary>
    /// A <see cref="bool"/> indicating whether automatic entity mention linking is currently enabled.
    /// </summary>
    private bool _autoLinkEntityMentions;

    /// <summary>
    /// A <see cref="PreviewRenderCache"/> representing the current preview project cache snapshot.
    /// </summary>
    private PreviewRenderCache? _previewRenderCache;

    /// <summary>
    /// A <see cref="string"/> representing the project root associated with the active preview cache snapshot.
    /// </summary>
    private string? _previewRenderCacheRoot;

    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> representing per-file in-memory preview render cache entries.
    /// </summary>
    private readonly Dictionary<string, PreviewFileRenderCacheEntry> _previewFileRenderCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clears in-memory and on-demand lookup state used by preview rendering.
    /// </summary>
    public void InvalidatePreviewCache()
    {
        _previewRenderCache = null;
        _previewRenderCacheRoot = null;
        _previewFileRenderCache.Clear();
        _entityLookupByName.Clear();
        _entityLookupSourceRoot = null;
        _excludeLinksRegexCache.Clear();
        _headingLinksEnabledCache.Clear();
        _referenceableMaterialPathsCache.Clear();
        _referenceDictionaryMentionCandidatesCache.Clear();
    }

    /// <summary>
    /// Compiles a sourcebook project for the requested export target.
    /// </summary>
    /// <param name="request">A <see cref="CompilationRequest"/> representing the compilation input and output settings.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> indicating whether the operation should be canceled.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous compilation operation.</returns>
    public async Task CompileAsync(CompilationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        StartingCompilation(request.Target, request.SourceKind, request.InputPath, request.OutputPath);

        string? extractionRoot = null;
        string sourceRoot = request.InputPath;
        if (request.SourceKind == InputSourceKind.ZipArchive)
        {
            extractionRoot = CreateDeterministicTempPath("grimoire-src");
            ExtractingArchive(request.InputPath, extractionRoot);
            await ZipFile.ExtractToDirectoryAsync(request.InputPath, extractionRoot, cancellationToken).ConfigureAwait(false);
            sourceRoot = extractionRoot;
            ArchiveExtractionComplete(sourceRoot);
        }

        try
        {
            switch (request.Target)
            {
                case ExportTarget.Website:
                    DispatchingCompilationTarget(request.Target);
                    await CompileWebsiteAsync(sourceRoot, request.OutputPath, ExportTarget.Website, cancellationToken).ConfigureAwait(false);
                    break;
                case ExportTarget.Pdf:
                    DispatchingCompilationTarget(request.Target);
                    await CompilePdfAsync(sourceRoot, request.OutputPath, cancellationToken).ConfigureAwait(false);
                    break;
                case ExportTarget.FoundryDb:
                    DispatchingCompilationTarget(request.Target);
                    await CompileFoundrySeedExportAsync(sourceRoot, request.OutputPath, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported export target.");
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(extractionRoot) && Directory.Exists(extractionRoot))
            {
                CleaningExtractedSource(extractionRoot);
                Directory.Delete(extractionRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Renders a preview document for a project material file.
    /// </summary>
    /// <param name="projectRoot">A <see cref="string"/> representing the project root directory.</param>
    /// <param name="sourcePath">A <see cref="string"/> representing the material file path to preview.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> indicating whether the operation should be canceled.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous preview operation result.</returns>
    public async Task<SourcebookPreviewResult> RenderPreviewAsync(string projectRoot, string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        string sourceRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(sourceRoot))
        {
            throw new ArgumentException($"Project directory does not exist: {sourceRoot}", nameof(projectRoot));
        }

        string resolvedSourcePath = Path.IsPathRooted(sourcePath)
            ? Path.GetFullPath(sourcePath)
            : Path.GetFullPath(Path.Combine(sourceRoot, sourcePath));
        if (!File.Exists(resolvedSourcePath))
        {
            throw new ArgumentException($"Source file does not exist: {resolvedSourcePath}", nameof(sourcePath));
        }

        string relativePath = Path.GetRelativePath(sourceRoot, resolvedSourcePath);
        if (relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Preview source must be inside the project directory.", nameof(sourcePath));
        }

        if (!IsStructuredInclude(resolvedSourcePath))
        {
            throw new ArgumentException("Preview source must be a Markdown or JSON file.", nameof(sourcePath));
        }

        string normalizedRelativePath = ToWebPath(relativePath);
        IReadOnlyDictionary<string, string> previousProjectSubstitutionSettings = _activeProjectSubstitutionSettings;
        _activeProjectSubstitutionSettings = LoadProjectSubstitutionSettings(sourceRoot, ExportTarget.Website);
        var previewTimer = Stopwatch.StartNew();
        PreviewRenderStarted(relativePath, sourceRoot);
        try
        {
            PreviewDiskCacheValidation cacheValidation = await ValidatePreviewDiskCacheAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
            string previewFileCacheKey = BuildPreviewFileCacheKey(sourceRoot, normalizedRelativePath);
            long sourceWriteTicks = File.GetLastWriteTimeUtc(resolvedSourcePath).Ticks;
            cacheValidation.ContentHashes.TryGetValue(normalizedRelativePath, out string? sourceContentHash);
            switch (cacheValidation.IsValid)
            {
                case true when
                    _previewFileRenderCache.TryGetValue(previewFileCacheKey, out PreviewFileRenderCacheEntry? cachedRender) &&
                    cachedRender.SourceWriteUtcTicks == sourceWriteTicks:
                    previewTimer.Stop();
                    PreviewRenderFileCacheHit(relativePath, previewTimer.ElapsedMilliseconds);
                    PreviewRenderCompleted(relativePath, cacheHit: true, cacheValidation.State?.IndexedTopicCount ?? 0, previewTimer.ElapsedMilliseconds);
                    return new(
                        cachedRender.Html,
                        cachedRender.LinkTargets,
                        new(
                            ProjectCacheHit: true,
                            FileCacheHit: true,
                            ElapsedMs: previewTimer.ElapsedMilliseconds,
                            CacheBuildElapsedMs: 0,
                            IndexTopicCount: cacheValidation.State?.IndexedTopicCount ?? 0));
                case true when
                    !string.IsNullOrWhiteSpace(sourceContentHash) &&
                    await TryReadGeneratedPreviewCacheAsync(
                        cacheValidation.Paths,
                        normalizedRelativePath,
                        sourceContentHash,
                        cancellationToken).ConfigureAwait(false) is { } generatedPreview:
                    _previewFileRenderCache[previewFileCacheKey] = new(sourceWriteTicks, generatedPreview.Html, generatedPreview.LinkTargets);
                    PreviewRenderCacheHit(sourceRoot, cacheValidation.State?.IndexedTopicCount ?? 0, generatedPreview.LinkTargets.Count);
                    previewTimer.Stop();
                    PreviewRenderFileCacheHit(relativePath, previewTimer.ElapsedMilliseconds);
                    PreviewRenderCompleted(relativePath, cacheHit: true, cacheValidation.State?.IndexedTopicCount ?? 0, previewTimer.ElapsedMilliseconds);
                    return new(
                        generatedPreview.Html,
                        generatedPreview.LinkTargets,
                        new(
                            ProjectCacheHit: true,
                            FileCacheHit: true,
                            ElapsedMs: previewTimer.ElapsedMilliseconds,
                            CacheBuildElapsedMs: 0,
                            IndexTopicCount: cacheValidation.State?.IndexedTopicCount ?? 0));
            }

            ((RenderOptions renderOptions,
                    List<IndexTopic> indexTopics,
                    _,
                    ProjectMetadata metadata,
                    ProjectPageSubstitutionValues projectPageSubstitutionValues),
                bool cacheHit,
                long cacheBuildElapsedMs) = await GetPreviewRenderCacheAsync(
                sourceRoot,
                rebuildDiskCacheArtifacts: !cacheValidation.IsValid,
                contentHashes: cacheValidation.ContentHashes,
                cancellationToken).ConfigureAwait(false);
            ConfigureEntityMentionLinks(
                indexTopics,
                ExportTarget.Website,
                renderOptions.AutoLinkEntityMentions,
                renderOptions.GenerateReferenceDictionary,
                sourceRoot);
            Dictionary<string, string> previewLinkTargets = BuildPreviewLinkTargets(sourceRoot, indexTopics);
            AddPreviewEntityMentionLinkTargets(sourceRoot, previewLinkTargets);

            PreviewRenderBodyStarted(relativePath);
            string title = ResolveMaterialTitle(resolvedSourcePath, null);
            string targetId = ResolveReferenceTargetId(sourceRoot, resolvedSourcePath) ?? BuildMaterialAnchorId(sourceRoot, resolvedSourcePath);
            string markdown = await RenderMaterialBodyMarkdownAsync(
                resolvedSourcePath,
                sourceRoot,
                targetId,
                title,
                title,
                cancellationToken).ConfigureAwait(false);
            string bodyHtml = RenderMarkdownToHtml(markdown);
            PreviewRewriteLinksStarted(relativePath, previewLinkTargets.Count);
            string rewrittenBodyHtml = RewritePreviewLinks(bodyHtml, previewLinkTargets);
            rewrittenBodyHtml = ReplaceProjectSubstitutionPlaceholders(rewrittenBodyHtml, projectPageSubstitutionValues);
            string heroImageHtml = BuildMaterialHeroImageHtml(resolvedSourcePath, sourceRoot);
            heroImageHtml = ReplaceProjectSubstitutionPlaceholders(heroImageHtml, projectPageSubstitutionValues);

            FontSettings fontSettings = LoadFontSettings(sourceRoot);
            RenderContext context = new(
                metadata.Title,
                sourceRoot,
                [],
                null,
                metadata,
                indexTopics,
                null,
                fontSettings,
                [],
                renderOptions);
            string stylesheet = BuildStylesheet(context);
            string html =
                $$"""
                <!doctype html>
                <html>
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <style>
                  {{stylesheet}}
                  body{background:var(--paper);}
                  .layout{display:block;max-width:none;min-height:auto;}
                  main{padding:1.2rem 1.35rem;}
                  section{border-bottom:0;margin-bottom:0;padding-bottom:0;}
                  a{color:var(--accent);text-decoration-thickness:.08em;text-underline-offset:.16em;}
                  </style>
                </head>
                <body>
                  <div class="layout">
                    <main>
                      <section id="{{EscapeHtml(targetId)}}">
                        {{heroImageHtml}}
                        <h2>{{EscapeHtml(title)}}</h2>
                        {{rewrittenBodyHtml}}
                      </section>
                    </main>
                  </div>
                </body>
                </html>
                """;

            html = ReplaceProjectSubstitutionPlaceholders(html, projectPageSubstitutionValues);
            _previewFileRenderCache[previewFileCacheKey] = new(sourceWriteTicks, html, previewLinkTargets);
            if (!string.IsNullOrWhiteSpace(sourceContentHash))
            {
                await WriteGeneratedPreviewCacheAsync(
                        cacheValidation.Paths,
                        normalizedRelativePath,
                        sourceContentHash,
                        html,
                        previewLinkTargets,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            PreviewRenderFileCacheStored(relativePath, previewFileCacheKey);
            previewTimer.Stop();
            PreviewRenderCompleted(relativePath, cacheHit, indexTopics.Count, previewTimer.ElapsedMilliseconds);
            return new(
                html,
                previewLinkTargets,
                new(
                    ProjectCacheHit: cacheHit,
                    FileCacheHit: false,
                    ElapsedMs: previewTimer.ElapsedMilliseconds,
                    CacheBuildElapsedMs: cacheBuildElapsedMs,
                    IndexTopicCount: indexTopics.Count));
        }
        finally
        {
            _activeProjectSubstitutionSettings = previousProjectSubstitutionSettings;
        }
    }
}

/// <summary>
/// Represents preview HTML output and associated metadata.
/// </summary>
public sealed record SourcebookPreviewResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SourcebookPreviewResult"/> record.
    /// </summary>
    /// <param name="Html">A <see cref="string"/> representing the rendered preview HTML.</param>
    /// <param name="LinkTargets">A <see cref="IReadOnlyDictionary{TKey, TValue}"/> representing link targets available during preview rewriting.</param>
    /// <param name="Diagnostics">A <see cref="PreviewRenderDiagnostics"/> representing optional diagnostics captured during rendering.</param>
    public SourcebookPreviewResult(
        string Html,
        IReadOnlyDictionary<string, string> LinkTargets,
        PreviewRenderDiagnostics? Diagnostics = null)
    {
        this.Html = Html;
        this.LinkTargets = LinkTargets;
        this.Diagnostics = Diagnostics;
    }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the rendered preview HTML document.
    /// </summary>
    public string Html { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="IReadOnlyDictionary{TKey, TValue}"/> representing preview link target mappings keyed by target token.
    /// </summary>
    public IReadOnlyDictionary<string, string> LinkTargets { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="PreviewRenderDiagnostics"/> representing diagnostics recorded for the preview render operation.
    /// </summary>
    public PreviewRenderDiagnostics? Diagnostics { get; init; }
}

/// <summary>
/// Represents diagnostic timing and cache details for a preview render operation.
/// </summary>
public sealed record PreviewRenderDiagnostics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewRenderDiagnostics"/> record.
    /// </summary>
    /// <param name="ProjectCacheHit">A <see cref="bool"/> indicating whether the project-level preview cache was reused.</param>
    /// <param name="FileCacheHit">A <see cref="bool"/> indicating whether the file-level preview cache was reused.</param>
    /// <param name="ElapsedMs">A <see cref="long"/> indicating the total preview render duration in milliseconds.</param>
    /// <param name="CacheBuildElapsedMs">A <see cref="long"/> indicating the cache rebuild duration in milliseconds.</param>
    /// <param name="IndexTopicCount">A <see cref="int"/> indicating the number of index topics in the render context.</param>
    public PreviewRenderDiagnostics(
        bool ProjectCacheHit,
        bool FileCacheHit,
        long ElapsedMs,
        long CacheBuildElapsedMs,
        int IndexTopicCount)
    {
        this.ProjectCacheHit = ProjectCacheHit;
        this.FileCacheHit = FileCacheHit;
        this.ElapsedMs = ElapsedMs;
        this.CacheBuildElapsedMs = CacheBuildElapsedMs;
        this.IndexTopicCount = IndexTopicCount;
    }

    /// <summary>
    /// Gets or sets a <see cref="bool"/> indicating whether the project-level preview cache was reused.
    /// </summary>
    public bool ProjectCacheHit { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="bool"/> indicating whether the file-level preview cache was reused.
    /// </summary>
    public bool FileCacheHit { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="long"/> indicating the total preview render duration in milliseconds.
    /// </summary>
    public long ElapsedMs { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="long"/> indicating the cache rebuild duration in milliseconds.
    /// </summary>
    public long CacheBuildElapsedMs { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the number of index topics in the render context.
    /// </summary>
    public int IndexTopicCount { get; init; }
}
