using Microsoft.Extensions.Logging;

namespace Grimoire.Core;

/// <summary>
/// Compiles sourcebook inputs into supported export targets while emitting structured diagnostic logs.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Logs the start of a compilation operation.
    /// </summary>
    /// <param name="target">The export target being compiled.</param>
    /// <param name="sourceKind">The kind of input source being compiled.</param>
    /// <param name="input">The input path or identifier.</param>
    /// <param name="output">The output path.</param>
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "Starting compilation: target={target}, sourceKind={sourceKind}, input={input}, output={output}.")]
    private partial void StartingCompilation(ExportTarget target, InputSourceKind sourceKind, string input, string output);

    /// <summary>
    /// Logs archive extraction to a temporary location.
    /// </summary>
    /// <param name="input">The archive input path.</param>
    /// <param name="extractionRoot">The temporary extraction root path.</param>
    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Extracting archive {input} to temporary path {extractionRoot}.")]
    private partial void ExtractingArchive(string input, string extractionRoot);

    /// <summary>
    /// Logs completion of archive extraction and the selected source root.
    /// </summary>
    /// <param name="sourceRoot">The extracted source root path.</param>
    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Archive extraction complete. Using source root {sourceRoot}.")]
    private partial void ArchiveExtractionComplete(string sourceRoot);

    /// <summary>
    /// Logs dispatching compilation work for a target.
    /// </summary>
    /// <param name="target">The export target being dispatched.</param>
    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "Dispatching compilation for target {target}.")]
    private partial void DispatchingCompilationTarget(ExportTarget target);

    /// <summary>
    /// Logs cleanup of an extracted source directory.
    /// </summary>
    /// <param name="extractionRoot">The extracted source directory to remove.</param>
    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "Cleaning up extracted source {extractionRoot}.")]
    private partial void CleaningExtractedSource(string extractionRoot);

    /// <summary>
    /// Logs preparation of website render output for a target.
    /// </summary>
    /// <param name="target">The export target being prepared.</param>
    /// <param name="outputDirectory">The website output directory.</param>
    [LoggerMessage(EventId = 2005, Level = LogLevel.Debug, Message = "Preparing {target} website render output at {outputDirectory}.")]
    private partial void PreparingWebsiteRenderOutput(ExportTarget target, string outputDirectory);

    /// <summary>
    /// Logs the number of compile-time substitution settings that were loaded.
    /// </summary>
    /// <param name="count">The number of settings loaded.</param>
    /// <param name="target">The export target the settings were loaded for.</param>
    [LoggerMessage(EventId = 2006, Level = LogLevel.Debug, Message = "Loaded {count} compile-time substitution settings for target {target}.")]
    private partial void LoadedCompileTimeSubstitutionSettings(int count, ExportTarget target);

    /// <summary>
    /// Logs creation of the render context.
    /// </summary>
    [LoggerMessage(EventId = 2007, Level = LogLevel.Debug, Message = "Building render context.")]
    private partial void BuildingRenderContext();

    /// <summary>
    /// Logs stylesheet output to disk.
    /// </summary>
    /// <param name="stylePath">The stylesheet output path.</param>
    [LoggerMessage(EventId = 2008, Level = LogLevel.Debug, Message = "Writing stylesheet to {stylePath}.")]
    private partial void WritingStylesheet(string stylePath);

    /// <summary>
    /// Logs copying of registered assets to output.
    /// </summary>
    [LoggerMessage(EventId = 2009, Level = LogLevel.Debug, Message = "Copying registered assets into output.")]
    private partial void CopyingRegisteredAssets();

    /// <summary>
    /// Logs computation of dynamic project substitution values.
    /// </summary>
    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug, Message = "Computing dynamic project substitutions (page counts, references, macros).")]
    private partial void ComputingDynamicProjectSubstitutions();

    /// <summary>
    /// Logs writing of website documents.
    /// </summary>
    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug, Message = "Writing website documents.")]
    private partial void WritingWebsiteDocuments();

    /// <summary>
    /// Logs writing consolidated HTML output.
    /// </summary>
    /// <param name="htmlPath">The consolidated HTML output path.</param>
    [LoggerMessage(EventId = 2012, Level = LogLevel.Debug, Message = "Writing consolidated HTML to {htmlPath}.")]
    private partial void WritingConsolidatedHtml(string htmlPath);

    /// <summary>
    /// Logs the start of PDF compilation.
    /// </summary>
    /// <param name="outputPath">The PDF output path.</param>
    [LoggerMessage(EventId = 2013, Level = LogLevel.Debug, Message = "Starting PDF compilation to {outputPath}.")]
    private partial void StartingPdfCompilation(string outputPath);

    /// <summary>
    /// Logs rendering of the intermediate website used for PDF generation.
    /// </summary>
    /// <param name="tempWebsitePath">The temporary website path used for PDF rendering.</param>
    [LoggerMessage(EventId = 2014, Level = LogLevel.Debug, Message = "Rendering intermediate website for PDF at {tempWebsitePath}.")]
    private partial void RenderingIntermediateWebsiteForPdf(string tempWebsitePath);

    /// <summary>
    /// Logs resolution of the browser executable used for PDF rendering.
    /// </summary>
    /// <param name="htmlPath">The HTML path that will be rendered to PDF.</param>
    [LoggerMessage(EventId = 2015, Level = LogLevel.Debug, Message = "Resolving browser executable for PDF render. HTML source: {htmlPath}.")]
    private partial void ResolvingPdfBrowserExecutable(string htmlPath);

    /// <summary>
    /// Logs launch of headless Chromium for PDF generation.
    /// </summary>
    [LoggerMessage(EventId = 2016, Level = LogLevel.Debug, Message = "Launching headless Chromium for PDF generation.")]
    private partial void LaunchingPdfChromium();

    /// <summary>
    /// Logs loading rendered HTML into Chromium and waiting for readiness.
    /// </summary>
    [LoggerMessage(EventId = 2017, Level = LogLevel.Debug, Message = "Loading rendered HTML into Chromium and waiting for network idle/font readiness.")]
    private partial void LoadingRenderedHtmlIntoChromium();

    /// <summary>
    /// Logs computation of page references for PDF table-of-contents and index output.
    /// </summary>
    [LoggerMessage(EventId = 2018, Level = LogLevel.Debug, Message = "Computing PDF table-of-contents and index page references.")]
    private partial void ComputingPdfIndexPageReferences();

    /// <summary>
    /// Logs handoff to the PDF byte generation step.
    /// </summary>
    [LoggerMessage(EventId = 2019, Level = LogLevel.Debug, Message = "Handing off to PuppeteerSharp for PDF byte generation.")]
    private partial void GeneratingPdfBytes();

    /// <summary>
    /// Logs writing generated PDF bytes to disk.
    /// </summary>
    /// <param name="outputPath">The destination PDF file path.</param>
    [LoggerMessage(EventId = 2020, Level = LogLevel.Debug, Message = "Writing generated PDF bytes to {outputPath}.")]
    private partial void WritingPdfBytes(string outputPath);

    /// <summary>
    /// Logs cleanup of the temporary website generated for PDF export.
    /// </summary>
    /// <param name="tempWebsitePath">The temporary website path to remove.</param>
    [LoggerMessage(EventId = 2021, Level = LogLevel.Debug, Message = "Cleaning up temporary PDF website path {tempWebsitePath}.")]
    private partial void CleaningPdfTemporaryWebsite(string tempWebsitePath);

    /// <summary>
    /// Logs the start of Foundry database export.
    /// </summary>
    /// <param name="outputPath">The Foundry database output path.</param>
    /// <param name="settingsCount">The number of substitution settings applied.</param>
    [LoggerMessage(EventId = 2022, Level = LogLevel.Debug, Message = "Starting Foundry DB export to {outputPath} with {settingsCount} substitution settings.")]
    private partial void StartingFoundryExport(string outputPath, int settingsCount);

    /// <summary>
    /// Logs loading Foundry entries from content chapters.
    /// </summary>
    [LoggerMessage(EventId = 2023, Level = LogLevel.Debug, Message = "Loading Foundry entries from content chapters.")]
    private partial void LoadingFoundryEntries();

    /// <summary>
    /// Logs computation of dynamic substitutions for Foundry entries.
    /// </summary>
    [LoggerMessage(EventId = 2024, Level = LogLevel.Debug, Message = "Computing dynamic substitutions for Foundry entry content.")]
    private partial void ComputingFoundryDynamicSubstitutions();

    /// <summary>
    /// Logs opening the SQLite connection for Foundry database output.
    /// </summary>
    [LoggerMessage(EventId = 2025, Level = LogLevel.Debug, Message = "Opening SQLite connection for Foundry DB output.")]
    private partial void OpeningFoundrySqliteConnection();

    /// <summary>
    /// Logs committing the Foundry database transaction.
    /// </summary>
    [LoggerMessage(EventId = 2026, Level = LogLevel.Debug, Message = "Committing Foundry DB transaction.")]
    private partial void CommittingFoundryTransaction();

    /// <summary>
    /// Logs loading render context data for a target and source root.
    /// </summary>
    /// <param name="target">The export target whose context is being loaded.</param>
    /// <param name="sourceRoot">The source root path supplying content.</param>
    [LoggerMessage(EventId = 2027, Level = LogLevel.Debug, Message = "Loading render context for target {target} from {sourceRoot}.")]
    private partial void LoadingRenderContext(ExportTarget target, string sourceRoot);

    /// <summary>
    /// Logs copying font assets into the output tree.
    /// </summary>
    /// <param name="fontsSourcePath">The source path containing font assets.</param>
    /// <param name="fontsOutputPath">The destination path for font assets.</param>
    [LoggerMessage(EventId = 2028, Level = LogLevel.Debug, Message = "Copying font assets from {fontsSourcePath} to {fontsOutputPath}.")]
    private partial void CopyingFontAssets(string fontsSourcePath, string fontsOutputPath);

    /// <summary>
    /// Logs loading index topics.
    /// </summary>
    [LoggerMessage(EventId = 2029, Level = LogLevel.Debug, Message = "Loading index topics.")]
    private partial void LoadingIndexTopics();

    /// <summary>
    /// Logs the number of loaded index topics.
    /// </summary>
    /// <param name="indexTopicCount">The number of index topics loaded.</param>
    [LoggerMessage(EventId = 2030, Level = LogLevel.Debug, Message = "Loaded {indexTopicCount} index topics.")]
    private partial void LoadedIndexTopics(int indexTopicCount);

    /// <summary>
    /// Logs loading content sections.
    /// </summary>
    [LoggerMessage(EventId = 2031, Level = LogLevel.Debug, Message = "Loading content sections.")]
    private partial void LoadingContentSections();

    /// <summary>
    /// Logs building the reference dictionary section.
    /// </summary>
    [LoggerMessage(EventId = 2032, Level = LogLevel.Debug, Message = "Building reference dictionary section.")]
    private partial void BuildingReferenceDictionarySection();

    /// <summary>
    /// Logs building the unreferenced materials appendix section.
    /// </summary>
    [LoggerMessage(EventId = 2033, Level = LogLevel.Debug, Message = "Building unreferenced materials appendix section.")]
    private partial void BuildingUnreferencedAppendixSection();

    /// <summary>
    /// Logs loading cover, metadata, and bibliography content.
    /// </summary>
    [LoggerMessage(EventId = 2034, Level = LogLevel.Debug, Message = "Loading cover, metadata, and bibliography content.")]
    private partial void LoadingCoverMetadataBibliography();

    /// <summary>
    /// Logs final render context counts once context assembly is complete.
    /// </summary>
    /// <param name="sectionCount">The number of sections in the render context.</param>
    /// <param name="indexTopicCount">The number of index topics in the render context.</param>
    /// <param name="fontCount">The number of fonts available in the render context.</param>
    [LoggerMessage(EventId = 2035, Level = LogLevel.Debug, Message = "Render context ready: sections={sectionCount}, indexTopics={indexTopicCount}, fonts={fontCount}.")]
    private partial void RenderContextReady(int sectionCount, int indexTopicCount, int fontCount);

    /// <summary>
    /// Logs loading chapter markdown files from the content root.
    /// </summary>
    /// <param name="chapterFileCount">The number of chapter markdown files discovered.</param>
    /// <param name="contentRoot">The content root containing chapter files.</param>
    [LoggerMessage(EventId = 2036, Level = LogLevel.Debug, Message = "Loading {chapterFileCount} chapter markdown files from {contentRoot}.")]
    private partial void LoadingChapterMarkdownFiles(int chapterFileCount, string contentRoot);

    /// <summary>
    /// Logs rendering an appendix section and its material entry count.
    /// </summary>
    /// <param name="title">The appendix section title.</param>
    /// <param name="materialCount">The number of material entries in the section.</param>
    [LoggerMessage(EventId = 2037, Level = LogLevel.Debug, Message = "Rendering appendix section {title} with {materialCount} material entries.")]
    private partial void RenderingAppendixSection(string title, int materialCount);

    /// <summary>
    /// Logs rendering an appendix group and its entry count.
    /// </summary>
    /// <param name="group">The appendix group name.</param>
    /// <param name="entryCount">The number of entries in the group.</param>
    [LoggerMessage(EventId = 2038, Level = LogLevel.Debug, Message = "Rendering appendix group {group} with {entryCount} entries.")]
    private partial void RenderingAppendixGroup(string group, int entryCount);

    /// <summary>
    /// Logs scanning markdown files for referenced material paths.
    /// </summary>
    /// <param name="markdownFileCount">The number of markdown files being scanned.</param>
    [LoggerMessage(EventId = 2039, Level = LogLevel.Debug, Message = "Scanning {markdownFileCount} markdown files for referenced material paths.")]
    private partial void ScanningReferencedMaterialPaths(int markdownFileCount);

    /// <summary>
    /// Logs scanning markdown files for referenced material target pages.
    /// </summary>
    /// <param name="markdownFileCount">The number of markdown files being scanned.</param>
    [LoggerMessage(EventId = 2040, Level = LogLevel.Debug, Message = "Scanning {markdownFileCount} markdown files for referenced material target pages.")]
    private partial void ScanningReferencedMaterialTargetPages(int markdownFileCount);

    /// <summary>
    /// Logs progress while scanning files for preview reference paths.
    /// </summary>
    /// <param name="index">The current one-based scan index.</param>
    /// <param name="total">The total number of files being scanned.</param>
    /// <param name="relativePath">The relative path of the file being scanned.</param>
    [LoggerMessage(EventId = 2064, Level = LogLevel.Debug, Message = "Preview reference scan {index}/{total}: {relativePath}.")]
    private partial void ScanningReferencedMaterialPathFile(int index, int total, string relativePath);

    /// <summary>
    /// Logs progress while scanning files for preview reference target pages.
    /// </summary>
    /// <param name="index">The current one-based scan index.</param>
    /// <param name="total">The total number of files being scanned.</param>
    /// <param name="relativePath">The relative path of the file being scanned.</param>
    [LoggerMessage(EventId = 2065, Level = LogLevel.Debug, Message = "Preview reference target scan {index}/{total}: {relativePath}.")]
    private partial void ScanningReferencedMaterialTargetFile(int index, int total, string relativePath);

    /// <summary>
    /// Logs how many preview-referenceable material files were found.
    /// </summary>
    /// <param name="materialCount">The number of material files found.</param>
    [LoggerMessage(EventId = 2066, Level = LogLevel.Debug, Message = "Preview found {materialCount} referenceable material files.")]
    private partial void PreviewReferenceableMaterialsFound(int materialCount);

    /// <summary>
    /// Logs how many candidate topic files were found for preview indexing.
    /// </summary>
    /// <param name="candidateCount">The number of candidate topic files found.</param>
    [LoggerMessage(EventId = 2067, Level = LogLevel.Debug, Message = "Preview indexing {candidateCount} candidate topic files.")]
    private partial void PreviewIndexCandidatesFound(int candidateCount);

    /// <summary>
    /// Logs progress while indexing preview topics.
    /// </summary>
    /// <param name="index">The current one-based topic index.</param>
    /// <param name="total">The total number of topics being indexed.</param>
    /// <param name="relativePath">The relative path of the topic being indexed.</param>
    [LoggerMessage(EventId = 2068, Level = LogLevel.Debug, Message = "Preview index {index}/{total}: {relativePath}.")]
    private partial void PreviewIndexingTopic(int index, int total, string relativePath);

    /// <summary>
    /// Logs the start of preview body rendering for a source file.
    /// </summary>
    /// <param name="relativePath">The relative path being rendered for preview.</param>
    [LoggerMessage(EventId = 2069, Level = LogLevel.Debug, Message = "Rendering preview body for {relativePath}: includes, substitutions, and autolinks.")]
    private partial void PreviewRenderBodyStarted(string relativePath);

    /// <summary>
    /// Logs the start of preview link rewriting for a source file.
    /// </summary>
    /// <param name="relativePath">The relative path whose links are being rewritten.</param>
    /// <param name="linkTargetCount">The number of navigable link targets.</param>
    [LoggerMessage(EventId = 2070, Level = LogLevel.Debug, Message = "Rewriting preview links for {relativePath}: {linkTargetCount} navigable entity links.")]
    private partial void PreviewRewriteLinksStarted(string relativePath, int linkTargetCount);

    /// <summary>
    /// Logs resolution attempts for shadow reference names.
    /// </summary>
    /// <param name="requestedCount">The number of requested shadow references.</param>
    /// <param name="materialCount">The number of material files searched.</param>
    [LoggerMessage(EventId = 2041, Level = LogLevel.Debug, Message = "Resolving {requestedCount} shadow reference names across {materialCount} material files.")]
    private partial void ResolvingShadowReferences(int requestedCount, int materialCount);

    /// <summary>
    /// Logs loading compile-time substitution settings for a target.
    /// </summary>
    /// <param name="target">The export target settings are being loaded for.</param>
    [LoggerMessage(EventId = 2042, Level = LogLevel.Debug, Message = "Loading compile-time substitution settings for target {target}.")]
    private partial void LoadingCompileTimeSubstitutionSettings(ExportTarget target);

    /// <summary>
    /// Logs application of configuration providers for substitution resolution.
    /// </summary>
    /// <param name="inMemoryCount">The number of in-memory provider entries.</param>
    [LoggerMessage(EventId = 2043, Level = LogLevel.Debug, Message = "Applying IConfiguration providers: in-memory({inMemoryCount}), appsettings.json, environment variables.")]
    private partial void ApplyingConfigurationProviders(int inMemoryCount);

    /// <summary>
    /// Logs the number of resolved compile-time substitution settings.
    /// </summary>
    /// <param name="settingCount">The number of resolved settings.</param>
    [LoggerMessage(EventId = 2044, Level = LogLevel.Debug, Message = "Resolved {settingCount} compile-time substitution settings.")]
    private partial void ResolvedCompileTimeSubstitutionSettings(int settingCount);

    /// <summary>
    /// Logs inline token processing counts for a file.
    /// </summary>
    /// <param name="currentFilePath">The current file being processed.</param>
    /// <param name="substitutionCount">The number of substitution tokens found.</param>
    /// <param name="macroCount">The number of macro tokens found.</param>
    /// <param name="includeCount">The number of include tokens found.</param>
    [LoggerMessage(EventId = 2045, Level = LogLevel.Debug, Message = "Processing inline tokens for {currentFilePath}: substitutions={substitutionCount}, macros={macroCount}, includes={includeCount}.")]
    private partial void ProcessingInlineTokens(string currentFilePath, int substitutionCount, int macroCount, int includeCount);

    /// <summary>
    /// Logs inclusion of material into the current file during token processing.
    /// </summary>
    /// <param name="absolutePath">The absolute path of the included material.</param>
    /// <param name="inline">Whether the include is rendered inline.</param>
    /// <param name="currentFilePath">The file receiving the included material.</param>
    [LoggerMessage(EventId = 2046, Level = LogLevel.Debug, Message = "Including material {absolutePath} (inline={inline}) into {currentFilePath}.")]
    private partial void IncludingMaterial(string absolutePath, bool inline, string currentFilePath);

    /// <summary>
    /// Logs substitution of a macro.pageCount token.
    /// </summary>
    /// <param name="currentFilePath">The file containing the token.</param>
    /// <param name="token">The token being substituted.</param>
    [LoggerMessage(EventId = 2047, Level = LogLevel.Debug, Message = "Substitution in {currentFilePath}: {token} -> dynamic macro.pageCount placeholder.")]
    private partial void SubstitutingMacroPageCount(string currentFilePath, string token);

    /// <summary>
    /// Logs substitution of a macro.seeAlso token.
    /// </summary>
    /// <param name="currentFilePath">The file containing the token.</param>
    /// <param name="token">The token being substituted.</param>
    [LoggerMessage(EventId = 2048, Level = LogLevel.Debug, Message = "Substitution in {currentFilePath}: {token} -> dynamic macro.seeAlso placeholder.")]
    private partial void SubstitutingMacroSeeAlso(string currentFilePath, string token);

    /// <summary>
    /// Logs substitution of a macro.pageTitle token.
    /// </summary>
    /// <param name="currentFilePath">The file containing the token.</param>
    /// <param name="token">The token being substituted.</param>
    /// <param name="pageTitle">The resolved page title value.</param>
    [LoggerMessage(EventId = 2049, Level = LogLevel.Debug, Message = "Substitution in {currentFilePath}: {token} -> {pageTitle}.")]
    private partial void SubstitutingMacroPageTitle(string currentFilePath, string token, string pageTitle);

    /// <summary>
    /// Logs substitution of a macro.contentPageTitle token.
    /// </summary>
    /// <param name="currentFilePath">The file containing the token.</param>
    /// <param name="token">The token being substituted.</param>
    /// <param name="contentPageTitle">The resolved content page title value.</param>
    [LoggerMessage(EventId = 2050, Level = LogLevel.Debug, Message = "Substitution in {currentFilePath}: {token} -> {contentPageTitle}.")]
    private partial void SubstitutingMacroContentPageTitle(string currentFilePath, string token, string contentPageTitle);

    /// <summary>
    /// Logs substitution of a dynamic macro placeholder token.
    /// </summary>
    /// <param name="currentFilePath">The file containing the token.</param>
    /// <param name="token">The token being substituted.</param>
    [LoggerMessage(EventId = 2051, Level = LogLevel.Debug, Message = "Substitution in {currentFilePath}: {token} -> dynamic placeholder.")]
    private partial void SubstitutingDynamicMacroPlaceholder(string currentFilePath, string token);

    /// <summary>
    /// Logs substitution of a compile-time setting token.
    /// </summary>
    /// <param name="currentFilePath">The file containing the token.</param>
    /// <param name="token">The token being substituted.</param>
    [LoggerMessage(EventId = 2052, Level = LogLevel.Debug, Message = "Substitution in {currentFilePath}: {token} -> compile-time setting value.")]
    private partial void SubstitutingCompileTimeSetting(string currentFilePath, string token);

    /// <summary>
    /// Logs an entity-lookup substitution miss.
    /// </summary>
    /// <param name="currentFilePath">The file requesting the entity lookup.</param>
    /// <param name="entityName">The entity name that could not be resolved.</param>
    [LoggerMessage(EventId = 2053, Level = LogLevel.Debug, Message = "Entity lookup substitution in {currentFilePath}: no match for {entityName}.")]
    private partial void EntityLookupNoMatch(string currentFilePath, string entityName);

    /// <summary>
    /// Logs a successful entity-lookup substitution.
    /// </summary>
    /// <param name="currentFilePath">The file requesting the entity lookup.</param>
    /// <param name="entityName">The resolved entity name.</param>
    /// <param name="property">The resolved entity property.</param>
    /// <param name="entityPath">The resolved entity path value.</param>
    [LoggerMessage(EventId = 2054, Level = LogLevel.Debug, Message = "Entity lookup substitution in {currentFilePath}: {entityName}:{property} -> {entityPath}.")]
    private partial void EntityLookupResolved(string currentFilePath, string entityName, string property, string entityPath);

    /// <summary>
    /// Logs a successful file-path substitution.
    /// </summary>
    /// <param name="currentFilePath">The file requesting the substitution.</param>
    /// <param name="relativePath">The relative path in the substitution token.</param>
    /// <param name="property">The requested property from the file token.</param>
    /// <param name="absolutePath">The resolved absolute file path.</param>
    [LoggerMessage(EventId = 2055, Level = LogLevel.Debug, Message = "File substitution in {currentFilePath}: @{relativePath}:{property} -> {absolutePath}.")]
    private partial void FileSubstitutionResolved(string currentFilePath, string relativePath, string property, string absolutePath);

    /// <summary>
    /// Logs progress while auto-linking entity mentions in preview output.
    /// </summary>
    /// <param name="index">The current one-based mention index.</param>
    /// <param name="total">The total number of mentions being processed.</param>
    /// <param name="entityName">The entity name for the current mention.</param>
    [LoggerMessage(EventId = 2056, Level = LogLevel.Debug, Message = "Preview auto-linking mention {index}/{total}: {entityName}.")]
    private partial void AutoLinkingMention(int index, int total, string entityName);

    /// <summary>
    /// Logs completion of preview auto-linking.
    /// </summary>
    /// <param name="mentionCount">The total number of processed entity names.</param>
    [LoggerMessage(EventId = 2071, Level = LogLevel.Debug, Message = "Preview auto-linking complete: {mentionCount} entity names processed.")]
    private partial void AutoLinkingMentionsCompleted(int mentionCount);

    /// <summary>
    /// Logs the start of preview rendering for a file.
    /// </summary>
    /// <param name="relativePath">The relative path being rendered.</param>
    /// <param name="sourceRoot">The source root containing the file.</param>
    [LoggerMessage(EventId = 2057, Level = LogLevel.Debug, Message = "Starting preview render for {relativePath} in {sourceRoot}.")]
    private partial void PreviewRenderStarted(string relativePath, string sourceRoot);

    /// <summary>
    /// Logs reuse of cached preview render context data.
    /// </summary>
    /// <param name="sourceRoot">The source root that produced the cache entry.</param>
    /// <param name="indexTopicCount">The number of cached index topics.</param>
    /// <param name="linkTargetCount">The number of cached link targets.</param>
    [LoggerMessage(EventId = 2058, Level = LogLevel.Debug, Message = "Preview cache hit for {sourceRoot}: indexTopics={indexTopicCount}, linkTargets={linkTargetCount}.")]
    private partial void PreviewRenderCacheHit(string sourceRoot, int indexTopicCount, int linkTargetCount);

    /// <summary>
    /// Logs a preview render cache miss and context rebuild.
    /// </summary>
    /// <param name="sourceRoot">The source root whose cache entry was missing.</param>
    [LoggerMessage(EventId = 2059, Level = LogLevel.Debug, Message = "Preview cache miss for {sourceRoot}; rebuilding preview index context.")]
    private partial void PreviewRenderCacheMiss(string sourceRoot);

    /// <summary>
    /// Logs creation of preview render cache data.
    /// </summary>
    /// <param name="sourceRoot">The source root used to build the cache entry.</param>
    /// <param name="indexTopicCount">The number of indexed topics cached.</param>
    /// <param name="linkTargetCount">The number of link targets cached.</param>
    /// <param name="substitutionValueCount">The number of substitution values cached.</param>
    /// <param name="elapsedMs">The elapsed build time in milliseconds.</param>
    [LoggerMessage(EventId = 2060, Level = LogLevel.Debug, Message = "Preview cache built for {sourceRoot}: indexTopics={indexTopicCount}, linkTargets={linkTargetCount}, substitutionValues={substitutionValueCount}, elapsedMs={elapsedMs}.")]
    private partial void PreviewRenderCacheBuilt(string sourceRoot, int indexTopicCount, int linkTargetCount, int substitutionValueCount, long elapsedMs);

    /// <summary>
    /// Logs completion of preview rendering for a file.
    /// </summary>
    /// <param name="relativePath">The rendered relative path.</param>
    /// <param name="cacheHit">Whether preview context cache data was reused.</param>
    /// <param name="indexTopicCount">The number of index topics used during rendering.</param>
    /// <param name="elapsedMs">The elapsed render time in milliseconds.</param>
    [LoggerMessage(EventId = 2061, Level = LogLevel.Debug, Message = "Completed preview render for {relativePath}: cacheHit={cacheHit}, indexTopics={indexTopicCount}, elapsedMs={elapsedMs}.")]
    private partial void PreviewRenderCompleted(string relativePath, bool cacheHit, int indexTopicCount, long elapsedMs);

    /// <summary>
    /// Logs reuse of a cached rendered preview file.
    /// </summary>
    /// <param name="relativePath">The cached relative path.</param>
    /// <param name="elapsedMs">The elapsed retrieval time in milliseconds.</param>
    [LoggerMessage(EventId = 2062, Level = LogLevel.Debug, Message = "Preview file cache hit for {relativePath} (elapsedMs={elapsedMs}).")]
    private partial void PreviewRenderFileCacheHit(string relativePath, long elapsedMs);

    /// <summary>
    /// Logs storage of a rendered preview file cache entry.
    /// </summary>
    /// <param name="relativePath">The cached relative path.</param>
    /// <param name="cacheKey">The cache key used for the stored entry.</param>
    [LoggerMessage(EventId = 2063, Level = LogLevel.Debug, Message = "Stored preview file cache entry for {relativePath} ({cacheKey}).")]
    private partial void PreviewRenderFileCacheStored(string relativePath, string cacheKey);

    /// <summary>
    /// Logs reuse of cached reference-dictionary candidates for preview.
    /// </summary>
    /// <param name="sourceRoot">The source root that produced the cache entry.</param>
    /// <param name="candidateCount">The number of cached candidates.</param>
    [LoggerMessage(EventId = 2072, Level = LogLevel.Debug, Message = "Preview reference dictionary candidate cache hit for {sourceRoot}: candidates={candidateCount}.")]
    private partial void PreviewReferenceDictionaryCandidateCacheHit(string sourceRoot, int candidateCount);

    /// <summary>
    /// Logs creation of reference-dictionary candidate cache data for preview.
    /// </summary>
    /// <param name="sourceRoot">The source root used to build the cache entry.</param>
    /// <param name="candidateCount">The number of cached candidates.</param>
    /// <param name="elapsedMs">The elapsed build time in milliseconds.</param>
    [LoggerMessage(EventId = 2073, Level = LogLevel.Debug, Message = "Preview reference dictionary candidate cache built for {sourceRoot}: candidates={candidateCount}, elapsedMs={elapsedMs}.")]
    private partial void PreviewReferenceDictionaryCandidateCacheBuilt(string sourceRoot, int candidateCount, long elapsedMs);
}
