using Microsoft.Data.Sqlite;
using PuppeteerSharp;
using System.Globalization;
using System.Text.Json;

namespace Grimoire.Core;

/// <summary>
/// Compiles sourcebook content into website, PDF, and Foundry outputs.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// A <see cref="string"/> representing the browser script that maps page-reference placeholders to computed PDF page numbers.
    /// </summary>
    private const string ComputePdfIndexPageReferencesScript =
        """
        (() => {
          const pageHeightPx = 980;
          const pageByTargetId = new Map();
          const pageReferences = document.querySelectorAll('[data-page-ref]');
          for (let index = 0; index < pageReferences.length; index += 1) {
            const reference = pageReferences[index];
            const targetId = reference.getAttribute('data-page-ref');
            if (!targetId) {
              continue;
            }

            let page = pageByTargetId.get(targetId);
            if (page === undefined) {
              const target = document.getElementById(targetId);
              page = target
                ? Math.floor((target.getBoundingClientRect().top + window.scrollY) / pageHeightPx) + 1
                : null;
              pageByTargetId.set(targetId, page);
            }

            if (page !== null) {
              reference.textContent = String(page);
            }
          }
        })()
        """;


    /// <summary>
    /// Compiles the sourcebook into website-oriented output for the specified export target.
    /// </summary>
    private async Task CompileWebsiteAsync(string sourceRoot, string outputDirectory, ExportTarget target, CancellationToken cancellationToken)
    {
        PreparingWebsiteRenderOutput(target, outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        AssetRewriteState? previousAssetRewriteState = _assetRewriteState;
        IReadOnlyDictionary<string, string> previousProjectSubstitutionSettings = _activeProjectSubstitutionSettings;
        _activeProjectSubstitutionSettings = LoadProjectSubstitutionSettings(sourceRoot, target);
        LoadedCompileTimeSubstitutionSettings(_activeProjectSubstitutionSettings.Count, target);
        _assetRewriteState = new(
            Path.GetFullPath(sourceRoot),
            Path.GetFullPath(outputDirectory),
            new(StringComparer.OrdinalIgnoreCase));
        try
        {
            BuildingRenderContext();
            RenderContext context = await BuildRenderContextAsync(sourceRoot, outputDirectory, target, cancellationToken).ConfigureAwait(false);
            string stylePath = Path.Combine(outputDirectory, "styles.css");

            WritingStylesheet(stylePath);
            await WriteTextFileAsync(stylePath, BuildStylesheet(context), cancellationToken).ConfigureAwait(false);
            CopyingRegisteredAssets();
            CopyRegisteredAssets();

            if (ContainsProjectSubstitutionPlaceholders(context))
            {
                ComputingDynamicProjectSubstitutions();
                ProjectPageSubstitutionValues substitutions = await ComputeProjectPageSubstitutionValuesAsync(context, outputDirectory, cancellationToken).ConfigureAwait(false);
                context = ApplyProjectPageSubstitutions(context, substitutions);
            }

            if (target == ExportTarget.Website)
            {
                WritingWebsiteDocuments();
                await WriteWebsiteDocumentsAsync(context, outputDirectory, cancellationToken).ConfigureAwait(false);
                return;
            }

            string htmlPath = Path.Combine(outputDirectory, "index.html");
            WritingConsolidatedHtml(htmlPath);
            await WriteTextFileAsync(htmlPath, BuildHtmlDocument(context), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _activeProjectSubstitutionSettings = previousProjectSubstitutionSettings;
            _assetRewriteState = previousAssetRewriteState;
        }
    }

    /// <summary>
    /// Compiles the sourcebook into a single PDF document.
    /// </summary>
    private async Task CompilePdfAsync(string sourceRoot, string outputPath, CancellationToken cancellationToken)
    {
        StartingPdfCompilation(outputPath);
        string tempWebsitePath = CreateDeterministicTempPath("grimoire-site");
        Directory.CreateDirectory(tempWebsitePath);
        RenderOptions renderOptions = LoadRenderOptions(sourceRoot, ExportTarget.Pdf);

        try
        {
            RenderingIntermediateWebsiteForPdf(tempWebsitePath);
            await CompileWebsiteAsync(sourceRoot, tempWebsitePath, ExportTarget.Pdf, cancellationToken).ConfigureAwait(false);

            string htmlPath = Path.Combine(tempWebsitePath, "index.html");
            string htmlUri = new Uri(htmlPath).AbsoluteUri;
            ResolvingPdfBrowserExecutable(htmlPath);
            string browserPath = await ChromiumHost.EnsureBrowserExecutableAsync(logger, cancellationToken).ConfigureAwait(false);

            LaunchOptions launchOptions = new()
            {
                Headless = true,
                ExecutablePath = browserPath,
                Args = ["--allow-file-access-from-files", "--font-render-hinting=medium"],
            };

            IBrowser? browser = null;
            IPage? page = null;
            byte[] pdfBytes;
            try
            {
                LaunchingPdfChromium();
                browser = await Puppeteer.LaunchAsync(launchOptions).ConfigureAwait(false);
                page = await browser.NewPageAsync().ConfigureAwait(false);
                LoadingRenderedHtmlIntoChromium();
                await page.GoToAsync(htmlUri, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle0] }).ConfigureAwait(false);
                await page.EvaluateExpressionAsync("document.fonts.ready").ConfigureAwait(false);
                ComputingPdfIndexPageReferences();
                await page.EvaluateExpressionAsync(ComputePdfIndexPageReferencesScript).ConfigureAwait(false);

                PdfOptions options = new()
                {
                    PrintBackground = true,
                    Format = renderOptions.PdfPageFormat,
                    MarginOptions = new() { Top = "10mm", Bottom = "12mm", Left = "10mm", Right = "10mm" },
                    Timeout = 0,
                };

                GeneratingPdfBytes();
                pdfBytes = await page.PdfDataAsync(options).ConfigureAwait(false);
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
            string? parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            WritingPdfBytes(outputPath);
            await File.WriteAllBytesAsync(outputPath, pdfBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(tempWebsitePath))
            {
                CleaningPdfTemporaryWebsite(tempWebsitePath);
                Directory.Delete(tempWebsitePath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Compiles the sourcebook into a Foundry seed database export.
    /// </summary>
    private async Task CompileFoundrySeedExportAsync(string sourceRoot, string outputPath, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> previousProjectSubstitutionSettings = _activeProjectSubstitutionSettings;
        _activeProjectSubstitutionSettings = LoadProjectSubstitutionSettings(sourceRoot, ExportTarget.FoundryDb);
        StartingFoundryExport(outputPath, _activeProjectSubstitutionSettings.Count);
        try
        {
            string? parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            RenderOptions renderOptions = LoadRenderOptions(sourceRoot, ExportTarget.FoundryDb);
            List<IndexTopic> indexTopics = await LoadIndexTopicsAsync(
                sourceRoot,
                renderOptions.IncludeUnreferencedSnippetsInAppendix,
                renderOptions.GenerateReferenceDictionary,
                renderOptions.ShadowReferences,
                cancellationToken).ConfigureAwait(false);
            ConfigureEntityMentionLinks(
                indexTopics,
                ExportTarget.FoundryDb,
                renderOptions.AutoLinkEntityMentions,
                renderOptions.GenerateReferenceDictionary,
                sourceRoot);

            LoadingFoundryEntries();
            List<FoundryEntry> entries = await LoadFoundryEntriesAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
            if (entries.Any(static entry => ContainsProjectSubstitutionPlaceholders(entry.ContentHtml)))
            {
                ComputingFoundryDynamicSubstitutions();
                ProjectPageSubstitutionValues substitutions = await ComputeProjectPageSubstitutionValuesAsync(sourceRoot, cancellationToken).ConfigureAwait(false);
                entries =
                [
                    .. entries.Select(entry => entry with { ContentHtml = ReplaceProjectSubstitutionPlaceholders(entry.ContentHtml, substitutions) }),
                ];
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            await using SqliteConnection connection = new($"Data Source={outputPath}");
            OpeningFoundrySqliteConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteSqlAsync(connection, transaction,
                """
                CREATE TABLE IF NOT EXISTS metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """, cancellationToken).ConfigureAwait(false);

            await ExecuteSqlAsync(connection, transaction,
                """
                CREATE TABLE IF NOT EXISTS documents (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    type TEXT NOT NULL,
                    content_html TEXT NOT NULL,
                    source_path TEXT NOT NULL
                );
                """, cancellationToken).ConfigureAwait(false);

            await ExecuteSqlAsync(connection, transaction,
                """
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """, cancellationToken).ConfigureAwait(false);

            await UpsertMetadataAsync(connection, transaction, "schemaVersion", "1", cancellationToken).ConfigureAwait(false);
            await UpsertMetadataAsync(connection, transaction, "project", InferProjectTitle(sourceRoot), cancellationToken).ConfigureAwait(false);
            await UpsertMetadataAsync(connection, transaction, "collections", JsonSerializer.Serialize(FoundryCollections, FoundryJsonOptions), cancellationToken).ConfigureAwait(false);
            await UpsertMetadataAsync(connection, transaction, "entryCount", entries.Count.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            Dictionary<string, string> foundrySettings = LoadYamlSettings(Path.Combine(sourceRoot, "settings", "foundry.yml"));
            string foundryPackName = GetValue(foundrySettings, FoundryPackNameSettingKey) ?? "journal";
            await UpsertSettingAsync(connection, transaction, FoundryPackNameSettingKey, foundryPackName, cancellationToken).ConfigureAwait(false);

            foreach (FoundryEntry entry in entries)
            {
                SqliteCommand insert = connection.CreateCommand();
                await using SqliteCommand _ = insert;

                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO documents (id, name, type, content_html, source_path)
                    VALUES ($id, $name, $type, $content_html, $source_path);
                    """;
                insert.Parameters.AddWithValue("$id", entry.Id);
                insert.Parameters.AddWithValue("$name", entry.Name);
                insert.Parameters.AddWithValue("$type", entry.Type);
                insert.Parameters.AddWithValue("$content_html", entry.ContentHtml);
                insert.Parameters.AddWithValue("$source_path", entry.SourcePath);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            CommittingFoundryTransaction();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _activeProjectSubstitutionSettings = previousProjectSubstitutionSettings;
        }
    }

    /// <summary>
    /// Executes a static SQL statement inside the provided transaction.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "SQL statements are static literals at call sites.")]
    private static async Task ExecuteSqlAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        SqliteCommand command = connection.CreateCommand();
        await using SqliteCommand _ = command;
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates a metadata key-value pair in the Foundry export database.
    /// </summary>
    private static async Task UpsertMetadataAsync(SqliteConnection connection, SqliteTransaction transaction, string key, string value, CancellationToken cancellationToken)
    {
        SqliteCommand command = connection.CreateCommand();
        await using SqliteCommand _ = command;
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates a settings key-value pair in the Foundry export database.
    /// </summary>
    private static async Task UpsertSettingAsync(SqliteConnection connection, SqliteTransaction transaction, string key, string value, CancellationToken cancellationToken)
    {
        SqliteCommand command = connection.CreateCommand();
        await using SqliteCommand _ = command;
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO settings (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
