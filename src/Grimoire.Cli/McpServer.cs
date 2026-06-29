using System.Text.Json;
using Grimoire.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Cli;

/// <summary>
/// Represents a JSON-RPC loop that exposes Grimoire lore and project search capabilities through the MCP protocol.
/// </summary>
/// <param name="loreQueryEngine">The lore query engine representing project-backed lore search functionality.</param>
/// <param name="input">The text reader representing inbound JSON-RPC message input.</param>
/// <param name="output">The text writer representing outbound JSON-RPC response output.</param>
/// <param name="inputLogger">The optional logger representing diagnostic output for server operations.</param>
internal sealed partial class McpServer(
    LoreQueryEngine loreQueryEngine,
    TextReader input,
    TextWriter output,
    ILogger<McpServer>? inputLogger = null)
{
    /// <summary>
    /// A <see cref="string"/> representing the MCP protocol version advertised during initialization.
    /// </summary>
    private const string ProtocolVersion = "2025-06-18";

    /// <summary>
    /// A <see cref="JsonSerializerOptions"/> representing JSON serialization settings for request and response payloads.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// A <see cref="LoreQueryEngine"/> representing the project-backed lore search service used by tool calls.
    /// </summary>
    private readonly LoreQueryEngine _loreQueryEngine = loreQueryEngine ?? throw new ArgumentNullException(nameof(loreQueryEngine));

    /// <summary>
    /// A <see cref="TextReader"/> representing the stream used to read incoming JSON-RPC requests.
    /// </summary>
    private readonly TextReader _input = input ?? throw new ArgumentNullException(nameof(input));

    /// <summary>
    /// A <see cref="TextWriter"/> representing the stream used to write JSON-RPC responses.
    /// </summary>
    private readonly TextWriter _output = output ?? throw new ArgumentNullException(nameof(output));

    /// <summary>
    /// A <see cref="ILogger{TCategoryName}"/> representing diagnostics for MCP lifecycle and tool execution events.
    /// </summary>
    private readonly ILogger<McpServer> logger = inputLogger ?? NullLogger<McpServer>.Instance;

    /// <summary>
    /// Runs the MCP read-evaluate-write loop until shutdown is requested and returns a <see cref="Task"/> representing asynchronous server execution.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token indicating when server execution should stop.</param>
    /// <returns>A <see cref="Task"/> representing completion of the server loop.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        RunLoopStarted(_loreQueryEngine.ProjectRoot);
        while (await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var message = JsonDocument.Parse(line);
            if (!message.RootElement.TryGetProperty("method", out JsonElement methodElement))
            {
                continue;
            }

            string method = methodElement.GetString() ?? string.Empty;
            ReceivedMethod(method);
            object? id = null;
            if (message.RootElement.TryGetProperty("id", out JsonElement idElement))
            {
                id = JsonSerializer.Deserialize<object>(idElement.GetRawText(), JsonOptions);
            }

            object result;
            if (string.Equals(method, "initialize", StringComparison.Ordinal))
            {
                result = BuildInitializeResponse();
            }
            else if (string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                result = BuildToolsListResponse();
            }
            else if (string.Equals(method, "tools/call", StringComparison.Ordinal))
            {
                result = await BuildToolsCallResponseAsync(message.RootElement, cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(method, "shutdown", StringComparison.Ordinal))
            {
                result = new { };
            }
            else
            {
                result = new { };
            }

            if (id is not null)
            {
                string response = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, JsonOptions);
                await _output.WriteLineAsync(response).ConfigureAwait(false);
                await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(method, "shutdown", StringComparison.Ordinal))
            {
                ShutdownReceived();
                return;
            }
        }

        RunLoopEnded();
    }

    /// <summary>
    /// Builds the initialize response payload and returns an <see cref="object"/> representing the protocol handshake result.
    /// </summary>
    /// <returns>An <see cref="object"/> representing initialize response data for protocol version, capabilities, and server metadata.</returns>
    private object BuildInitializeResponse()
    {
        return new
        {
            protocolVersion = ProtocolVersion,
            basePath = _loreQueryEngine.ProjectRoot,
            serverInfo = new
            {
                name = "grimoire-mcp",
                version = "0.1.0",
            },
            capabilities = new
            {
                tools = new { },
            },
        };
    }

    /// <summary>
    /// Builds the tools/list response payload and returns an <see cref="object"/> representing the advertised MCP tools and schemas.
    /// </summary>
    /// <returns>An <see cref="object"/> representing tool metadata exposed by this server.</returns>
    private static object BuildToolsListResponse()
    {
        return new
        {
            tools = new object[]
            {
                new
                {
                    name = "lore.search",
                    description = "Search lore markdown/json content in a Grimoire project.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Search text." },
                            limit = new { type = "integer", minimum = 1, maximum = 50, description = "Result limit." },
                        },
                        required = new[] { "query" },
                    },
                },
                new
                {
                    name = "project.search",
                    description = "Search project entities with catalog, full-text, property, keyword-usage, and cross-reference modes.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "Search mode: catalog, full-text, property, keyword-usage, cross-reference." },
                            query = new { type = "string", description = "Search text (optional for catalog/property; required by full-text and keyword-usage)." },
                            propertyPath = new { type = "string", description = "Property path filter for property mode (e.g. definition.name)." },
                            paths = new
                            {
                                oneOf = new object[]
                                {
                                    new { type = "string", description = "Comma-separated top-level path filters." },
                                    new { type = "array", items = new { type = "string" }, description = "Top-level path filters." },
                                },
                            },
                            limit = new { type = "integer", minimum = 1, maximum = 5000, description = "Result limit." },
                        },
                    },
                },
            },
        };
    }

    /// <summary>
    /// Executes a tools/call request and returns a <see cref="Task{TResult}"/> representing an MCP content payload with call results or validation errors.
    /// </summary>
    /// <param name="request">The JSON element representing the incoming tools/call request object.</param>
    /// <param name="cancellationToken">The cancellation token indicating when tool execution should be aborted.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing an <see cref="object"/> payload with serialized tool results.</returns>
    private async Task<object> BuildToolsCallResponseAsync(JsonElement request, CancellationToken cancellationToken)
    {
        if (!request.TryGetProperty("params", out JsonElement parameters) ||
            !parameters.TryGetProperty("name", out JsonElement nameElement))
        {
            return new { content = new[] { new { type = "text", text = "Invalid tools/call payload." } } };
        }

        string toolName = nameElement.GetString() ?? string.Empty;
        ToolCallReceived(toolName);
        if (!parameters.TryGetProperty("arguments", out JsonElement arguments))
        {
            return new { content = new[] { new { type = "text", text = "Missing tool arguments." } } };
        }

        if (string.Equals(toolName, "lore.search", StringComparison.Ordinal))
        {
            if (!arguments.TryGetProperty("query", out JsonElement queryElement))
            {
                return new { content = new[] { new { type = "text", text = "Missing required argument 'query'." } } };
            }

            string query = queryElement.GetString() ?? string.Empty;
            int loreLimit = 8;
            if (arguments.TryGetProperty("limit", out JsonElement loreLimitElement) &&
                loreLimitElement.ValueKind == JsonValueKind.Number &&
                loreLimitElement.TryGetInt32(out int loreRequestedLimit))
            {
                loreLimit = Math.Clamp(loreRequestedLimit, 1, 50);
            }

            IReadOnlyList<LoreSearchResult> results = _loreQueryEngine.Search(query, loreLimit);
            string loreText = JsonSerializer.Serialize(results, JsonOptions);
            return new
            {
                content = new[] { new { type = "text", text = loreText } },
            };
        }

        if (!string.Equals(toolName, "project.search", StringComparison.Ordinal))
        {
            return new { content = new[] { new { type = "text", text = $"Unknown tool '{toolName}'." } } };
        }

        string mode = "catalog";
        if (arguments.TryGetProperty("mode", out JsonElement modeElement) && modeElement.ValueKind == JsonValueKind.String)
        {
            mode = modeElement.GetString() ?? "catalog";
        }

        string[] pathFilters = ParsePathFilters(arguments);
        int limit = 200;
        if (arguments.TryGetProperty("limit", out JsonElement limitElement) &&
            limitElement.ValueKind == JsonValueKind.Number &&
            limitElement.TryGetInt32(out int requestedLimit))
        {
            limit = Math.Clamp(requestedLimit, 1, 5000);
        }

        string? projectSearchQuery = arguments.TryGetProperty("query", out JsonElement searchQueryElement) && searchQueryElement.ValueKind == JsonValueKind.String
            ? searchQueryElement.GetString()
            : null;

        try
        {
            if (string.Equals(mode, "catalog", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(
                        new(_loreQueryEngine.ProjectRoot, pathFilters, projectSearchQuery),
                        cancellationToken)
                    .ConfigureAwait(false);
                string catalogText = JsonSerializer.Serialize(entries, JsonOptions);
                return new
                {
                    content = new[] { new { type = "text", text = catalogText } },
                };
            }

            ProjectSearchMode parsedMode = ParseProjectSearchMode(mode);
            string? propertyPath = arguments.TryGetProperty("propertyPath", out JsonElement propertyElement) && propertyElement.ValueKind == JsonValueKind.String
                ? propertyElement.GetString()
                : null;

            IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
                    new(_loreQueryEngine.ProjectRoot, parsedMode, projectSearchQuery, propertyPath, limit, pathFilters),
                    cancellationToken)
                .ConfigureAwait(false);
            string matchesText = JsonSerializer.Serialize(matches, JsonOptions);
            return new
            {
                content = new[] { new { type = "text", text = matchesText } },
            };
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or IOException or JsonException or UnauthorizedAccessException)
        {
            ToolCallFailed(toolName, ex.Message);
            return new { content = new[] { new { type = "text", text = ex.Message } } };
        }
    }

    /// <summary>
    /// Parses the user-specified search mode text and returns a <see cref="ProjectSearchMode"/> indicating which advanced search strategy to run.
    /// </summary>
    /// <param name="mode">The mode text representing a project-search mode alias.</param>
    /// <returns>A <see cref="ProjectSearchMode"/> representing the resolved search mode.</returns>
    private static ProjectSearchMode ParseProjectSearchMode(string mode)
    {
        return mode.Trim().ToUpperInvariant() switch
        {
            "FULL-TEXT" or "FULLTEXT" => ProjectSearchMode.FullText,
            "PROPERTY" or "PROPERTIES" => ProjectSearchMode.Property,
            "KEYWORD-USAGE" or "KEYWORD" or "USAGE" => ProjectSearchMode.KeywordUsage,
            "CROSS-REFERENCE" or "CROSS-REFERENCES" or "XREF" => ProjectSearchMode.CrossReference,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), $"Unsupported mode '{mode}'."),
        };
    }

    /// <summary>
    /// Parses path filters from a tools/call argument object and returns a <see cref="string"/> array indicating top-level search path filters.
    /// </summary>
    /// <param name="arguments">The JSON arguments object representing tool-call input.</param>
    /// <returns>A <see cref="string"/> array indicating normalized path filter values.</returns>
    private static string[] ParsePathFilters(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("paths", out JsonElement pathsElement))
        {
            return [];
        }

        if (pathsElement.ValueKind == JsonValueKind.String)
        {
            string raw = pathsElement.GetString() ?? string.Empty;
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (pathsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement element in pathsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string value = element.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return [.. values];
    }

    /// <summary>
    /// Logs that the MCP run loop has started for a project root.
    /// </summary>
    /// <param name="projectRoot">The project root path representing the active lore workspace.</param>
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug, Message = "MCP run loop started for project root {projectRoot}.")]
    private partial void RunLoopStarted(string projectRoot);

    /// <summary>
    /// Logs the JSON-RPC method received from an inbound message.
    /// </summary>
    /// <param name="method">The method name representing the requested JSON-RPC operation.</param>
    [LoggerMessage(EventId = 3001, Level = LogLevel.Debug, Message = "MCP received method {method}.")]
    private partial void ReceivedMethod(string method);

    /// <summary>
    /// Logs that an MCP tool invocation request has been received.
    /// </summary>
    /// <param name="toolName">The tool name representing the requested MCP tool.</param>
    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "MCP tool call received: {toolName}.")]
    private partial void ToolCallReceived(string toolName);

    /// <summary>
    /// Logs a tool invocation failure with the associated error message.
    /// </summary>
    /// <param name="toolName">The tool name representing the failed invocation target.</param>
    /// <param name="errorMessage">The error message representing the failure reason.</param>
    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "MCP tool call failed for {toolName}: {errorMessage}")]
    private partial void ToolCallFailed(string toolName, string errorMessage);

    /// <summary>
    /// Logs that an MCP shutdown request has been received.
    /// </summary>
    [LoggerMessage(EventId = 3004, Level = LogLevel.Debug, Message = "MCP shutdown requested.")]
    private partial void ShutdownReceived();

    /// <summary>
    /// Logs that the MCP run loop has terminated.
    /// </summary>
    [LoggerMessage(EventId = 3005, Level = LogLevel.Debug, Message = "MCP run loop ended.")]
    private partial void RunLoopEnded();
}
