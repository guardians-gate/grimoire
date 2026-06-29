using Grimoire.Core.Localization;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Immutable;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Represents options for a D&amp;D Beyond synchronization operation.
/// </summary>
public sealed record DndBeyondSyncOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DndBeyondSyncOptions"/> record.
    /// </summary>
    /// <param name="CobaltToken">The D&amp;D Beyond cobalt session token.</param>
    /// <param name="OutputBaseDirectory">The output directory for downloaded content.</param>
    /// <param name="IncludeHomebrew">Whether homebrew content should be included.</param>
    /// <param name="CampaignId">The optional campaign identifier.</param>
    /// <param name="ItemNames">Optional item-name filters.</param>
    /// <param name="CreatureNames">Optional creature-name filters.</param>
    /// <param name="SpellNames">Optional spell-name filters.</param>
    /// <param name="CharacterSheetNames">Optional character-sheet filters.</param>
    /// <param name="UpgradeToMarkdown">Whether JSON should be upgraded to markdown after download.</param>
    /// <param name="PatreonKey">The optional Patreon beta key.</param>
    public DndBeyondSyncOptions(
        string CobaltToken,
        string OutputBaseDirectory,
        bool IncludeHomebrew,
        int? CampaignId,
        IReadOnlyList<string>? ItemNames = null,
        IReadOnlyList<string>? CreatureNames = null,
        IReadOnlyList<string>? SpellNames = null,
        IReadOnlyList<string>? CharacterSheetNames = null,
        bool UpgradeToMarkdown = false,
        string? PatreonKey = null)
    {
        this.CobaltToken = CobaltToken;
        this.OutputBaseDirectory = OutputBaseDirectory;
        this.IncludeHomebrew = IncludeHomebrew;
        this.CampaignId = CampaignId;
        this.ItemNames = ItemNames;
        this.CreatureNames = CreatureNames;
        this.SpellNames = SpellNames;
        this.CharacterSheetNames = CharacterSheetNames;
        this.UpgradeToMarkdown = UpgradeToMarkdown;
        this.PatreonKey = PatreonKey;
    }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the cobalt session token.
    /// </summary>
    public string CobaltToken { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the base output directory.
    /// </summary>
    public string OutputBaseDirectory { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="bool"/> indicating whether homebrew content is included.
    /// </summary>
    public bool IncludeHomebrew { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the campaign identifier.
    /// </summary>
    public int? CampaignId { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing item-name filters.
    /// </summary>
    public IReadOnlyList<string>? ItemNames { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing creature-name filters.
    /// </summary>
    public IReadOnlyList<string>? CreatureNames { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing spell-name filters.
    /// </summary>
    public IReadOnlyList<string>? SpellNames { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing character-sheet filters.
    /// </summary>
    public IReadOnlyList<string>? CharacterSheetNames { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="bool"/> indicating whether JSON output is upgraded to markdown.
    /// </summary>
    public bool UpgradeToMarkdown { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="string"/> representing the optional Patreon key.
    /// </summary>
    public string? PatreonKey { get; init; }
}

/// <summary>
/// Synchronizes D&amp;D Beyond entities from a proxy endpoint into local files.
/// </summary>
public sealed partial class DndBeyondSyncService(
    HttpClient httpClient,
    Uri? baseUri = null,
    IStringLocalizer? localizer = null,
    ILogger<DndBeyondSyncService>? logger = null,
    Func<HttpMessageHandler>? directProxyHandlerFactory = null)
{
    /// <summary>
    /// Stores the account page URL shown for challenge recovery guidance.
    /// </summary>
    private const string CaptchaHelpUrl = "https://www.dndbeyond.com/account";

    /// <summary>
    /// Stores JSON serialization settings used for persisted output payloads.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Stores supported class and rules-version combinations used for spell downloads.
    /// </summary>
    private static readonly ImmutableArray<(string RulesVersion, string ClassName)> SpellLists =
    [
        ("2014", "Cleric"),
        ("2014", "Druid"),
        ("2014", "Sorcerer"),
        ("2014", "Warlock"),
        ("2014", "Wizard"),
        ("2014", "Paladin"),
        ("2014", "Ranger"),
        ("2014", "Bard"),
        ("2014", "Artificer"),
        ("2014", "Graviturgy"),
        ("2014", "Chronurgy"),
        ("2024", "Wizard"),
        ("2024", "Artificer"),
    ];

    /// <summary>
    /// Stores the HTTP client used for proxy requests.
    /// </summary>
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>
    /// Stores the base proxy URI for synchronization requests.
    /// </summary>
    private readonly Uri _baseUri = baseUri ?? new Uri("https://proxy.ddb.mrprimate.co.uk", UriKind.Absolute);

    /// <summary>
    /// Stores the localizer used for user-facing messages.
    /// </summary>
    private readonly IStringLocalizer _localizer = localizer ?? new GrimoireLocalizationFactory().CreateDefault();

    /// <summary>
    /// Stores the logger used for synchronization diagnostics.
    /// </summary>
    private readonly ILogger<DndBeyondSyncService> _logger = logger ?? NullLogger<DndBeyondSyncService>.Instance;

    /// <summary>
    /// Stores the factory used to create a direct-proxy HTTP handler.
    /// </summary>
    private readonly Func<HttpMessageHandler> _directProxyHandlerFactory = directProxyHandlerFactory ?? (() => new DndBeyondDirectProxyHandler());

    /// <summary>
    /// Stores a value indicating whether a custom direct-proxy handler factory was supplied.
    /// </summary>
    private readonly bool _hasCustomDirectProxyHandlerFactory = directProxyHandlerFactory is not null;

    /// <summary>
    /// Synchronizes supported D&amp;D Beyond entities into the configured output directory.
    /// </summary>
    /// <param name="options">The synchronization options.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to a synchronization summary.</returns>
    public async Task<DndBeyondSyncSummary> SyncAsync(DndBeyondSyncOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        string cobaltToken = NormalizeCobaltToken(options.CobaltToken);
        if (string.IsNullOrWhiteSpace(cobaltToken))
        {
            throw new ArgumentException(Text("Core:DndBeyond:Errors:MissingCobalt"), nameof(options));
        }
        string patreonKey = options.PatreonKey?.Trim() ?? string.Empty;

        var filters = DndBeyondEntityFilters.FromOptions(options);
        if (filters is { HasAny: true, CharacterSheets.Count: > 0 } &&
            options.CampaignId is null &&
            !filters.CharacterSheets.All(IsIntegerText))
        {
            throw new ArgumentException(Text("Core:DndBeyond:Errors:CharacterSheetRequiresCampaign"), nameof(options));
        }

        string outputRoot = Path.GetFullPath(options.OutputBaseDirectory);
        SyncStarted(outputRoot, _baseUri);
        Directory.CreateDirectory(outputRoot);
        string itemsPath = EnsureSubdir(outputRoot, "items");
        string spellsPath = EnsureSubdir(outputRoot, "spells");
        string creaturesPath = EnsureSubdir(outputRoot, "creatures");
        string playersPath = EnsureSubdir(outputRoot, "players");

        ConfigureHttpHeaders();
        await ValidateCobaltAsync(cobaltToken, patreonKey, cancellationToken).ConfigureAwait(false);

        int items = filters.ShouldDownloadItems
            ? await DownloadItemsAsync(cobaltToken, patreonKey, options, filters.Items, itemsPath, cancellationToken).ConfigureAwait(false)
            : 0;
        int spells = filters.ShouldDownloadSpells
            ? await DownloadSpellsAsync(cobaltToken, patreonKey, options, filters.Spells, spellsPath, cancellationToken).ConfigureAwait(false)
            : 0;
        int creatures = 0;
        if (filters.ShouldDownloadCreatures)
        {
            try
            {
                if (IsDirectProxyBaseUri())
                {
                    creatures = await DownloadCreaturesAsync(cobaltToken, patreonKey, options, filters.Creatures, creaturesPath, cancellationToken).ConfigureAwait(false);
                }
                else if (_hasCustomDirectProxyHandlerFactory)
                {
                    CreatureDownloadRetryingDirectProxy("Using direct-proxy monster muncher path.");
                    creatures = await DownloadCreaturesWithDirectProxyFallbackAsync(cobaltToken, patreonKey, options, filters.Creatures, creaturesPath, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    creatures = await DownloadCreaturesAsync(cobaltToken, patreonKey, options, filters.Creatures, creaturesPath, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (InvalidOperationException ex) when (IsPatreonMonsterAccessFailure(ex))
            {
                CreatureDownloadSkippedWithoutPatreonKey(ex.Message);
            }
        }
        int players = filters.ShouldDownloadCharacterSheets
            ? await DownloadCharacterSheetsAsync(cobaltToken, patreonKey, options.CampaignId, filters.CharacterSheets, playersPath, cancellationToken).ConfigureAwait(false)
            : 0;

        int upgraded = 0;
        if (options.UpgradeToMarkdown)
        {
            JsonMarkdownUpgradeSummary upgradeSummary = await new JsonMarkdownUpgrader(_localizer).UpgradeDirectoryAsync(outputRoot, cancellationToken).ConfigureAwait(false);
            upgraded = upgradeSummary.ConvertedFiles;
        }

        string metadataPath = Path.Combine(outputRoot, "dndb-sync-metadata.json");
        Dictionary<string, object?> metadata = new()
        {
            ["downloadedItems"] = items,
            ["downloadedSpells"] = spells,
            ["downloadedCreatures"] = creatures,
            ["downloadedPlayers"] = players,
            ["homebrew"] = options.IncludeHomebrew,
            ["campaignId"] = options.CampaignId,
            ["itemFilters"] = filters.Items,
            ["spellFilters"] = filters.Spells,
            ["creatureFilters"] = filters.Creatures,
            ["characterSheetFilters"] = filters.CharacterSheets,
            ["upgradedMarkdownFiles"] = upgraded,
        };
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken).ConfigureAwait(false);

        SyncCompleted(items, spells, creatures, players, upgraded);
        return new(items, spells, creatures, players, SourceCount: 0, UpgradedMarkdownFiles: upgraded);
    }

    /// <summary>
    /// Configures baseline HTTP request headers for proxy calls.
    /// </summary>
    private void ConfigureHttpHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Grimoire/0.1; +https://github.com/guardians-gate/grimoire)");
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.dndbeyond.com/");
    }

    /// <summary>
    /// Validates cobalt and Patreon credentials with the proxy authentication route.
    /// </summary>
    /// <param name="cobaltToken">The normalized cobalt token.</param>
    /// <param name="patreonKey">The optional Patreon key.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    private async Task ValidateCobaltAsync(string cobaltToken, string patreonKey, CancellationToken cancellationToken)
    {
        JsonDocument auth = await PostJsonAsync("/proxy/auth", new { cobalt = cobaltToken, betaKey = patreonKey }, cancellationToken).ConfigureAwait(false);
        EnsureProxySuccess(auth, Text("Core:DndBeyond:Errors:AuthFailed"));
    }

    /// <summary>
    /// Downloads and persists item entities.
    /// </summary>
    /// <param name="cobaltToken">The normalized cobalt token.</param>
    /// <param name="patreonKey">The optional Patreon key.</param>
    /// <param name="options">The synchronization options.</param>
    /// <param name="itemNames">The item-name filters.</param>
    /// <param name="outputDirectory">The destination item directory.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the number of saved item files.</returns>
    private async Task<int> DownloadItemsAsync(
        string cobaltToken,
        string patreonKey,
        DndBeyondSyncOptions options,
        IReadOnlyCollection<string> itemNames,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        object body = new
        {
            cobalt = cobaltToken,
            campaignId = options.CampaignId?.ToString(CultureInfo.InvariantCulture),
            betaKey = patreonKey,
            addSpells = true,
        };
        JsonDocument document = await PostJsonAsync("/proxy/items", body, cancellationToken).ConfigureAwait(false);
        JsonElement data = GetProxyDataOrThrow(document, Text("Core:DndBeyond:Errors:ItemDownload"));
        List<JsonElement> items = ExtractArray(data, "items");
        if (items.Count == 0 && data.ValueKind == JsonValueKind.Array)
        {
            items = ExtractArray(data);
        }

        List<JsonElement> filtered =
        [
            .. items
                .Where(item => options.IncludeHomebrew || !GetBoolean(item, "isHomebrew"))
                .Where(item => NameMatches(item, itemNames))
        ];
        return await SaveEntitiesAsync(filtered, outputDirectory, "item", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads and persists spell entities for all configured classes.
    /// </summary>
    /// <param name="cobaltToken">The normalized cobalt token.</param>
    /// <param name="patreonKey">The optional Patreon key.</param>
    /// <param name="options">The synchronization options.</param>
    /// <param name="spellNames">The spell-name filters.</param>
    /// <param name="outputDirectory">The destination spell directory.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the number of saved spell files.</returns>
    private async Task<int> DownloadSpellsAsync(
        string cobaltToken,
        string patreonKey,
        DndBeyondSyncOptions options,
        IReadOnlyCollection<string> spellNames,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Dictionary<string, JsonElement> uniqueSpells = new(StringComparer.OrdinalIgnoreCase);
        int spellClassIndex = 0;
        foreach ((string rulesVersion, string className) in SpellLists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spellClassIndex++;
            DownloadingSpellClass(spellClassIndex, SpellLists.Length, className, rulesVersion);
            object body = new
            {
                cobalt = cobaltToken,
                campaignId = options.CampaignId?.ToString(CultureInfo.InvariantCulture),
                betaKey = patreonKey,
                className,
                rulesVersion,
            };
            JsonDocument document = await PostJsonAsync("/proxy/class/spells", body, cancellationToken).ConfigureAwait(false);
            JsonElement data = GetProxyDataOrThrow(document, Text("Core:DndBeyond:Errors:SpellDownloadForClass", className, rulesVersion));
            foreach (JsonElement spell in ExtractArray(data))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!options.IncludeHomebrew && GetBoolean(GetNestedObject(spell, "definition"), "isHomebrew"))
                {
                    continue;
                }

                if (!NameMatches(spell, spellNames))
                {
                    continue;
                }

                string key = GetEntityId(spell);
                if (!uniqueSpells.ContainsKey(key))
                {
                    uniqueSpells.Add(key, spell.Clone());
                }
            }
        }

        return await SaveEntitiesAsync(uniqueSpells.Values, outputDirectory, "spell", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads and persists creature entities.
    /// </summary>
    /// <param name="cobaltToken">The normalized cobalt token.</param>
    /// <param name="patreonKey">The optional Patreon key.</param>
    /// <param name="options">The synchronization options.</param>
    /// <param name="creatureNames">The creature-name filters.</param>
    /// <param name="outputDirectory">The destination creature directory.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the number of saved creature files.</returns>
    private async Task<int> DownloadCreaturesAsync(
        string cobaltToken,
        string patreonKey,
        DndBeyondSyncOptions options,
        IReadOnlyCollection<string> creatureNames,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        List<JsonElement> creatures = [];
        IReadOnlyCollection<string> searchTerms = creatureNames.Count > 0 ? creatureNames : [string.Empty];
        int searchTermIndex = 0;
        foreach (string searchTerm in searchTerms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            searchTermIndex++;
            DownloadingCreatureBatch(searchTermIndex, searchTerms.Count, searchTerm);
            object body = new
            {
                cobalt = cobaltToken,
                betaKey = patreonKey,
                sources = Array.Empty<int>(),
                search = searchTerm,
                searchTerm = Uri.EscapeDataString(searchTerm),
                homebrew = options.IncludeHomebrew,
                homebrewOnly = false,
                exactMatch = false,
                excludeLegacy = false,
                excludedCategories = Array.Empty<int>(),
                monsterTypes = Array.Empty<int>(),
            };
            JsonDocument document = await PostJsonAsync("/proxy/monster", body, cancellationToken).ConfigureAwait(false);
            JsonElement data;
            try
            {
                data = GetProxyDataOrThrow(document, Text("Core:DndBeyond:Errors:CreatureDownload"));
            }
            catch (InvalidOperationException ex) when (IsMonsterEndpointFallbackFailure(ex))
            {
                CreatureDownloadRetryingFallbackRoute(ex.Message);
                document = await PostJsonAsync("/proxy/monsters", body, cancellationToken).ConfigureAwait(false);
                data = GetProxyDataOrThrow(document, Text("Core:DndBeyond:Errors:CreatureDownload"));
            }

            creatures.AddRange(ExtractArray(data));
        }

        List<JsonElement> filtered =
        [
            .. Deduplicate(creatures)
                .Where(creature => options.IncludeHomebrew || !GetBoolean(creature, "isHomebrew"))
                .Where(creature => NameMatches(creature, creatureNames))
        ];
        return await SaveEntitiesAsync(filtered, outputDirectory, "creature", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads and persists character sheet entities by campaign lookup or direct identifiers.
    /// </summary>
    /// <param name="cobaltToken">The normalized cobalt token.</param>
    /// <param name="patreonKey">The optional Patreon key.</param>
    /// <param name="campaignId">The optional campaign identifier.</param>
    /// <param name="characterNames">The character-sheet filters or direct IDs.</param>
    /// <param name="outputDirectory">The destination player directory.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the number of saved player files.</returns>
    private async Task<int> DownloadCharacterSheetsAsync(
        string cobaltToken,
        string patreonKey,
        int? campaignId,
        IReadOnlyCollection<string> characterNames,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        List<string> directCharacterIds = [.. characterNames.Where(IsIntegerText)];
        if (directCharacterIds.Count > 0 && directCharacterIds.Count == characterNames.Count)
        {
            return await DownloadCharacterSheetsByIdAsync(cobaltToken, patreonKey, campaignId, directCharacterIds, outputDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (campaignId is not { } resolvedCampaignId)
        {
            return 0;
        }

        object partyBody = new
        {
            cobalt = cobaltToken,
            betaKey = patreonKey,
            campaignId = resolvedCampaignId.ToString(CultureInfo.InvariantCulture),
        };
        JsonDocument partyDocument = await PostJsonAsync($"/proxy/party/{resolvedCampaignId.ToString(CultureInfo.InvariantCulture)}/characters", partyBody, cancellationToken).ConfigureAwait(false);
        JsonElement partyData = GetProxyDataOrThrow(partyDocument, Text("Core:DndBeyond:Errors:CampaignCharacterLookup"));
        List<string> characterIds =
        [
            .. ExtractCharacterRows(partyData)
                .Where(row => NameMatches(row, characterNames))
                .Select(GetCharacterId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
        ];

        return await DownloadCharacterSheetsByIdAsync(cobaltToken, patreonKey, campaignId, characterIds, outputDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads and persists character sheet entities by explicit character identifiers.
    /// </summary>
    /// <param name="cobaltToken">The normalized cobalt token.</param>
    /// <param name="patreonKey">The optional Patreon key.</param>
    /// <param name="campaignId">The optional campaign identifier.</param>
    /// <param name="characterIds">The character identifiers to download.</param>
    /// <param name="outputDirectory">The destination player directory.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the number of saved player files.</returns>
    private async Task<int> DownloadCharacterSheetsByIdAsync(
        string cobaltToken,
        string patreonKey,
        int? campaignId,
        IReadOnlyCollection<string> characterIds,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        List<JsonElement> characterSheets = [];
        List<string> distinctCharacterIds = [.. characterIds.Distinct(StringComparer.OrdinalIgnoreCase)];
        int characterIndex = 0;
        foreach (string characterId in distinctCharacterIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            characterIndex++;
            DownloadingCharacterSheet(characterIndex, distinctCharacterIds.Count, characterId);
            object characterBody = new
            {
                cobalt = cobaltToken,
                betaKey = patreonKey,
                characterId,
                campaignId = campaignId?.ToString(CultureInfo.InvariantCulture),
                filterModifiers = false,
                splitSpells = true,
                devMode = false,
            };
            JsonDocument characterDocument = await PostJsonAsync("/proxy/v5/character", characterBody, cancellationToken).ConfigureAwait(false);
            JsonElement characterData = GetProxyDataOrThrow(characterDocument, Text("Core:DndBeyond:Errors:CharacterDownloadForId", characterId));
            characterSheets.Add(characterData.Clone());
        }

        return await SaveEntitiesAsync(characterSheets, outputDirectory, "player", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts JSON content to a proxy route and parses the JSON response payload.
    /// </summary>
    /// <param name="relativePath">The relative proxy route.</param>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the parsed JSON document.</returns>
    private async Task<JsonDocument> PostJsonAsync(string relativePath, object body, CancellationToken cancellationToken)
    {
        Uri requestUri = new(_baseUri, relativePath);
        using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (IsChallengeResponse(response, responseBody))
        {
            SecurityChallenge(requestUri);
            throw BuildChallengeException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(Text("Core:DndBeyond:Errors:RequestFailed", (int)response.StatusCode, response.ReasonPhrase ?? string.Empty, TrimForError(responseBody)));
        }

        try
        {
            return JsonDocument.Parse(responseBody);
        }
        catch (JsonException ex) when (LooksLikeHtml(responseBody))
        {
            SecurityChallenge(requestUri);
            throw BuildChallengeException(ex);
        }
    }

    /// <summary>
    /// Verifies proxy success semantics and throws when the payload reports failure.
    /// </summary>
    /// <param name="document">The proxy response document.</param>
    /// <param name="failurePrefix">The operation-specific failure prefix.</param>
    private void EnsureProxySuccess(JsonDocument document, string failurePrefix)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("success", out JsonElement successElement) &&
            successElement.ValueKind == JsonValueKind.False)
        {
            string message = document.RootElement.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.ToString()
                : "unknown proxy error";
            ProxyFailure(failurePrefix, message);
            throw new InvalidOperationException(Text("Core:DndBeyond:Errors:ProxyFailure", failurePrefix, message, CaptchaHelpUrl));
        }
    }

    /// <summary>
    /// Extracts the <c>data</c> payload from a proxy response after success validation.
    /// </summary>
    /// <param name="document">The proxy response document.</param>
    /// <param name="failurePrefix">The operation-specific failure prefix.</param>
    /// <returns>A cloned JSON element containing the data payload.</returns>
    private JsonElement GetProxyDataOrThrow(JsonDocument document, string failurePrefix)
    {
        EnsureProxySuccess(document, failurePrefix);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("data", out JsonElement data))
        {
            return data.Clone();
        }

        return document.RootElement.Clone();
    }

    /// <summary>
    /// Extracts a cloned array payload from an element or named property.
    /// </summary>
    /// <param name="element">The source JSON element.</param>
    /// <param name="propertyName">The optional property containing the array.</param>
    /// <returns>A list of cloned array elements.</returns>
    private static List<JsonElement> ExtractArray(JsonElement element, string? propertyName = null)
    {
        JsonElement target = element;
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out target))
            {
                return [];
            }
        }

        if (target.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<JsonElement> results = [];
        foreach (JsonElement item in target.EnumerateArray())
        {
            results.Add(item.Clone());
        }

        return results;
    }

    /// <summary>
    /// Extracts campaign character rows from party payload variants.
    /// </summary>
    /// <param name="partyData">The party lookup payload.</param>
    /// <returns>A list of character-row elements.</returns>
    private static List<JsonElement> ExtractCharacterRows(JsonElement partyData)
    {
        switch (partyData.ValueKind)
        {
            case JsonValueKind.Array:
                return ExtractArray(partyData);
            case JsonValueKind.Object:
            {
                List<JsonElement> rows = ExtractArray(partyData, "characters");
                return rows.Count > 0 ? rows : [];
            }
            default: return [];
        }
    }

    /// <summary>
    /// Deduplicates entities by normalized entity identifier.
    /// </summary>
    /// <param name="entities">The entities to deduplicate.</param>
    /// <returns>An enumerable sequence with unique entity identifiers.</returns>
    private static IEnumerable<JsonElement> Deduplicate(IEnumerable<JsonElement> entities)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement entity in entities)
        {
            if (seen.Add(GetEntityId(entity)))
            {
                yield return entity;
            }
        }
    }

    /// <summary>
    /// Persists entity payloads to JSON files in an output directory.
    /// </summary>
    /// <param name="entities">The entities to persist.</param>
    /// <param name="outputDirectory">The destination directory path.</param>
    /// <param name="entityType">The logical entity type for logging.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the number of saved files.</returns>
    private async Task<int> SaveEntitiesAsync(
        IEnumerable<JsonElement> entities,
        string outputDirectory,
        string entityType,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        int count = 0;
        foreach (JsonElement entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = BuildEntityFileName(entity, count);
            string path = Path.Combine(outputDirectory, fileName);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entity, JsonOptions), cancellationToken).ConfigureAwait(false);
            string entityId = GetEntityId(entity);
            string entityName = GetEntityName(entity);
            EntitySaved(entityType, entityId, entityName, path);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Builds a normalized output file name for an entity payload.
    /// </summary>
    /// <param name="entity">The entity payload.</param>
    /// <param name="index">The fallback index used when no entity ID is available.</param>
    /// <returns>A normalized JSON file name.</returns>
    private static string BuildEntityFileName(JsonElement entity, int index)
    {
        string id = GetEntityId(entity);
        if (string.IsNullOrWhiteSpace(id))
        {
            id = index.ToString(CultureInfo.InvariantCulture);
        }

        string name = GetEntityName(entity);
        string slug = EntitySlugRegex.Replace(name.ToUpperInvariant(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "entity";
        }

        return $"{id}-{slug}.json";
    }

    /// <summary>
    /// Determines whether an entity name matches the provided filters.
    /// </summary>
    /// <param name="entity">The entity payload.</param>
    /// <param name="filters">The normalized filter collection.</param>
    /// <returns><see langword="true"/> when the entity should be included; otherwise, <see langword="false"/>.</returns>
    private static bool NameMatches(JsonElement entity, IReadOnlyCollection<string> filters)
    {
        if (filters.Count == 0)
        {
            return true;
        }

        string name = NormalizeName(GetEntityName(entity));
        return filters.Any(filter => FilterMatches(name, filter));
    }

    /// <summary>
    /// Determines whether a normalized name matches a single filter expression.
    /// </summary>
    /// <param name="normalizedName">The normalized entity name.</param>
    /// <param name="filter">The raw filter expression.</param>
    /// <returns><see langword="true"/> when the filter matches; otherwise, <see langword="false"/>.</returns>
    private static bool FilterMatches(string normalizedName, string filter)
    {
        string normalizedFilter = NormalizeName(filter);
        if (normalizedFilter.Length == 0)
        {
            return true;
        }

        if (normalizedFilter.Contains('*', StringComparison.Ordinal) || normalizedFilter.Contains('?', StringComparison.Ordinal))
        {
            return WildcardMatches(normalizedName.AsSpan(), normalizedFilter.AsSpan());
        }

        return normalizedName.Contains(normalizedFilter, StringComparison.Ordinal);
    }

    /// <summary>
    /// Evaluates wildcard pattern matching with <c>*</c> and <c>?</c> semantics.
    /// </summary>
    /// <param name="value">The normalized value to test.</param>
    /// <param name="pattern">The wildcard pattern.</param>
    /// <returns><see langword="true"/> when the pattern matches; otherwise, <see langword="false"/>.</returns>
    private static bool WildcardMatches(ReadOnlySpan<char> value, ReadOnlySpan<char> pattern)
    {
        int valueIndex = 0;
        int patternIndex = 0;
        int starPatternIndex = -1;
        int matchAfterStarIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' || pattern[patternIndex] == value[valueIndex]))
            {
                valueIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starPatternIndex = patternIndex++;
                matchAfterStarIndex = valueIndex;
                continue;
            }

            if (starPatternIndex < 0) return false;
            patternIndex = starPatternIndex + 1;
            valueIndex = ++matchAfterStarIndex;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    /// <summary>
    /// Resolves a display name from common entity payload shapes.
    /// </summary>
    /// <param name="entity">The entity payload.</param>
    /// <returns>The resolved entity name, or <c>entity</c> when unavailable.</returns>
    private static string GetEntityName(JsonElement entity)
    {
        foreach (string property in new[] { "name", "characterName" })
        {
            if (entity.ValueKind == JsonValueKind.Object && entity.TryGetProperty(property, out JsonElement value))
            {
                return value.ToString();
            }
        }

        if (entity.ValueKind == JsonValueKind.Object &&
            entity.TryGetProperty("definition", out JsonElement definition) &&
            definition.ValueKind == JsonValueKind.Object &&
            definition.TryGetProperty("name", out JsonElement definitionName))
        {
            return definitionName.ToString();
        }

        if (entity.ValueKind == JsonValueKind.Object &&
            entity.TryGetProperty("ddb", out JsonElement ddb) &&
            ddb.ValueKind == JsonValueKind.Object &&
            ddb.TryGetProperty("character", out JsonElement character) &&
            character.ValueKind == JsonValueKind.Object &&
            character.TryGetProperty("name", out JsonElement characterName))
        {
            return characterName.ToString();
        }

        return "entity";
    }

    /// <summary>
    /// Resolves a stable identifier from common entity payload shapes.
    /// </summary>
    /// <param name="entity">The entity payload.</param>
    /// <returns>The resolved entity identifier.</returns>
    private static string GetEntityId(JsonElement entity)
    {
        foreach (string property in new[] { "id", "characterId" })
        {
            if (entity.ValueKind == JsonValueKind.Object && entity.TryGetProperty(property, out JsonElement value))
            {
                return value.ToString();
            }
        }

        if (entity.ValueKind == JsonValueKind.Object &&
            entity.TryGetProperty("definition", out JsonElement definition) &&
            definition.ValueKind == JsonValueKind.Object &&
            definition.TryGetProperty("id", out JsonElement definitionId))
        {
            return definitionId.ToString();
        }

        if (entity.ValueKind == JsonValueKind.Object &&
            entity.TryGetProperty("ddb", out JsonElement ddb) &&
            ddb.ValueKind == JsonValueKind.Object &&
            ddb.TryGetProperty("character", out JsonElement character) &&
            character.ValueKind == JsonValueKind.Object &&
            character.TryGetProperty("id", out JsonElement characterId))
        {
            return characterId.ToString();
        }

        return NormalizeName(GetEntityName(entity));
    }

    /// <summary>
    /// Resolves a character identifier from a campaign character row.
    /// </summary>
    /// <param name="row">The character row payload.</param>
    /// <returns>The character identifier when available; otherwise, <see langword="null"/>.</returns>
    private static string? GetCharacterId(JsonElement row)
    {
        foreach (string property in new[] { "characterId", "id" })
        {
            if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty(property, out JsonElement value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether text is a non-signed integer.
    /// </summary>
    /// <param name="value">The text to evaluate.</param>
    /// <returns><see langword="true"/> when the text parses as an integer; otherwise, <see langword="false"/>.</returns>
    private static bool IsIntegerText(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// Gets a nested object property from an entity payload when present.
    /// </summary>
    /// <param name="entity">The source payload.</param>
    /// <param name="propertyName">The object property name.</param>
    /// <returns>The nested object element, or the default element when unavailable.</returns>
    private static JsonElement GetNestedObject(JsonElement entity, string propertyName)
    {
        if (entity.ValueKind == JsonValueKind.Object &&
            entity.TryGetProperty(propertyName, out JsonElement nested) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            return nested;
        }

        return default;
    }

    /// <summary>
    /// Gets whether a JSON boolean property is explicitly <see langword="true"/>.
    /// </summary>
    /// <param name="entity">The source payload.</param>
    /// <param name="propertyName">The boolean property name.</param>
    /// <returns><see langword="true"/> when the property exists and is true; otherwise, <see langword="false"/>.</returns>
    private static bool GetBoolean(JsonElement entity, string propertyName)
    {
        return entity.ValueKind == JsonValueKind.Object &&
            entity.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Normalizes entity and filter names for case-insensitive comparison.
    /// </summary>
    /// <param name="value">The source value.</param>
    /// <returns>The normalized comparison key.</returns>
    private static string NormalizeName(string value)
    {
        return WebUtility.HtmlDecode(value)
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201c', '"')
            .Replace('\u201d', '"')
            .Trim()
            .ToUpperInvariant();
    }

    /// <summary>
    /// Normalizes cobalt token text from cookie or JSON payload formats.
    /// </summary>
    /// <param name="cobaltToken">The raw cobalt token text.</param>
    /// <returns>The normalized cobalt token.</returns>
    private static string NormalizeCobaltToken(string cobaltToken)
    {
        string token = cobaltToken.Trim();
        if (token.StartsWith("CobaltSession=", StringComparison.OrdinalIgnoreCase))
        {
            token = token["CobaltSession=".Length..];
            int separator = token.IndexOf(';', StringComparison.Ordinal);
            if (separator >= 0)
            {
                token = token[..separator];
            }
        }

        if (!token.StartsWith('{')) return token.Trim();
        try
        {
            using var document = JsonDocument.Parse(token);
            if (document.RootElement.TryGetProperty("cbt", out JsonElement cbt))
            {
                return cbt.ToString().Trim();
            }
        }
        catch (JsonException)
        {
            return token;
        }

        return token.Trim();
    }

    /// <summary>
    /// Determines whether a response body indicates a security challenge.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <param name="responseBody">The response body text.</param>
    /// <returns><see langword="true"/> when the response appears to be a challenge page; otherwise, <see langword="false"/>.</returns>
    private static bool IsChallengeResponse(HttpResponseMessage response, string responseBody)
    {
        return response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized &&
            (LooksLikeHtml(responseBody) ||
             responseBody.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
             responseBody.Contains("cloudflare", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether response text appears to be HTML content.
    /// </summary>
    /// <param name="responseBody">The response body text.</param>
    /// <returns><see langword="true"/> when the payload looks like HTML; otherwise, <see langword="false"/>.</returns>
    private static bool LooksLikeHtml(string responseBody)
    {
        string trimmed = responseBody.TrimStart();
        return trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether an error message indicates missing Patreon monster access.
    /// </summary>
    /// <param name="ex">The thrown operation exception.</param>
    /// <returns><see langword="true"/> when the message indicates Patreon access failure; otherwise, <see langword="false"/>.</returns>
    private static bool IsPatreonMonsterAccessFailure(InvalidOperationException ex)
    {
        string message = ex.Message;
        return message.Contains("patreon", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not an authorised user", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not an authorized user", StringComparison.OrdinalIgnoreCase)
            || message.Contains("beta key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether an error message should trigger monster endpoint fallback.
    /// </summary>
    /// <param name="ex">The thrown operation exception.</param>
    /// <returns><see langword="true"/> when fallback should be attempted; otherwise, <see langword="false"/>.</returns>
    private static bool IsMonsterEndpointFallbackFailure(InvalidOperationException ex)
    {
        string message = ex.Message;
        return message.Contains("unknown error during monster loading", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid json response body", StringComparison.OrdinalIgnoreCase)
            || message.Contains("monster-service.dndbeyond.com", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unexpected token '<'", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Retries creature download using the direct-proxy handler path.
    /// </summary>
    /// <param name="cobaltToken">The normalized cobalt token.</param>
    /// <param name="patreonKey">The optional Patreon key.</param>
    /// <param name="options">The synchronization options.</param>
    /// <param name="creatureNames">The creature-name filters.</param>
    /// <param name="outputDirectory">The destination creature directory.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the number of saved creature files.</returns>
    private async Task<int> DownloadCreaturesWithDirectProxyFallbackAsync(
        string cobaltToken,
        string patreonKey,
        DndBeyondSyncOptions options,
        IReadOnlyCollection<string> creatureNames,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        using HttpMessageHandler directProxyHandler = _directProxyHandlerFactory();
        using HttpClient directProxyClient = new(directProxyHandler);
        DndBeyondSyncService directProxyService = new(
            directProxyClient,
            new Uri("https://grimoire.local", UriKind.Absolute),
            _localizer,
            _logger,
            _directProxyHandlerFactory);
        directProxyService.ConfigureHttpHeaders();
        return await directProxyService
            .DownloadCreaturesAsync(cobaltToken, patreonKey, options, creatureNames, outputDirectory, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether the configured base URI is the direct-proxy host.
    /// </summary>
    /// <returns><see langword="true"/> when the host is <c>grimoire.local</c>; otherwise, <see langword="false"/>.</returns>
    private bool IsDirectProxyBaseUri()
    {
        return string.Equals(_baseUri.Host, "grimoire.local", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a localized exception for security challenge responses.
    /// </summary>
    /// <param name="innerException">The optional inner exception.</param>
    /// <returns>An <see cref="InvalidOperationException"/> describing challenge remediation guidance.</returns>
    private InvalidOperationException BuildChallengeException(Exception? innerException = null)
    {
        return new InvalidOperationException(
            Text("Core:DndBeyond:Errors:Challenge", CaptchaHelpUrl),
            innerException);
    }

    /// <summary>
    /// Resolves a localized string value by key.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <returns>The localized string value.</returns>
    private string Text(string key)
    {
        return _localizer[key].Value;
    }

    /// <summary>
    /// Resolves a formatted localized string value by key and arguments.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="arguments">The formatting arguments.</param>
    /// <returns>The localized string value.</returns>
    private string Text(string key, params object[] arguments)
    {
        return _localizer[key, arguments].Value;
    }

    /// <summary>
    /// Compacts and truncates text for inclusion in error messages.
    /// </summary>
    /// <param name="value">The source text.</param>
    /// <returns>A compact error-safe text fragment.</returns>
    private static string TrimForError(string value)
    {
        string compact = ErrorWhitespaceRegex.Replace(value, " ").Trim();
        return compact.Length <= 240 ? compact : compact[..240] + "...";
    }

    /// <summary>
    /// Ensures a child directory exists beneath a root path.
    /// </summary>
    /// <param name="root">The root directory path.</param>
    /// <param name="name">The child directory name.</param>
    /// <returns>The full child directory path.</returns>
    private static string EnsureSubdir(string root, string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Logs synchronization start details.
    /// </summary>
    /// <param name="outputRoot">The destination output root path.</param>
    /// <param name="proxyBaseUri">The proxy base URI.</param>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Starting D&D Beyond sync into {outputRoot} using proxy {proxyBaseUri}.")]
    private partial void SyncStarted(string outputRoot, Uri proxyBaseUri);

    /// <summary>
    /// Logs synchronization completion totals.
    /// </summary>
    /// <param name="items">The number of downloaded items.</param>
    /// <param name="spells">The number of downloaded spells.</param>
    /// <param name="creatures">The number of downloaded creatures.</param>
    /// <param name="players">The number of downloaded character sheets.</param>
    /// <param name="upgraded">The number of upgraded markdown files.</param>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Completed D&D Beyond sync: items={items}, spells={spells}, creatures={creatures}, players={players}, upgraded={upgraded}.")]
    private partial void SyncCompleted(int items, int spells, int creatures, int players, int upgraded);

    /// <summary>
    /// Logs proxy operation failures.
    /// </summary>
    /// <param name="operation">The failed operation label.</param>
    /// <param name="message">The proxy-reported failure message.</param>
    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "D&D Beyond proxy reported a failure for {operation}: {message}")]
    private partial void ProxyFailure(string operation, string message);

    /// <summary>
    /// Logs that a security challenge was detected.
    /// </summary>
    /// <param name="requestUri">The URI that produced the challenge.</param>
    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "D&D Beyond returned a security challenge for {requestUri}.")]
    private partial void SecurityChallenge(Uri requestUri);

    /// <summary>
    /// Logs entity persistence details.
    /// </summary>
    /// <param name="entityType">The entity type label.</param>
    /// <param name="entityId">The persisted entity identifier.</param>
    /// <param name="entityName">The persisted entity name.</param>
    /// <param name="outputPath">The output file path.</param>
    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Saved D&D Beyond {entityType}: id={entityId}, name={entityName}, path={outputPath}.")]
    private partial void EntitySaved(string entityType, string entityId, string entityName, string outputPath);

    /// <summary>
    /// Logs skipped creature synchronization caused by Patreon access failure.
    /// </summary>
    /// <param name="reason">The proxy-reported denial reason.</param>
    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "Skipping monster download because the proxy denied access (Patreon key missing or invalid): {reason}")]
    private partial void CreatureDownloadSkippedWithoutPatreonKey(string reason);

    /// <summary>
    /// Logs retry attempts against the fallback monster route.
    /// </summary>
    /// <param name="reason">The failure reason from the primary route.</param>
    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "Primary monster route failed; retrying creature sync against fallback route: {reason}")]
    private partial void CreatureDownloadRetryingFallbackRoute(string reason);

    /// <summary>
    /// Logs retry attempts using the direct-proxy fallback path.
    /// </summary>
    /// <param name="reason">The failure reason from external proxy routes.</param>
    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "External proxy monster routes failed; retrying creature sync with direct proxy fallback: {reason}")]
    private partial void CreatureDownloadRetryingDirectProxy(string reason);

    /// <summary>
    /// Logs spell class-batch download progress.
    /// </summary>
    /// <param name="index">The one-based batch index.</param>
    /// <param name="total">The total class batch count.</param>
    /// <param name="className">The class name for the batch.</param>
    /// <param name="rulesVersion">The rules version for the batch.</param>
    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug, Message = "Downloading spells for class batch {index}/{total}: {className} ({rulesVersion}).")]
    private partial void DownloadingSpellClass(int index, int total, string className, string rulesVersion);

    /// <summary>
    /// Logs creature-batch download progress.
    /// </summary>
    /// <param name="index">The one-based batch index.</param>
    /// <param name="total">The total batch count.</param>
    /// <param name="searchTerm">The search term for the batch.</param>
    [LoggerMessage(EventId = 1010, Level = LogLevel.Debug, Message = "Downloading creature batch {index}/{total} using search term '{searchTerm}'.")]
    private partial void DownloadingCreatureBatch(int index, int total, string searchTerm);

    /// <summary>
    /// Logs character-sheet download progress.
    /// </summary>
    /// <param name="index">The one-based character index.</param>
    /// <param name="total">The total character count.</param>
    /// <param name="characterId">The character identifier.</param>
    [LoggerMessage(EventId = 1011, Level = LogLevel.Debug, Message = "Downloading character sheet {index}/{total} for character id {characterId}.")]
    private partial void DownloadingCharacterSheet(int index, int total, string characterId);

    /// <summary>
    /// Gets a <see cref="Regex"/> representing entity slug separators.
    /// </summary>
    [GeneratedRegex(@"[^A-Z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EntitySlugRegex { get; }

    /// <summary>
    /// Gets a <see cref="Regex"/> representing collapsible whitespace for error formatting.
    /// </summary>
    [GeneratedRegex(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorWhitespaceRegex { get; }

    /// <summary>
    /// Represents normalized entity filter collections for sync selection.
    /// </summary>
    private sealed record DndBeyondEntityFilters
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DndBeyondEntityFilters"/> record.
        /// </summary>
        /// <param name="items">The normalized item filters.</param>
        /// <param name="creatures">The normalized creature filters.</param>
        /// <param name="spells">The normalized spell filters.</param>
        /// <param name="characterSheets">The normalized character-sheet filters.</param>
        public DndBeyondEntityFilters(
            IReadOnlyList<string> items,
            IReadOnlyList<string> creatures,
            IReadOnlyList<string> spells,
            IReadOnlyList<string> characterSheets)
        {
            Items = items;
            Creatures = creatures;
            Spells = spells;
            CharacterSheets = characterSheets;
        }

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing item filters.
        /// </summary>
        public IReadOnlyList<string> Items { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing creature filters.
        /// </summary>
        public IReadOnlyList<string> Creatures { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing spell filters.
        /// </summary>
        public IReadOnlyList<string> Spells { get; init; }

        /// <summary>
        /// Gets or sets a <see cref="IReadOnlyList{String}"/> representing character-sheet filters.
        /// </summary>
        public IReadOnlyList<string> CharacterSheets { get; init; }

        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether any explicit filters were provided.
        /// </summary>
        public bool HasAny => Items.Count > 0 || Creatures.Count > 0 || Spells.Count > 0 || CharacterSheets.Count > 0;

        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether item download should execute.
        /// </summary>
        public bool ShouldDownloadItems => !HasAny || Items.Count > 0;

        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether creature download should execute.
        /// </summary>
        public bool ShouldDownloadCreatures => !HasAny || Creatures.Count > 0;

        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether spell download should execute.
        /// </summary>
        public bool ShouldDownloadSpells => !HasAny || Spells.Count > 0;

        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether character-sheet download should execute.
        /// </summary>
        public bool ShouldDownloadCharacterSheets => !HasAny || CharacterSheets.Count > 0;

        /// <summary>
        /// Creates normalized filters from synchronization options.
        /// </summary>
        /// <param name="options">The synchronization options.</param>
        /// <returns>A normalized filter record.</returns>
        public static DndBeyondEntityFilters FromOptions(DndBeyondSyncOptions options)
        {
            return new(
                NormalizeFilters(options.ItemNames),
                NormalizeFilters(options.CreatureNames),
                NormalizeFilters(options.SpellNames),
                NormalizeFilters(options.CharacterSheetNames));
        }

        /// <summary>
        /// Normalizes raw filter values by trimming blanks and removing duplicates.
        /// </summary>
        /// <param name="values">The optional raw filter values.</param>
        /// <returns>A normalized filter array.</returns>
        private static string[] NormalizeFilters(IReadOnlyList<string>? values)
        {
            return values is null
                ? []
                : [.. values
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }
}

/// <summary>
/// Represents aggregate counts from a D&amp;D Beyond synchronization operation.
/// </summary>
public sealed record DndBeyondSyncSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DndBeyondSyncSummary"/> record.
    /// </summary>
    /// <param name="Items">The downloaded item count.</param>
    /// <param name="Spells">The downloaded spell count.</param>
    /// <param name="Creatures">The downloaded creature count.</param>
    /// <param name="Players">The downloaded character-sheet count.</param>
    /// <param name="SourceCount">The source count value.</param>
    /// <param name="UpgradedMarkdownFiles">The upgraded markdown file count.</param>
    public DndBeyondSyncSummary(int Items, int Spells, int Creatures, int Players, int SourceCount, int UpgradedMarkdownFiles = 0)
    {
        this.Items = Items;
        this.Spells = Spells;
        this.Creatures = Creatures;
        this.Players = Players;
        this.SourceCount = SourceCount;
        this.UpgradedMarkdownFiles = UpgradedMarkdownFiles;
    }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the downloaded item count.
    /// </summary>
    public int Items { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the downloaded spell count.
    /// </summary>
    public int Spells { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the downloaded creature count.
    /// </summary>
    public int Creatures { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the downloaded character-sheet count.
    /// </summary>
    public int Players { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the source count.
    /// </summary>
    public int SourceCount { get; init; }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the upgraded markdown file count.
    /// </summary>
    public int UpgradedMarkdownFiles { get; init; }
}

/// <summary>
/// Represents the result of upgrading JSON files to markdown files.
/// </summary>
public sealed record JsonMarkdownUpgradeSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMarkdownUpgradeSummary"/> record.
    /// </summary>
    /// <param name="convertedFiles">The number of converted files.</param>
    public JsonMarkdownUpgradeSummary(int convertedFiles)
    {
        ConvertedFiles = convertedFiles;
    }

    /// <summary>
    /// Gets or sets a <see cref="int"/> indicating the number of converted files.
    /// </summary>
    public int ConvertedFiles { get; init; }
}
