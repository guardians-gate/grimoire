using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Grimoire.Core;
using Microsoft.Extensions.Logging;

namespace Grimoire.Core.Tests;

/// <summary>
/// Verifies synchronization and upgrade behavior for <see cref="DndBeyondSyncService"/>.
/// </summary>
public sealed class DndBeyondSyncServiceTests
{
    /// <summary>
    /// Verifies that synchronization writes category files to expected output subdirectories.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncWritesJsonIntoExpectedSubdirectoriesAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using StubHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: true,
            CampaignId: 42,
            UpgradeToMarkdown: true);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "dndb-sync-metadata.json")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "items")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "spells")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "creatures")));
        Assert.True(Directory.Exists(Path.Combine(workspace.RootPath, "players")));
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "items"), "*.md").Length > 0);
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "spells"), "*.md").Length > 0);
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "creatures"), "*.md").Length > 0);
        Assert.True(Directory.GetFiles(Path.Combine(workspace.RootPath, "players"), "*.md").Length > 0);
        Assert.True(summary.Items > 0);
        Assert.True(summary.Spells > 0);
        Assert.True(summary.Creatures > 0);
        Assert.True(summary.Players > 0);
        Assert.True(summary.UpgradedMarkdownFiles > 0);
    }

    /// <summary>
    /// Verifies that inclusive wildcard filters constrain synchronized entities by name.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncAppliesInclusiveNameFiltersAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using StubHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: 42,
            ItemNames: ["Long*"],
            CreatureNames: ["Gob?in"]);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None);

        Assert.Equal(1, summary.Items);
        Assert.Equal(0, summary.Spells);
        Assert.Equal(1, summary.Creatures);
        Assert.Equal(0, summary.Players);
        Assert.Single(Directory.GetFiles(Path.Combine(workspace.RootPath, "items"), "*.json"));
        Assert.Empty(Directory.GetFiles(Path.Combine(workspace.RootPath, "spells"), "*.json"));
        Assert.Single(Directory.GetFiles(Path.Combine(workspace.RootPath, "creatures"), "*.json"));
    }

    /// <summary>
    /// Verifies that challenge responses surface a user-actionable exception message.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncReportsChallengeResponsesAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using ChallengeHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SyncAsync(new DndBeyondSyncOptions("token", workspace.RootPath, false, null), CancellationToken.None))
            .ConfigureAwait(true);

        Assert.Contains("clear any security challenge or CAPTCHA", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that synchronization can run through the in-process direct proxy handler.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncUsesInProcessSelfProxyHandlerAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using DirectProxyUpstreamHandler upstream = new();
        using DndBeyondDirectProxyHandler proxy = new(upstream);
        using HttpClient client = new(proxy);
        DndBeyondSyncService service = new(client, new Uri("https://grimoire.local", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: null,
            ItemNames: ["Long*"]);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, summary.Items);
        Assert.Equal(0, summary.Spells);
        Assert.Equal(0, summary.Creatures);
        Assert.Equal(0, summary.Players);
        Assert.Contains("https://auth-service.dndbeyond.com/v1/cobalt-token", upstream.Requests, StringComparer.Ordinal);
        Assert.Contains(
            "https://character-service.dndbeyond.com/character/v5/game-data/items?sharingSetting=2",
            upstream.Requests,
            StringComparer.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(workspace.RootPath, "items"), "*.json"));
    }

    /// <summary>
    /// Verifies that Patreon keys are included in proxy request payloads.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncSendsPatreonKeyToProxyPayloadsAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using PatreonKeyHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: null,
            ItemNames: ["Long*"],
            PatreonKey: "patreon-key");

        await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.NotEmpty(handler.BetaKeys);
        Assert.All(handler.BetaKeys, static key => Assert.Equal("patreon-key", key));
    }

    /// <summary>
    /// Verifies that numeric character sheet filters are downloaded without campaign context.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncDownloadsNumericCharacterSheetFiltersWithoutCampaignAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using StubHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: null,
            CharacterSheetNames: ["99"]);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None);

        Assert.Equal(1, summary.Players);
        Assert.Single(Directory.GetFiles(Path.Combine(workspace.RootPath, "players"), "*.json"));
    }

    /// <summary>
    /// Verifies that monster synchronization is skipped when proxy responses require a Patreon key.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncSkipsMonsterCategoryWhenProxyRequiresPatreonKeyAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using MonsterPatreonRequiredHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: 42);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, summary.Creatures);
        Assert.True(summary.Items > 0);
        Assert.True(summary.Spells > 0);
        Assert.True(summary.Players > 0);
    }

    /// <summary>
    /// Verifies that monster synchronization is skipped when an invalid Patreon key is rejected.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncSkipsMonsterCategoryWhenProxyRejectsInvalidPatreonKeyAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using MonsterPatreonRequiredHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: 42,
            PatreonKey: "invalid-key");

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, summary.Creatures);
        Assert.True(summary.Items > 0);
        Assert.True(summary.Spells > 0);
        Assert.True(summary.Players > 0);
    }

    /// <summary>
    /// Verifies that monster downloads retry using the alternate fallback route.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncRetriesCreatureDownloadAgainstAlternateMonsterRouteAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using MonsterFallbackRouteHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(client, new Uri("https://grimoire.local", UriKind.Absolute));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: 42);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.True(handler.FallbackRouteCalled);
        Assert.True(summary.Creatures > 0);
    }

    /// <summary>
    /// Verifies that direct proxy fallback recovers monster downloads when external routes fail.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncRetriesCreatureDownloadViaDirectProxyWhenExternalMonsterRoutesFailAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using MonsterInvalidJsonFailureHandler handler = new();
        using HttpClient client = new(handler);
        DndBeyondSyncService service = new(
            client,
            new Uri("https://unit.test", UriKind.Absolute),
            directProxyHandlerFactory: () => new DndBeyondDirectProxyHandler(new DirectProxyMonsterSuccessHandler()));
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: 42);

        DndBeyondSyncSummary summary = await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.True(summary.Creatures > 0);
        Assert.True(summary.Items > 0);
        Assert.True(summary.Spells > 0);
        Assert.True(summary.Players > 0);
        Assert.False(handler.PrimaryMonsterRouteCalled);
        Assert.False(handler.FallbackMonsterRouteCalled);
    }

    /// <summary>
    /// Verifies that synchronization logs each saved entity.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task SyncAsyncLogsEachSavedEntityAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        using StubHandler handler = new();
        using HttpClient client = new(handler);
        CapturingLogger<DndBeyondSyncService> logger = new();
        DndBeyondSyncService service = new(client, new Uri("https://unit.test", UriKind.Absolute), logger: logger);
        DndBeyondSyncOptions options = new(
            CobaltToken: "token",
            OutputBaseDirectory: workspace.RootPath,
            IncludeHomebrew: false,
            CampaignId: null,
            ItemNames: ["Long*"]);

        await service.SyncAsync(options, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains(logger.Messages, static message => message.Contains("Saved D&D Beyond item:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that JSON wildcard input upgrades matching files to markdown.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    [Fact]
    public async Task UpgradeAsyncAcceptsJsonWildcardAsync()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string first = Path.Combine(workspace.RootPath, "first.json");
        string second = Path.Combine(workspace.RootPath, "second.json");
        string ignored = Path.Combine(workspace.RootPath, "ignored.txt");
        await File.WriteAllTextAsync(first, """{"name":"First","content":"# First"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(second, """{"name":"Second","content":"# Second"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(ignored, """{"name":"Ignored","content":"# Ignored"}""").ConfigureAwait(true);

        JsonMarkdownUpgradeSummary summary = await new JsonMarkdownUpgrader()
            .UpgradeAsync(Path.Combine(workspace.RootPath, "*.json"), CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(2, summary.ConvertedFiles);
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "first.md")));
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "second.md")));
        Assert.True(File.Exists(ignored));
    }

    /// <summary>
    /// Provides successful proxy responses for sync test scenarios.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>
        /// Handles outgoing HTTP requests by returning deterministic proxy payloads.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;
            Assert.Contains("token", body, StringComparison.Ordinal);
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[{"id":1,"name":"Longsword","content":"# Longsword"},{"id":4,"name":"Lantern","isHomebrew":true}],"spells":[],"extra":[]}}""",
                "/proxy/class/spells" => """{"success":true,"data":[{"definition":{"id":2,"name":"Fireball","content":"# Fireball","isHomebrew":false}}]}""",
                "/proxy/monster" => """{"success":true,"data":[{"id":3,"name":"Goblin","content":"# Goblin"},{"id":5,"name":"Custom Creature","isHomebrew":true}]}""",
                "/proxy/party/42/characters" => """{"success":true,"data":{"characters":[{"characterId":99,"characterName":"Rogue"}]}}""",
                "/proxy/v5/character" => """{"success":true,"data":{"ddb":{"character":{"id":99,"name":"Rogue"}},"content":"# Rogue"}}""",
                _ => """{"success":true,"data":[]}""",
            };

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Returns a forbidden challenge page to simulate CAPTCHA blocking.
    /// </summary>
    private sealed class ChallengeHandler : HttpMessageHandler
    {
        /// <summary>
        /// Handles outgoing HTTP requests by returning a challenge response.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<html>captcha</html>", Encoding.UTF8, "text/html"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Captures direct-proxy upstream requests and returns canned upstream responses.
    /// </summary>
    private sealed class DirectProxyUpstreamHandler : HttpMessageHandler
    {
        /// <summary>
        /// Gets or sets a <see cref="List{T}"/> representing captured upstream request URIs.
        /// </summary>
        private readonly List<string> _requests = [];

        /// <summary>
        /// Gets a <see cref="IReadOnlyList{T}"/> representing captured upstream request URIs.
        /// </summary>
        public IReadOnlyList<string> Requests => _requests;

        /// <summary>
        /// Handles outgoing direct-proxy requests and records requested URIs.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string uri = request.RequestUri?.ToString() ?? string.Empty;
            _requests.Add(uri);
            string json;
            if (string.Equals(uri, "https://auth-service.dndbeyond.com/v1/cobalt-token", StringComparison.Ordinal))
            {
                Assert.Contains("CobaltSession=token", request.Headers.GetValues("Cookie"), StringComparer.Ordinal);
                json = """{"token":"bearer-token"}""";
            }
            else if (string.Equals(uri, "https://character-service.dndbeyond.com/character/v5/game-data/items?sharingSetting=2", StringComparison.Ordinal))
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("bearer-token", request.Headers.Authorization?.Parameter);
                json = """{"data":[{"id":1,"name":"Longsword","content":"# Longsword","sources":[]},{"id":2,"name":"Legacy Sword","sources":[{"sourceId":39}]}]}""";
            }
            else
            {
                json = """{"data":[]}""";
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Captures beta keys posted to proxy endpoints during synchronization.
    /// </summary>
    private sealed class PatreonKeyHandler : HttpMessageHandler
    {
        /// <summary>
        /// Gets or sets a <see cref="List{T}"/> representing captured beta keys.
        /// </summary>
        private readonly List<string> _betaKeys = [];

        /// <summary>
        /// Gets a <see cref="IReadOnlyList{T}"/> representing captured beta keys.
        /// </summary>
        public IReadOnlyList<string> BetaKeys => _betaKeys;

        /// <summary>
        /// Handles outgoing proxy requests and records any supplied beta key.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string requestBody = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            using JsonDocument document = JsonDocument.Parse(requestBody);
            if (document.RootElement.TryGetProperty("betaKey", out JsonElement betaKey))
            {
                _betaKeys.Add(betaKey.GetString() ?? string.Empty);
            }

            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[{"id":1,"name":"Longsword"}]}}""",
                _ => """{"success":true,"data":[]}""",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// Returns a Patreon-required monster response while leaving other categories available.
    /// </summary>
    private sealed class MonsterPatreonRequiredHandler : HttpMessageHandler
    {
        /// <summary>
        /// Handles outgoing HTTP requests by returning monster entitlement failure payloads.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[{"id":1,"name":"Longsword"}]}}""",
                "/proxy/class/spells" => """{"success":true,"data":[{"definition":{"id":2,"name":"Fireball","isHomebrew":false}}]}""",
                "/proxy/monster" => """{"success":false,"message":"Not an authorised user, please enter your Patreon key in the module settings."}""",
                "/proxy/party/42/characters" => """{"success":true,"data":{"characters":[{"characterId":99,"characterName":"Rogue"}]}}""",
                "/proxy/v5/character" => """{"success":true,"data":{"ddb":{"character":{"id":99,"name":"Rogue"}}}}""",
                _ => """{"success":true,"data":[]}""",
            };

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Simulates primary monster route failure followed by fallback route success.
    /// </summary>
    private sealed class MonsterFallbackRouteHandler : HttpMessageHandler
    {
        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether the fallback monster route was called.
        /// </summary>
        public bool FallbackRouteCalled { get; private set; }

        /// <summary>
        /// Handles outgoing HTTP requests and marks fallback route usage.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[{"id":1,"name":"Longsword"}]}}""",
                "/proxy/class/spells" => """{"success":true,"data":[{"definition":{"id":2,"name":"Fireball","isHomebrew":false}}]}""",
                "/proxy/monster" => """{"success":false,"message":"Unknown error during monster loading: FetchError: invalid json response body at https://monster-service.dndbeyond.com/v1/Monster reason: Unexpected token '<', \"<html>\" is not valid JSON"}""",
                "/proxy/monsters" => """{"success":true,"data":[{"id":3,"name":"Goblin"}]}""",
                "/proxy/party/42/characters" => """{"success":true,"data":{"characters":[{"characterId":99,"characterName":"Rogue"}]}}""",
                "/proxy/v5/character" => """{"success":true,"data":{"ddb":{"character":{"id":99,"name":"Rogue"}}}}""",
                _ => """{"success":true,"data":[]}""",
            };
            if (string.Equals(path, "/proxy/monsters", StringComparison.Ordinal))
            {
                FallbackRouteCalled = true;
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Simulates invalid JSON failures from both monster proxy routes.
    /// </summary>
    private sealed class MonsterInvalidJsonFailureHandler : HttpMessageHandler
    {
        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether the primary monster route was called.
        /// </summary>
        public bool PrimaryMonsterRouteCalled { get; private set; }

        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether the fallback monster route was called.
        /// </summary>
        public bool FallbackMonsterRouteCalled { get; private set; }

        /// <summary>
        /// Handles outgoing HTTP requests and tracks monster route attempts.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            string json = path switch
            {
                "/proxy/auth" => """{"success":true,"message":"ok"}""",
                "/proxy/items" => """{"success":true,"data":{"items":[{"id":1,"name":"Longsword"}]}}""",
                "/proxy/class/spells" => """{"success":true,"data":[{"definition":{"id":2,"name":"Fireball","isHomebrew":false}}]}""",
                "/proxy/monster" => """{"success":false,"message":"Unknown error during monster loading: FetchError: invalid json response body at https://monster-service.dndbeyond.com/v1/Monster reason: Unexpected token '<', \"<html>\" is not valid JSON"}""",
                "/proxy/monsters" => """{"success":false,"message":"Unknown error during monster loading: FetchError: invalid json response body at https://monster-service.dndbeyond.com/v1/Monster reason: Unexpected token '<', \"<html>\" is not valid JSON"}""",
                "/proxy/party/42/characters" => """{"success":true,"data":{"characters":[{"characterId":99,"characterName":"Rogue"}]}}""",
                "/proxy/v5/character" => """{"success":true,"data":{"ddb":{"character":{"id":99,"name":"Rogue"}}}}""",
                _ => """{"success":true,"data":[]}""",
            };
            if (string.Equals(path, "/proxy/monster", StringComparison.Ordinal))
            {
                PrimaryMonsterRouteCalled = true;
            }
            else if (string.Equals(path, "/proxy/monsters", StringComparison.Ordinal))
            {
                FallbackMonsterRouteCalled = true;
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Returns successful direct-proxy monster payloads for fallback synchronization.
    /// </summary>
    private sealed class DirectProxyMonsterSuccessHandler : HttpMessageHandler
    {
        /// <summary>
        /// Handles outgoing direct-proxy requests by returning deterministic payloads.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation and yields an HTTP response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string uri = request.RequestUri?.ToString() ?? string.Empty;
            string json = uri switch
            {
                "https://auth-service.dndbeyond.com/v1/cobalt-token" => """{"token":"bearer-token"}""",
                _ when uri.StartsWith("https://monster-service.dndbeyond.com/v1/Monster?", StringComparison.Ordinal) =>
                    """{"pagination":{"total":1},"data":[{"id":3,"name":"Goblin","isReleased":true}]}""",
                _ => """{"data":[]}""",
            };

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Provides an isolated temporary directory for synchronization test artifacts.
    /// </summary>
    private sealed class TestWorkspace : IDisposable
    {
        /// <summary>
        /// Gets or sets a <see cref="string"/> representing the workspace root path backing field.
        /// </summary>
        private readonly string _rootPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestWorkspace"/> class.
        /// </summary>
        /// <param name="rootPath">The workspace root path.</param>
        private TestWorkspace(string rootPath)
        {
            _rootPath = rootPath;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the workspace root path.
        /// </summary>
        public string RootPath => _rootPath;

        /// <summary>
        /// Creates a new temporary workspace instance.
        /// </summary>
        /// <returns>A workspace rooted at a unique temporary path.</returns>
        public static TestWorkspace Create()
        {
            string path = Path.Combine(Path.GetTempPath(), $"grimoire-dndb-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TestWorkspace(path);
        }

        /// <summary>
        /// Deletes the workspace directory and all generated files.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }

    /// <summary>
    /// Captures informational log messages emitted by synchronization operations.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        /// <summary>
        /// Gets a <see cref="List{T}"/> representing captured log messages.
        /// </summary>
        public List<string> Messages { get; } = [];

        /// <summary>
        /// Begins a no-op logging scope.
        /// </summary>
        /// <typeparam name="TState">The scope state type.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>A disposable no-op scope.</returns>
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoopScope.Instance;
        }

        /// <summary>
        /// Determines whether logging is enabled for the supplied level.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns><see langword="true"/> when informational logging is enabled; otherwise, <see langword="false"/>.</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        /// <summary>
        /// Writes a formatted log message when the log level is enabled.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="logLevel">The log level.</param>
        /// <param name="eventId">The event identifier.</param>
        /// <param name="state">The state payload.</param>
        /// <param name="exception">The associated exception.</param>
        /// <param name="formatter">The state formatter.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (!string.IsNullOrWhiteSpace(message))
            {
                Messages.Add(message);
            }
        }

        /// <summary>
        /// Represents a reusable no-op logging scope.
        /// </summary>
        private sealed class NoopScope : IDisposable
        {
            /// <summary>
            /// Gets a <see cref="NoopScope"/> representing the singleton scope instance.
            /// </summary>
            public static NoopScope Instance { get; } = new();

            /// <summary>
            /// Disposes the scope instance.
            /// </summary>
            public void Dispose()
            {
            }
        }
    }
}
