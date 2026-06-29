using System.Text.Json;
using Grimoire.Cli;
using Grimoire.Core;

namespace Grimoire.Ui.Tests;

/// <summary>
/// Represents integration-style tests for MCP server behavior exposed through the CLI service surface.
/// </summary>
public sealed class GrimoireCliMcpServerTests
{
    /// <summary>
    /// Verifies that initialize and tools/list responses expose base path and registered tool names and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task InitializeIncludesBasePathAndToolsListIncludesProjectSearchToolAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-ui-cli-mcp-list");
        string projectRoot = workspace.CreateProjectRoot();
        IReadOnlyList<JsonDocument> responses = await RunMcpAsync(
            projectRoot,
            """{"jsonrpc":"2.0","id":1,"method":"initialize"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""").ConfigureAwait(true);

        JsonDocument initializeResponse = Assert.Single(responses, static document =>
            document.RootElement.TryGetProperty("id", out JsonElement idElement) &&
            idElement.ValueKind == JsonValueKind.Number &&
            idElement.GetInt32() == 1);
        Assert.Equal(projectRoot, initializeResponse.RootElement.GetProperty("result").GetProperty("basePath").GetString());

        JsonDocument toolsResponse = Assert.Single(responses, static document =>
            document.RootElement.TryGetProperty("id", out JsonElement idElement) &&
            idElement.ValueKind == JsonValueKind.Number &&
            idElement.GetInt32() == 2);
        JsonElement tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
        string[] names =
        [
            .. tools.EnumerateArray()
            .Select(static item => item.GetProperty("name").GetString() ?? string.Empty),
        ];
        Assert.Contains("lore.search", names);
        Assert.Contains("project.search", names);
    }

    /// <summary>
    /// Verifies that keyword-usage and cross-reference modes return expected project-search results and returns a <see cref="Task"/> representing asynchronous test execution.
    /// </summary>
    [Fact]
    public async Task ProjectSearchToolSupportsKeywordUsageAndCrossReferenceModesAsync()
    {
        using TempWorkspace workspace = TempWorkspace.Create("grimoire-ui-cli-mcp-search");
        string projectRoot = workspace.CreateProjectRoot();
        Directory.CreateDirectory(Path.Combine(projectRoot, "content"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "spells", "cure.json"),
            """{"name":"Cure Wounds","description":"Healing spell"}""").ConfigureAwait(true);
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, "content", "001.md"),
            """
            ---
            title: Chapter One
            ---
            Cure Wounds keeps the party alive.
            ![Cure](../spells/cure.json)
            """).ConfigureAwait(true);

        IReadOnlyList<JsonDocument> responses = await RunMcpAsync(
            projectRoot,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"project.search","arguments":{"mode":"keyword-usage","query":"cure","limit":25}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"project.search","arguments":{"mode":"cross-reference","limit":25}}}""",
            """{"jsonrpc":"2.0","id":3,"method":"shutdown"}""").ConfigureAwait(true);

        JsonDocument keywordResponse = Assert.Single(responses, static document =>
            document.RootElement.TryGetProperty("id", out JsonElement idElement) &&
            idElement.ValueKind == JsonValueKind.Number &&
            idElement.GetInt32() == 1);
        string keywordText = keywordResponse.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "[]";
        using JsonDocument keywordMatches = JsonDocument.Parse(keywordText);
        Assert.Contains(
            keywordMatches.RootElement.EnumerateArray(),
            static item => string.Equals(item.GetProperty("entityName").GetString(), "Cure Wounds", StringComparison.Ordinal) &&
                    string.Equals(item.GetProperty("entityPath").GetString(), "spells/cure.json", StringComparison.Ordinal));

        JsonDocument crossReferenceResponse = Assert.Single(responses, static document =>
            document.RootElement.TryGetProperty("id", out JsonElement idElement) &&
            idElement.ValueKind == JsonValueKind.Number &&
            idElement.GetInt32() == 2);
        string crossReferenceText = crossReferenceResponse.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "[]";
        using JsonDocument crossReferenceMatches = JsonDocument.Parse(crossReferenceText);
        Assert.Contains(
            crossReferenceMatches.RootElement.EnumerateArray(),
            item => string.Equals(item.GetProperty("matchKind").GetString(), "include", StringComparison.Ordinal) &&
                    string.Equals(item.GetProperty("targetPath").GetString(), "spells/cure.json", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs the MCP server against a scripted request sequence and returns a <see cref="Task{TResult}"/> representing parsed JSON-RPC responses.
    /// </summary>
    /// <param name="projectRoot">The project root path representing the lore workspace for the MCP server.</param>
    /// <param name="requestLines">The request lines representing inbound JSON-RPC payloads.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing an <see cref="IReadOnlyList{T}"/> of parsed response documents.</returns>
    private static async Task<IReadOnlyList<JsonDocument>> RunMcpAsync(string projectRoot, params string[] requestLines)
    {
        string inputText = string.Join(Environment.NewLine, requestLines) + Environment.NewLine;
        using StringReader input = new(inputText);
        using StringWriter output = new();
        McpServer server = new(new LoreQueryEngine(projectRoot), input, output);
        await server.RunAsync(CancellationToken.None).ConfigureAwait(true);

        List<JsonDocument> responses = [];
        string[] lines = output.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            responses.Add(JsonDocument.Parse(line));
        }

        return responses;
    }

    /// <summary>
    /// Represents a disposable temporary workspace fixture used by MCP integration tests.
    /// </summary>
    private sealed class TempWorkspace : IDisposable
    {
        /// <summary>
        /// A <see cref="bool"/> indicating whether this fixture has already been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a temporary workspace fixture rooted at a specific path.
        /// </summary>
        /// <param name="rootPath">The root path representing the temporary workspace location.</param>
        private TempWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the root directory for the temporary workspace fixture.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Creates a temporary workspace fixture and returns a <see cref="TempWorkspace"/> representing the created workspace.
        /// </summary>
        /// <param name="name">The fixture name representing a path prefix for the temporary workspace.</param>
        /// <returns>A <see cref="TempWorkspace"/> representing the created temporary workspace.</returns>
        public static TempWorkspace Create(string name)
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TempWorkspace(rootPath);
        }

        /// <summary>
        /// Creates a project root under the fixture directory and returns a <see cref="string"/> representing the created project path.
        /// </summary>
        /// <returns>A <see cref="string"/> representing the created project root path.</returns>
        public string CreateProjectRoot()
        {
            string projectRoot = Path.Combine(RootPath, "project");
            Directory.CreateDirectory(projectRoot);
            return projectRoot;
        }

        /// <summary>
        /// Deletes the temporary workspace fixture directory and returns <see langword="void"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
