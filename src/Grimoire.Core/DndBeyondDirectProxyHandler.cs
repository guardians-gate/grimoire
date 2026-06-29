using Grimoire.Core.Localization;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Immutable;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Core;

/// <summary>
/// Implements a local direct proxy for selected D&amp;D Beyond endpoints.
/// </summary>
public sealed partial class DndBeyondDirectProxyHandler(
    HttpMessageHandler? upstreamHandler = null,
    IStringLocalizer? localizer = null,
    ILogger<DndBeyondDirectProxyHandler>? inputLogger = null)
    : HttpMessageHandler
{
    /// <summary>
    /// Stores JSON serialization settings used for proxy responses.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Stores class-name metadata required for class spell route expansion.
    /// </summary>
    private static readonly ImmutableDictionary<string, (int Id, string SpellType)> ClassMap =
        ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            new Dictionary<string, (int Id, string SpellType)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Bard"] = (1, "SPELLS"),
                ["Cleric"] = (2, "KNOWN"),
                ["Druid"] = (3, "KNOWN"),
                ["Paladin"] = (4, "KNOWN"),
                ["Ranger"] = (5, "SPELLS"),
                ["Sorcerer"] = (6, "SPELLS"),
                ["Warlock"] = (7, "SPELLS"),
                ["Wizard"] = (8, "SPELLS"),
                ["Artificer"] = (252717, "KNOWN"),
                ["Graviturgy"] = (400661, "SPELLS"),
                ["Chronurgy"] = (400659, "SPELLS"),
            });

    /// <summary>
    /// Stores the upstream HTTP client used for outbound D&amp;D Beyond requests.
    /// </summary>
    private readonly HttpClient _upstream = upstreamHandler is null ? new() : new HttpClient(upstreamHandler);

    /// <summary>
    /// Stores cached bearer tokens keyed by cobalt token hash.
    /// </summary>
    private readonly Dictionary<string, string> _bearerTokens = new(StringComparer.Ordinal);

    /// <summary>
    /// Stores the localizer used for proxy response messages.
    /// </summary>
    private readonly IStringLocalizer _localizer = localizer ?? new GrimoireLocalizationFactory().CreateDefault();

    /// <summary>
    /// Stores the logger used for proxy diagnostics.
    /// </summary>
    private readonly ILogger<DndBeyondDirectProxyHandler> logger = inputLogger ?? NullLogger<DndBeyondDirectProxyHandler>.Instance;

    /// <summary>
    /// Routes incoming proxy requests to direct D&amp;D Beyond endpoint handlers.
    /// </summary>
    /// <param name="request">The inbound HTTP request.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to an HTTP response payload.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpMessageHandler returns HttpResponseMessage ownership to HttpClient.")]
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = request.RequestUri?.AbsolutePath ?? string.Empty;
        HandlingRoute(path);
        JsonObject body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
        switch (path)
        {
            case "/proxy/auth":
                return await AuthAsync(body, cancellationToken).ConfigureAwait(false);
            case "/proxy/items":
                return await ItemsAsync(body, cancellationToken).ConfigureAwait(false);
            case "/proxy/class/spells":
                return await ClassSpellsAsync(body, cancellationToken).ConfigureAwait(false);
            case "/proxy/monster":
            case "/proxy/monsters":
                return await MonstersAsync(body, cancellationToken).ConfigureAwait(false);
            case "/proxy/v5/character":
            case "/proxy/character":
                return await CharacterAsync(body, cancellationToken).ConfigureAwait(false);
            case "/proxy/campaigns":
                return await CampaignsAsync(body, cancellationToken).ConfigureAwait(false);
            default:
                if (path.StartsWith("/proxy/party/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/characters", StringComparison.OrdinalIgnoreCase))
                {
                    return await PartyCharactersAsync(body, cancellationToken).ConfigureAwait(false);
                }

                UnsupportedRoute(path);
                return JsonResponse(new { success = false, message = Text("Core:DirectProxy:Messages:UnsupportedRoute", path) });
        }
    }

    /// <summary>
    /// Validates cobalt authentication by requesting a bearer token.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to an authentication response.</returns>
    private async Task<HttpResponseMessage> AuthAsync(JsonObject body, CancellationToken cancellationToken)
    {
        string cobalt = GetRequiredString(body, "cobalt");
        string? token = await GetBearerTokenAsync(cobalt, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            InvalidCobaltToken();
        }
        return string.IsNullOrWhiteSpace(token)
            ? JsonResponse(new { success = false, message = Text("Core:DirectProxy:Messages:InvalidCobalt") })
            : JsonResponse(new { success = true, message = Text("Core:DirectProxy:Messages:Authenticated") });
    }

    /// <summary>
    /// Proxies item retrieval and filters unsupported source content.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to an items response.</returns>
    private async Task<HttpResponseMessage> ItemsAsync(JsonObject body, CancellationToken cancellationToken)
    {
        string cobalt = GetRequiredString(body, "cobalt");
        string? campaignId = GetString(body, "campaignId");
        JsonNode json = await GetDdbJsonAsync(cobalt, BuildItemsUrl(campaignId), cancellationToken).ConfigureAwait(false);
        JsonArray data = GetDataArray(json);
        JsonArray filtered = new(
        [
            .. data
                .Where(static item => !HasSource(item, 39))
                .Select(static item => item?.DeepClone())
                .Where(static item => item is not null),
        ]);
        return JsonResponse(new { success = true, message = Text("Core:DirectProxy:Messages:ItemsReceived"), data = filtered });
    }

    /// <summary>
    /// Proxies class spell retrieval and merges class-specific supplemental spell payloads.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to a class spells response.</returns>
    private async Task<HttpResponseMessage> ClassSpellsAsync(JsonObject body, CancellationToken cancellationToken)
    {
        string cobalt = GetRequiredString(body, "cobalt");
        string className = GetRequiredString(body, "className");
        string? campaignId = GetString(body, "campaignId");
        if (!ClassMap.TryGetValue(className, out (int Id, string SpellType) classInfo))
        {
            return JsonResponse(new { success = false, message = Text("Core:DirectProxy:Messages:InvalidQuery") });
        }

        JsonArray spells = [];
        await AddSpellPayloadAsync(spells, cobalt, BuildSpellsUrl(classInfo.Id, campaignId), cancellationToken).ConfigureAwait(false);
        if (string.Equals(classInfo.SpellType, "KNOWN", StringComparison.OrdinalIgnoreCase))
        {
            await AddSpellPayloadAsync(spells, cobalt, BuildAlwaysKnownSpellsUrl(classInfo.Id, campaignId), cancellationToken).ConfigureAwait(false);
            await AddSpellPayloadAsync(spells, cobalt, BuildAlwaysPreparedSpellsUrl(classInfo.Id, campaignId), cancellationToken).ConfigureAwait(false);
        }

        JsonArray filtered = new(
        [
            .. spells
                .Where(static spell => !HasSource(spell, 39))
                .Select(static spell => spell?.DeepClone())
                .Where(static spell => spell is not null),
        ]);
        return JsonResponse(new { success = true, message = Text("Core:DirectProxy:Messages:SpellsReceived"), data = filtered });
    }

    /// <summary>
    /// Proxies paged monster retrieval and returns eligible monster entities.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to a monsters response.</returns>
    private async Task<HttpResponseMessage> MonstersAsync(JsonObject body, CancellationToken cancellationToken)
    {
        string cobalt = GetRequiredString(body, "cobalt");
        string searchTerm = GetString(body, "searchTerm") ?? GetString(body, "search") ?? string.Empty;
        bool homebrew = GetBoolean(body, "homebrew");
        bool homebrewOnly = GetBoolean(body, "homebrewOnly");
        JsonArray sources = GetArray(body, "sources");

        JsonNode first = await GetDdbJsonAsync(cobalt, BuildMonstersUrl(skip: 0, take: 1, searchTerm, homebrew, homebrewOnly, sources), cancellationToken).ConfigureAwait(false);
        int total = first["pagination"]?["total"]?.GetValue<int>() ?? GetDataArray(first).Count;
        JsonArray monsters = [];
        int pageIndex = 0;
        int totalPages = Math.Max(1, (int)Math.Ceiling((total + 1) / 100d));
        for (int skip = 0; skip <= total; skip += 100)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageIndex++;
            DownloadingMonsterPage(pageIndex, totalPages, skip, searchTerm);
            JsonNode page = await GetDdbJsonAsync(cobalt, BuildMonstersUrl(skip, 100, searchTerm, homebrew, homebrewOnly, sources), cancellationToken).ConfigureAwait(false);
            foreach (JsonNode? monster in GetDataArray(page))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (monster is null)
                {
                    continue;
                }

                bool isHomebrew = monster["isHomebrew"]?.GetValue<bool>() == true;
                bool isReleased = monster["isReleased"]?.GetValue<bool>() == true;
                if (isReleased || (homebrew && isHomebrew))
                {
                    monsters.Add(monster.DeepClone());
                }
            }
        }

        return JsonResponse(new { success = true, message = Text("Core:DirectProxy:Messages:MonstersReceived"), data = monsters });
    }

    /// <summary>
    /// Proxies character-sheet retrieval and wraps the payload in the expected structure.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to a character response.</returns>
    private async Task<HttpResponseMessage> CharacterAsync(JsonObject body, CancellationToken cancellationToken)
    {
        string cobalt = GetRequiredString(body, "cobalt");
        string characterId = GetRequiredString(body, "characterId");
        JsonNode json = await GetDdbJsonAsync(cobalt, $"https://character-service.dndbeyond.com/character/v5/character/{Uri.EscapeDataString(characterId)}?includeCustomItems=true", cancellationToken).ConfigureAwait(false);
        JsonNode data = json["data"]?.DeepClone() ?? new JsonObject();
        return JsonResponse(new { success = true, messages = CharacterReceivedMessages, data = new JsonObject { ["ddb"] = new JsonObject { ["character"] = data } } });
    }

    /// <summary>
    /// Proxies campaign list retrieval.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to a campaigns response.</returns>
    private async Task<HttpResponseMessage> CampaignsAsync(JsonObject body, CancellationToken cancellationToken)
    {
        string cobalt = GetRequiredString(body, "cobalt");
        JsonNode json = await GetDdbJsonWithCookieAsync(cobalt, "https://www.dndbeyond.com/api/campaign/stt/user-campaigns", cancellationToken).ConfigureAwait(false);
        JsonNode data = json["data"]?.DeepClone() ?? new JsonArray();
        return JsonResponse(new { success = true, message = Text("Core:DirectProxy:Messages:CampaignsReceived"), data });
    }

    /// <summary>
    /// Resolves campaign character rows for a requested campaign.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to a campaign characters response.</returns>
    private async Task<HttpResponseMessage> PartyCharactersAsync(JsonObject body, CancellationToken cancellationToken)
    {
        string campaignId = GetRequiredString(body, "campaignId");
        using HttpResponseMessage campaignsResponse = await CampaignsAsync(body, cancellationToken).ConfigureAwait(false);
        await using Stream stream = await campaignsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? payload = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonArray campaigns = payload?["data"]?.AsArray() ?? [];
        JsonNode? campaign = null;
        foreach (JsonNode? node in campaigns)
        {
            if (string.Equals(node?["id"]?.ToString(), campaignId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node?["campaignId"]?.ToString(), campaignId, StringComparison.OrdinalIgnoreCase))
            {
                campaign = node;
                break;
            }
        }
        JsonArray characters = campaign?["characters"]?.DeepClone() as JsonArray ?? [];
        JsonObject data = new()
        {
            ["campaignId"] = int.TryParse(campaignId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCampaignId) ? parsedCampaignId : campaignId,
            ["characters"] = characters,
        };
        return JsonResponse(new { success = true, message = Text("Core:DirectProxy:Messages:CampaignCharactersReceived"), data });
    }

    /// <summary>
    /// Retrieves and caches a bearer token for a cobalt session token.
    /// </summary>
    /// <param name="cobalt">The cobalt session token.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the bearer token when available.</returns>
    private async Task<string?> GetBearerTokenAsync(string cobalt, CancellationToken cancellationToken)
    {
        string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cobalt)));
        if (_bearerTokens.TryGetValue(cacheKey, out string? cachedToken))
        {
            return cachedToken;
        }

        using HttpRequestMessage request = new(HttpMethod.Post, "https://auth-service.dndbeyond.com/v1/cobalt-token");
        request.Headers.TryAddWithoutValidation("Cookie", $"CobaltSession={cobalt}");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _upstream.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            BearerTokenRequestFailed((int)response.StatusCode);
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? json = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        string? token = json?["token"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(token))
        {
            _bearerTokens[cacheKey] = token;
        }

        return token;
    }

    /// <summary>
    /// Sends an authenticated GET request to a D&amp;D Beyond JSON endpoint.
    /// </summary>
    /// <param name="cobalt">The cobalt session token.</param>
    /// <param name="url">The endpoint URL.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the parsed JSON node.</returns>
    private async Task<JsonNode> GetDdbJsonAsync(string cobalt, string url, CancellationToken cancellationToken)
    {
        string token = await GetBearerTokenAsync(cobalt, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException(Text("Core:DirectProxy:Messages:InvalidCobalt"));
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "Foundry VTT Character Integrator");
        using HttpResponseMessage response = await _upstream.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? new JsonObject();
    }

    /// <summary>
    /// Sends an authenticated GET request with both bearer and cookie headers.
    /// </summary>
    /// <param name="cobalt">The cobalt session token.</param>
    /// <param name="url">The endpoint URL.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the parsed JSON node.</returns>
    private async Task<JsonNode> GetDdbJsonWithCookieAsync(string cobalt, string url, CancellationToken cancellationToken)
    {
        string token = await GetBearerTokenAsync(cobalt, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException(Text("Core:DirectProxy:Messages:InvalidCobalt"));
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Cookie", $"CobaltSession={cobalt}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "Foundry VTT Character Integrator");
        using HttpResponseMessage response = await _upstream.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? new JsonObject();
    }

    /// <summary>
    /// Reads a JSON object body from an HTTP request.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    /// <returns>A task that resolves to the parsed request body.</returns>
    private static async Task<JsonObject> ReadBodyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return [];
        }

        await using Stream stream = await request.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? body = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return body as JsonObject ?? [];
    }

    /// <summary>
    /// Appends spell payload data from a D&amp;D Beyond endpoint to a target array.
    /// </summary>
    /// <param name="target">The target array receiving cloned spell nodes.</param>
    /// <param name="cobalt">The cobalt session token.</param>
    /// <param name="url">The endpoint URL.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous operation.</param>
    private async Task AddSpellPayloadAsync(JsonArray target, string cobalt, string url, CancellationToken cancellationToken)
    {
        JsonNode json = await GetDdbJsonAsync(cobalt, url, cancellationToken).ConfigureAwait(false);
        foreach (JsonNode? spell in GetDataArray(json))
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.Add(spell?.DeepClone());
        }
    }

    /// <summary>
    /// Extracts the <c>data</c> array from a proxy payload.
    /// </summary>
    /// <param name="json">The source JSON payload.</param>
    /// <returns>The extracted data array, or an empty array when unavailable.</returns>
    private static JsonArray GetDataArray(JsonNode? json)
    {
        return json?["data"] as JsonArray ?? [];
    }

    /// <summary>
    /// Determines whether a payload contains a given source identifier.
    /// </summary>
    /// <param name="node">The source payload.</param>
    /// <param name="sourceId">The source identifier.</param>
    /// <returns><see langword="true"/> when the source identifier is present; otherwise, <see langword="false"/>.</returns>
    private static bool HasSource(JsonNode? node, int sourceId)
    {
        JsonArray? sources = node?["sources"] as JsonArray ?? node?["definition"]?["sources"] as JsonArray;
        if (sources is null)
        {
            return false;
        }

        foreach (JsonNode? source in sources)
        {
            if (source?["sourceId"]?.GetValue<int>() == sourceId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a required string value from a request body.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The resolved string, or an empty string when unavailable.</returns>
    private static string GetRequiredString(JsonObject body, string key)
    {
        return GetString(body, key) ?? string.Empty;
    }

    /// <summary>
    /// Gets an optional string value from a request body.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The resolved string value, or <see langword="null"/> when unavailable.</returns>
    private static string? GetString(JsonObject body, string key)
    {
        return body.TryGetPropertyValue(key, out JsonNode? value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Gets whether a request body property is explicitly <see langword="true"/>.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="key">The property key.</param>
    /// <returns><see langword="true"/> when the property exists and is true; otherwise, <see langword="false"/>.</returns>
    private static bool GetBoolean(JsonObject body, string key)
    {
        return body.TryGetPropertyValue(key, out JsonNode? value) && value?.GetValue<bool>() == true;
    }

    /// <summary>
    /// Gets a JSON array from a request body property.
    /// </summary>
    /// <param name="body">The request body payload.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The resolved array, or an empty array when unavailable.</returns>
    private static JsonArray GetArray(JsonObject body, string key)
    {
        return body.TryGetPropertyValue(key, out JsonNode? value) && value is JsonArray array ? array : [];
    }

    /// <summary>
    /// Builds the item endpoint URL for an optional campaign.
    /// </summary>
    /// <param name="campaignId">The optional campaign identifier.</param>
    /// <returns>The item endpoint URL.</returns>
    private static string BuildItemsUrl(string? campaignId)
    {
        string campaign = string.IsNullOrWhiteSpace(campaignId) ? string.Empty : $"&campaignId={Uri.EscapeDataString(campaignId)}";
        return $"https://character-service.dndbeyond.com/character/v5/game-data/items?sharingSetting=2{campaign}";
    }

    /// <summary>
    /// Builds the class spell endpoint URL for an optional campaign.
    /// </summary>
    /// <param name="classId">The D&amp;D Beyond class identifier.</param>
    /// <param name="campaignId">The optional campaign identifier.</param>
    /// <returns>The class spell endpoint URL.</returns>
    private static string BuildSpellsUrl(int classId, string? campaignId)
    {
        string campaign = string.IsNullOrWhiteSpace(campaignId) ? string.Empty : $"&campaignId={Uri.EscapeDataString(campaignId)}";
        return $"https://character-service.dndbeyond.com/character/v5/game-data/spells?classId={classId.ToString(CultureInfo.InvariantCulture)}&classLevel=20&sharingSetting=2{campaign}";
    }

    /// <summary>
    /// Builds the always-known spell endpoint URL for an optional campaign.
    /// </summary>
    /// <param name="classId">The D&amp;D Beyond class identifier.</param>
    /// <param name="campaignId">The optional campaign identifier.</param>
    /// <returns>The always-known spell endpoint URL.</returns>
    private static string BuildAlwaysKnownSpellsUrl(int classId, string? campaignId)
    {
        string campaign = string.IsNullOrWhiteSpace(campaignId) ? string.Empty : $"&campaignId={Uri.EscapeDataString(campaignId)}";
        return $"https://character-service.dndbeyond.com/character/v5/game-data/always-known-spells?classId={classId.ToString(CultureInfo.InvariantCulture)}&classLevel=20&sharingSetting=2{campaign}";
    }

    /// <summary>
    /// Builds the always-prepared spell endpoint URL for an optional campaign.
    /// </summary>
    /// <param name="classId">The D&amp;D Beyond class identifier.</param>
    /// <param name="campaignId">The optional campaign identifier.</param>
    /// <returns>The always-prepared spell endpoint URL.</returns>
    private static string BuildAlwaysPreparedSpellsUrl(int classId, string? campaignId)
    {
        string campaign = string.IsNullOrWhiteSpace(campaignId) ? string.Empty : $"&campaignId={Uri.EscapeDataString(campaignId)}";
        return $"https://character-service.dndbeyond.com/character/v5/game-data/always-prepared-spells?classId={classId.ToString(CultureInfo.InvariantCulture)}&classLevel=20&sharingSetting=2{campaign}";
    }

    /// <summary>
    /// Builds the monster endpoint URL for a paged search request.
    /// </summary>
    /// <param name="skip">The number of rows to skip.</param>
    /// <param name="take">The page size.</param>
    /// <param name="search">The search term.</param>
    /// <param name="homebrew">Whether homebrew monsters are included.</param>
    /// <param name="homebrewOnly">Whether only homebrew monsters are included.</param>
    /// <param name="sources">The selected source identifiers.</param>
    /// <returns>The monster endpoint URL.</returns>
    private static string BuildMonstersUrl(int skip, int take, string search, bool homebrew, bool homebrewOnly, JsonArray sources)
    {
        string sourceSearch = string.Concat(sources.Select(static source => $"&sources={Uri.EscapeDataString(source?.ToString() ?? string.Empty)}"));
        string useHomebrew = homebrew ? string.Empty : "&showHomebrew=f";
        if (homebrewOnly)
        {
            sourceSearch = string.Empty;
            useHomebrew = "&showHomebrew=t";
        }

        return $"https://monster-service.dndbeyond.com/v1/Monster?search={Uri.EscapeDataString(search)}&skip={skip.ToString(CultureInfo.InvariantCulture)}&take={take.ToString(CultureInfo.InvariantCulture)}{useHomebrew}{sourceSearch}";
    }

    /// <summary>
    /// Creates a JSON HTTP response with success status code.
    /// </summary>
    /// <param name="payload">The payload object to serialize.</param>
    /// <returns>A JSON HTTP response message.</returns>
    private static HttpResponseMessage JsonResponse(object payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Resolves a localized string by key.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <returns>The localized string value.</returns>
    private string Text(string key)
    {
        return _localizer[key].Value;
    }

    /// <summary>
    /// Resolves a formatted localized string by key and arguments.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="arguments">The formatting arguments.</param>
    /// <returns>The localized string value.</returns>
    private string Text(string key, params object[] arguments)
    {
        return _localizer[key, arguments].Value;
    }

    /// <summary>
    /// Gets a <see cref="IReadOnlyList{String}"/> representing character success messages.
    /// </summary>
    private IReadOnlyList<string> CharacterReceivedMessages => [Text("Core:DirectProxy:Messages:CharacterReceived")];

    /// <summary>
    /// Releases managed resources held by this handler.
    /// </summary>
    /// <param name="disposing">A value indicating whether managed resources should be disposed.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _upstream.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Logs handling of an incoming direct-proxy route.
    /// </summary>
    /// <param name="path">The request path.</param>
    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug, Message = "Handling direct proxy route {path}.")]
    private partial void HandlingRoute(string path);

    /// <summary>
    /// Logs unsupported route requests.
    /// </summary>
    /// <param name="path">The unsupported request path.</param>
    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Unsupported direct proxy route {path}.")]
    private partial void UnsupportedRoute(string path);

    /// <summary>
    /// Logs invalid cobalt token authentication attempts.
    /// </summary>
    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Received invalid cobalt token for direct proxy authentication.")]
    private partial void InvalidCobaltToken();

    /// <summary>
    /// Logs bearer token request failures.
    /// </summary>
    /// <param name="statusCode">The failing HTTP status code.</param>
    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "Bearer token request failed with status code {statusCode}.")]
    private partial void BearerTokenRequestFailed(int statusCode);

    /// <summary>
    /// Logs monster page download progress.
    /// </summary>
    /// <param name="index">The one-based page index.</param>
    /// <param name="totalPages">The total page count.</param>
    /// <param name="skip">The skip value used for the page request.</param>
    /// <param name="searchTerm">The monster search term.</param>
    [LoggerMessage(EventId = 1104, Level = LogLevel.Debug, Message = "Downloading monster page {index}/{totalPages} (skip={skip}) for search term '{searchTerm}'.")]
    private partial void DownloadingMonsterPage(int index, int totalPages, int skip, string searchTerm);
}
