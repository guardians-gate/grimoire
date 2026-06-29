using Grimoire.Cli;
using Grimoire.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Text;
using System.Text.Json;

// ReSharper disable once CheckNamespace
/// <summary>
/// Represents top-level CLI helper methods that construct command handlers and output formatting routines.
/// </summary>
partial class Program
{
    /// <summary>
    /// Builds the compile command with source compilation and JSON-to-Markdown upgrade behaviors and returns a <see cref="Command"/> representing the configured CLI subcommand.
    /// </summary>
    /// <param name="localizer">The localizer representing text resources for command descriptions and output.</param>
    /// <param name="verboseOption">The shared verbose option representing log-level selection for command execution.</param>
    /// <returns>A <see cref="Command"/> representing the configured <c>compile</c> subcommand.</returns>
    static Command BuildCompileCommand(IStringLocalizer localizer, Option<bool> verboseOption)
    {
        Option<bool> upgradeOption = new("--upgrade")
        {
            Description = Localize(localizer, "Cli:Options:Upgrade"),
        };
        upgradeOption.Aliases.Add("-u");

        Option<string?> outputOption = new("--output")
        {
            Description = Localize(localizer, "Cli:Options:Output"),
        };
        outputOption.Aliases.Add("-o");

        Argument<string?> inputArgument = new("input")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = Localize(localizer, "Cli:Argument:Input"),
        };

        Command command = new("compile", Localize(localizer, "Cli:Commands:Compile"));
        command.Options.Add(outputOption);
        command.Options.Add(upgradeOption);
        command.Arguments.Add(inputArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            bool verbose = parseResult.GetValue(verboseOption);
            bool upgrade = parseResult.GetValue(upgradeOption);
            string? output = parseResult.GetValue(outputOption);
            string? input = parseResult.GetValue(inputArgument);

            if (upgrade)
            {
                if (string.IsNullOrWhiteSpace(output))
                {
                    await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:UpgradeRequiresOutput")).ConfigureAwait(false);
                    return 2;
                }

                try
                {
                    JsonMarkdownUpgradeSummary summary = await new JsonMarkdownUpgrader(localizer).UpgradeAsync(output, cancellationToken).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync(Localize(localizer, "Cli:Messages:Upgraded", summary.ConvertedFiles)).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or UnauthorizedAccessException)
                {
                    await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:General", ex.Message)).ConfigureAwait(false);
                    return 1;
                }
            }

            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(input))
            {
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Usage:CompileMain")).ConfigureAwait(false);
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Usage:CompileUpgrade")).ConfigureAwait(false);
                return 2;
            }

            try
            {
                using ILoggerFactory loggerFactory = CreateLoggerFactory(verbose);
                InputInspector inputInspector = new(loggerFactory.CreateLogger<InputInspector>());
                OutputInspector outputInspector = new(loggerFactory.CreateLogger<OutputInspector>());
                CompilationPlanner planner = new(inputInspector, outputInspector, loggerFactory.CreateLogger<CompilationPlanner>());
                CompilationRequest request = planner.Plan(input, output);
                ILogger<SourcebookCompiler> compilerLogger = loggerFactory.CreateLogger<SourcebookCompiler>();
                SourcebookCompiler compiler = new(compilerLogger);
                await compiler.CompileAsync(request, cancellationToken).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(Localize(localizer, "Cli:Messages:Compiled", request.SourceKind, request.Target, request.OutputPath)).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:General", ex.Message)).ConfigureAwait(false);
                return 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Builds the project scaffold command and returns a <see cref="Command"/> representing the configured CLI subcommand.
    /// </summary>
    /// <param name="localizer">The localizer representing text resources for command descriptions and output.</param>
    /// <param name="verboseOption">The shared verbose option representing log-level selection for command execution.</param>
    /// <returns>A <see cref="Command"/> representing the configured <c>new</c> subcommand.</returns>
    static Command BuildNewCommand(IStringLocalizer localizer, Option<bool> verboseOption)
    {
        Option<bool> forceOption = new("--force")
        {
            Description = Localize(localizer, "Cli:Options:Force"),
        };
        forceOption.Aliases.Add("-f");

        Option<string?> outputOption = new("--output")
        {
            Description = Localize(localizer, "Cli:Options:Output"),
        };
        outputOption.Aliases.Add("-o");

        Argument<string?> inputArgument = new("input")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = Localize(localizer, "Cli:Argument:Input"),
        };

        Command command = new("new", Localize(localizer, "Cli:Commands:New"));
        command.Options.Add(forceOption);
        command.Options.Add(outputOption);
        command.Arguments.Add(inputArgument);

        command.SetAction(async (parseResult, _) =>
        {
            bool verbose = parseResult.GetValue(verboseOption);
            bool force = parseResult.GetValue(forceOption);
            string output = parseResult.GetValue(outputOption) ?? ".";
            string? input = parseResult.GetValue(inputArgument);
            bool outputSpecified = parseResult.GetResult(outputOption) is not null;
            string targetPath = outputSpecified || string.IsNullOrWhiteSpace(input) ? output : input;
            using ILoggerFactory loggerFactory = CreateLoggerFactory(verbose);
            ProjectScaffolder.Scaffold(targetPath, overwriteExisting: force, loggerFactory.CreateLogger<ProjectScaffolder>());
            await Console.Out.WriteLineAsync(Localize(localizer, "Cli:Messages:Scaffolded", Path.GetFullPath(targetPath))).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    /// <summary>
    /// Builds the MCP server command and returns a <see cref="Command"/> representing the configured CLI subcommand.
    /// </summary>
    /// <param name="localizer">The localizer representing text resources for command descriptions and output.</param>
    /// <param name="verboseOption">The shared verbose option representing log-level selection for command execution.</param>
    /// <returns>A <see cref="Command"/> representing the configured <c>mcp</c> subcommand.</returns>
    static Command BuildMcpCommand(IStringLocalizer localizer, Option<bool> verboseOption)
    {
        Argument<string?> inputArgument = new("input")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = Localize(localizer, "Cli:Argument:Input"),
        };

        Command command = new("mcp", Localize(localizer, "Cli:Commands:Mcp"));
        command.Arguments.Add(inputArgument);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            bool verbose = parseResult.GetValue(verboseOption);
            string? input = parseResult.GetValue(inputArgument);
            string projectPath = !string.IsNullOrWhiteSpace(input)
                ? input
                : Directory.GetCurrentDirectory();

            try
            {
                using ILoggerFactory loggerFactory = CreateLoggerFactory(verbose);
                LoreQueryEngine loreQueryEngine = new(projectPath, loggerFactory.CreateLogger<LoreQueryEngine>());
                McpServer server = new(
                    loreQueryEngine,
                    Console.In,
                    Console.Out,
                    loggerFactory.CreateLogger<McpServer>());
                await server.RunAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:General", ex.Message)).ConfigureAwait(false);
                return 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Builds the D&amp;D Beyond synchronization command and returns a <see cref="Command"/> representing the configured CLI subcommand.
    /// </summary>
    /// <param name="localizer">The localizer representing text resources for command descriptions and output.</param>
    /// <param name="verboseOption">The shared verbose option representing log-level selection for command execution.</param>
    /// <returns>A <see cref="Command"/> representing the configured <c>dnd-beyond</c> subcommand.</returns>
    static Command BuildDndBeyondCommand(IStringLocalizer localizer, Option<bool> verboseOption)
    {
        Option<string?> cobaltOption = new("--cobalt")
        {
            Description = Localize(localizer, "Cli:Options:Cobalt"),
        };
        cobaltOption.Aliases.Add("-k");

        Option<string?> patreonKeyOption = new("--patreon-key")
        {
            Description = Localize(localizer, "Cli:Options:PatreonKey"),
        };
        patreonKeyOption.Aliases.Add("-K");
        patreonKeyOption.Aliases.Add("--patreon");

        Option<bool> homebrewOption = new("--homebrew")
        {
            Description = Localize(localizer, "Cli:Options:Homebrew"),
        };
        homebrewOption.Aliases.Add("-H");

        Option<int?> campaignOption = new("--campaign")
        {
            Description = Localize(localizer, "Cli:Options:Campaign"),
        };
        campaignOption.Aliases.Add("-C");

        Option<string[]> itemOption = new("--item")
        {
            Description = Localize(localizer, "Cli:Options:Item"),
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        itemOption.Aliases.Add("-I");

        Option<string[]> creatureOption = new("--creature")
        {
            Description = Localize(localizer, "Cli:Options:Creature"),
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        creatureOption.Aliases.Add("-M");
        creatureOption.Aliases.Add("--monster");

        Option<string[]> spellOption = new("--spell")
        {
            Description = Localize(localizer, "Cli:Options:Spell"),
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        spellOption.Aliases.Add("-S");

        Option<string[]> characterSheetOption = new("--character-sheet")
        {
            Description = Localize(localizer, "Cli:Options:CharacterSheet"),
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        characterSheetOption.Aliases.Add("-P");
        characterSheetOption.Aliases.Add("--player");

        Option<bool> upgradeOption = new("--upgrade")
        {
            Description = Localize(localizer, "Cli:Options:Upgrade"),
        };
        upgradeOption.Aliases.Add("-u");

        Option<bool> selfProxyOption = new("--self-proxy")
        {
            Description = Localize(localizer, "Cli:Options:SelfProxy"),
        };
        selfProxyOption.Aliases.Add("-s");

        Option<string?> outputOption = new("--output")
        {
            Description = Localize(localizer, "Cli:Options:Output"),
        };
        outputOption.Aliases.Add("-o");

        Command command = new("dnd-beyond", Localize(localizer, "Cli:Commands:DndBeyond"));
        command.Aliases.Add("dndb");
        command.Aliases.Add("ddb");
        command.Options.Add(campaignOption);
        command.Options.Add(homebrewOption);
        command.Options.Add(itemOption);
        command.Options.Add(cobaltOption);
        command.Options.Add(patreonKeyOption);
        command.Options.Add(creatureOption);
        command.Options.Add(outputOption);
        command.Options.Add(characterSheetOption);
        command.Options.Add(selfProxyOption);
        command.Options.Add(spellOption);
        command.Options.Add(upgradeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            bool verbose = parseResult.GetValue(verboseOption);
            string? cobalt = parseResult.GetValue(cobaltOption);
            string? patreonKey = parseResult.GetValue(patreonKeyOption);
            bool homebrew = parseResult.GetValue(homebrewOption);
            int? campaignId = parseResult.GetValue(campaignOption);
            string[] itemNames = parseResult.GetValue(itemOption) ?? [];
            string[] creatureNames = parseResult.GetValue(creatureOption) ?? [];
            string[] spellNames = parseResult.GetValue(spellOption) ?? [];
            string[] characterSheetNames = parseResult.GetValue(characterSheetOption) ?? [];
            bool upgrade = parseResult.GetValue(upgradeOption);
            bool selfProxy = parseResult.GetValue(selfProxyOption);
            string? output = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(output))
            {
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:DndbRequiresOutput")).ConfigureAwait(false);
                return 2;
            }

            string token = !string.IsNullOrWhiteSpace(cobalt)
                ? cobalt
                : Environment.GetEnvironmentVariable("DND_BEYOND_COBALT") ?? string.Empty;
            string resolvedPatreonKey = !string.IsNullOrWhiteSpace(patreonKey)
                ? patreonKey
                : Environment.GetEnvironmentVariable("MRPRIMATE_PATREON")
                  ?? Environment.GetEnvironmentVariable("DND_BEYOND_PATREON_KEY")
                  ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:MissingCobalt")).ConfigureAwait(false);
                return 2;
            }

            try
            {
                using HttpClient httpClient = CreateDndbHttpClient(selfProxy);
                Uri? proxyBaseUri = selfProxy ? ResolveSelfProxyUri(localizer) : null;
                using ILoggerFactory loggerFactory = CreateLoggerFactory(verbose);
                ILogger<DndBeyondSyncService> syncLogger = loggerFactory.CreateLogger<DndBeyondSyncService>();
                DndBeyondSyncService syncService = new(httpClient, proxyBaseUri, logger: syncLogger);
                DndBeyondSyncOptions syncOptions = new(
                    CobaltToken: token,
                    OutputBaseDirectory: output,
                    IncludeHomebrew: homebrew,
                    CampaignId: campaignId,
                    ItemNames: itemNames,
                    CreatureNames: creatureNames,
                    SpellNames: spellNames,
                    CharacterSheetNames: characterSheetNames,
                    UpgradeToMarkdown: upgrade,
                    PatreonKey: resolvedPatreonKey);

                DndBeyondSyncSummary summary = await syncService.SyncAsync(syncOptions, cancellationToken).ConfigureAwait(false);
                string upgradeSummary = summary.UpgradedMarkdownFiles > 0
                    ? Localize(localizer, "Cli:Messages:UpgradeSuffix", summary.UpgradedMarkdownFiles)
                    : string.Empty;
                await Console.Out.WriteLineAsync(
                        Localize(localizer, "Cli:Messages:DndbSynced", summary.SourceCount, summary.Items, summary.Spells, summary.Creatures, summary.Players, upgradeSummary))
                    .ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or HttpRequestException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:General", ex.Message)).ConfigureAwait(false);
                return 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Builds the project search command and returns a <see cref="Command"/> representing the configured CLI subcommand.
    /// </summary>
    /// <param name="localizer">The localizer representing text resources for command descriptions and output.</param>
    /// <param name="verboseOption">The shared verbose option representing log-level selection for command execution.</param>
    /// <returns>A <see cref="Command"/> representing the configured <c>search</c> subcommand.</returns>
    static Command BuildSearchCommand(IStringLocalizer localizer, Option<bool> verboseOption)
    {
        Option<string?> inputOption = new("--input")
        {
            Description = Localize(localizer, "Cli:Options:SearchInput"),
        };
        inputOption.Aliases.Add("-i");

        Option<string[]> pathOption = new("--path")
        {
            Description = Localize(localizer, "Cli:Options:SearchPath"),
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        pathOption.Aliases.Add("-p");

        Option<string?> modeOption = new("--mode")
        {
            Description = Localize(localizer, "Cli:Options:SearchMode"),
        };
        modeOption.Aliases.Add("-m");

        Option<string?> propertyOption = new("--property")
        {
            Description = Localize(localizer, "Cli:Options:SearchProperty"),
        };
        propertyOption.Aliases.Add("-P");

        Option<int> limitOption = new("--limit")
        {
            Description = Localize(localizer, "Cli:Options:SearchLimit"),
        };
        limitOption.Aliases.Add("-n");
        limitOption.Aliases.Add("--count");

        Option<bool> colorOption = new("--color")
        {
            Description = Localize(localizer, "Cli:Options:SearchColor"),
        };
        colorOption.Aliases.Add("-G");

        Argument<string[]> queryArgument = new("query")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = Localize(localizer, "Cli:Options:SearchQuery"),
        };

        Command command = new("search", Localize(localizer, "Cli:Commands:Search"));
        command.Options.Add(colorOption);
        command.Options.Add(inputOption);
        command.Options.Add(limitOption);
        command.Options.Add(modeOption);
        command.Options.Add(pathOption);
        command.Options.Add(propertyOption);
        command.Arguments.Add(queryArgument);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            bool verbose = parseResult.GetValue(verboseOption);
            bool forceColor = parseResult.GetValue(colorOption);
            bool colorize = IsSearchColorEnabled(forceColor);
            string input = parseResult.GetValue(inputOption) ?? ".";
            string[] pathFilters = parseResult.GetValue(pathOption) ?? [];
            string[] queryTokens = parseResult.GetValue(queryArgument) ?? [];
            string? query = queryTokens.Length == 0 ? null : string.Join(' ', queryTokens);
            string? propertyPath = parseResult.GetValue(propertyOption);
            bool modeSpecified = parseResult.GetResult(modeOption) is not null;
            string modeText = modeSpecified
                ? (parseResult.GetValue(modeOption) ?? "catalog").Trim()
                : !string.IsNullOrWhiteSpace(propertyPath)
                    ? "property"
                    : !string.IsNullOrWhiteSpace(query)
                        ? "full-text"
                        : "catalog";
            int limit = parseResult.GetValue(limitOption);
            limit = limit <= 0 ? 200 : limit;
            try
            {
                using ILoggerFactory loggerFactory = CreateLoggerFactory(verbose);
                ILogger<ProjectSearchService> searchLogger = loggerFactory.CreateLogger<ProjectSearchService>();
                if (string.Equals(modeText, "catalog", StringComparison.OrdinalIgnoreCase))
                {
                    IReadOnlyList<ProjectSearchEntry> entries = await ProjectSearchService.SearchAsync(new(input, pathFilters, query), searchLogger, cancellationToken).ConfigureAwait(false);
                    if (entries.Count == 0)
                    {
                        await Console.Out.WriteLineAsync(Localize(localizer, "Cli:Messages:SearchNoResults", Path.GetFullPath(input))).ConfigureAwait(false);
                        return 0;
                    }

                    await Console.Out.WriteLineAsync(Localize(localizer, "Cli:Messages:SearchResults", entries.Count, Path.GetFullPath(input))).ConfigureAwait(false);
                    await Console.Out.WriteLineAsync(string.Empty).ConfigureAwait(false);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        await Console.Out.WriteLineAsync(FormatSearchEntry(entries[i], colorize)).ConfigureAwait(false);
                        if (i < entries.Count - 1)
                        {
                            await Console.Out.WriteLineAsync(string.Empty).ConfigureAwait(false);
                        }
                    }

                    return 0;
                }

                ProjectSearchMode mode = ParseSearchMode(modeText);
                IReadOnlyList<ProjectSearchMatch> matches = await ProjectSearchService.SearchAdvancedAsync(
                        new(input, mode, query, propertyPath, limit, pathFilters),
                        searchLogger,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (matches.Count == 0)
                {
                    await Console.Out.WriteLineAsync(Localize(localizer, "Cli:Messages:SearchNoResults", Path.GetFullPath(input))).ConfigureAwait(false);
                    return 0;
                }

                await Console.Out.WriteLineAsync(Localize(localizer, "Cli:Messages:SearchResults", matches.Count, Path.GetFullPath(input))).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(string.Empty).ConfigureAwait(false);
                for (int i = 0; i < matches.Count; i++)
                {
                    await Console.Out.WriteLineAsync(FormatSearchMatch(matches[i], colorize)).ConfigureAwait(false);
                    if (i < matches.Count - 1)
                    {
                        await Console.Out.WriteLineAsync(string.Empty).ConfigureAwait(false);
                    }
                }

                return 0;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                await Console.Error.WriteLineAsync(Localize(localizer, "Cli:Errors:General", ex.Message)).ConfigureAwait(false);
                return 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Formats a catalog entry for CLI output and returns a <see cref="string"/> representing a human-readable, optionally colorized display block.
    /// </summary>
    /// <param name="entry">The search entry representing catalog metadata to render.</param>
    /// <param name="colorize">The value indicating whether ANSI color should be applied.</param>
    /// <returns>A <see cref="string"/> representing formatted output for a catalog entry.</returns>
    static string FormatSearchEntry(ProjectSearchEntry entry, bool colorize)
    {
        StringBuilder builder = new();
        builder.Append(ColorizeLabel("Name", colorize)).Append(": ").AppendLine(ColorizeValue(entry.Name, "\u001b[1;37m", colorize));
        builder.Append(ColorizeLabel("URL", colorize)).Append(": ").AppendLine(ColorizeValue(entry.RelativePath, "\u001b[36m", colorize));
        if (entry.IncludedBy.Length > 0)
        {
            builder.Append(ColorizeLabel("IncludedBy", colorize))
                .Append(": ")
                .AppendLine(ColorizeValue(string.Join(", ", entry.IncludedBy), "\u001b[33m", colorize));
        }

        if (!string.IsNullOrWhiteSpace(entry.Details))
        {
            builder.AppendLine(entry.Details.TrimEnd());
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Formats an advanced search match for CLI output and returns a <see cref="string"/> representing a human-readable, optionally colorized display block.
    /// </summary>
    /// <param name="match">The project search match representing advanced query output.</param>
    /// <param name="colorize">The value indicating whether ANSI color should be applied.</param>
    /// <returns>A <see cref="string"/> representing formatted output for an advanced search match.</returns>
    static string FormatSearchMatch(ProjectSearchMatch match, bool colorize)
    {
        StringBuilder builder = new();
        string pathWithLine = match.LineNumber.HasValue
            ? $"{match.RelativePath}:{match.LineNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : match.RelativePath;
        builder.Append(ColorizeLabel("Path", colorize)).Append(": ").AppendLine(ColorizeValue(pathWithLine, "\u001b[36m", colorize));
        builder.Append(ColorizeLabel("Type", colorize)).Append(": ").AppendLine(ColorizeMatchKind(match.MatchKind, colorize));
        if (!string.IsNullOrWhiteSpace(match.PropertyPath))
        {
            builder.Append(ColorizeLabel("Property", colorize)).Append(": ").AppendLine(ColorizeValue(match.PropertyPath, "\u001b[35m", colorize));
        }

        if (!string.IsNullOrWhiteSpace(match.TargetPath))
        {
            builder.Append(ColorizeLabel("Target", colorize)).Append(": ").AppendLine(ColorizeValue(match.TargetPath, "\u001b[33m", colorize));
        }

        if (!string.IsNullOrWhiteSpace(match.EntityName))
        {
            builder.Append(ColorizeLabel("Entity", colorize)).Append(": ").Append(ColorizeValue(match.EntityName, "\u001b[1;37m", colorize));
            if (!string.IsNullOrWhiteSpace(match.EntityPath))
            {
                builder.Append(" (").Append(ColorizeValue(match.EntityPath, "\u001b[33m", colorize)).Append(')');
            }

            builder.AppendLine();
        }

        if (match.IncludedBy.Length > 0)
        {
            builder.Append(ColorizeLabel("IncludedBy", colorize))
                .Append(": ")
                .AppendLine(ColorizeValue(string.Join(", ", match.IncludedBy), "\u001b[33m", colorize));
        }

        builder.Append(ColorizeLabel("Match", colorize)).Append(": ").AppendLine(match.Snippet);
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Determines whether ANSI color output should be enabled and returns a <see cref="bool"/> indicating whether colorized search output is active.
    /// </summary>
    /// <param name="forceColor">The value indicating whether the user explicitly requested color output.</param>
    /// <returns><see langword="true"/> indicating color should be used; otherwise, <see langword="false"/>.</returns>
    static bool IsSearchColorEnabled(bool forceColor)
    {
        if (forceColor)
        {
            return true;
        }

        if (Console.IsOutputRedirected)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("CLICOLOR"), "0", StringComparison.Ordinal))
        {
            return false;
        }

        string? term = Environment.GetEnvironmentVariable("TERM");
        if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WT_SESSION")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANSICON")) ||
               string.Equals(Environment.GetEnvironmentVariable("ConEmuANSI"), "ON", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(term);
    }

    /// <summary>
    /// Applies label styling and returns a <see cref="string"/> representing a formatted output label.
    /// </summary>
    /// <param name="label">The label text representing a field name in rendered output.</param>
    /// <param name="colorize">The value indicating whether ANSI color should be applied.</param>
    /// <returns>A <see cref="string"/> representing the formatted label text.</returns>
    static string ColorizeLabel(string label, bool colorize)
    {
        return ColorizeValue(label, "\u001b[1;36m", colorize);
    }

    /// <summary>
    /// Applies match-kind styling and returns a <see cref="string"/> representing the formatted match-kind label.
    /// </summary>
    /// <param name="matchKind">The match-kind value representing the search result category.</param>
    /// <param name="colorize">The value indicating whether ANSI color should be applied.</param>
    /// <returns>A <see cref="string"/> representing the formatted match-kind value.</returns>
    static string ColorizeMatchKind(string matchKind, bool colorize)
    {
        string color = matchKind.Trim().ToUpperInvariant() switch
        {
            "FULLTEXT" or "FULL-TEXT" or "FULL_TEXT" => "\u001b[32m",
            "PROPERTY" => "\u001b[35m",
            "KEYWORDUSAGE" or "KEYWORD-USAGE" or "KEYWORD_USAGE" => "\u001b[33m",
            "INCLUDE" => "\u001b[34m",
            "MACRO" => "\u001b[95m",
            _ => "\u001b[37m",
        };

        return ColorizeValue(matchKind, color, colorize);
    }

    /// <summary>
    /// Applies ANSI color styling to text and returns a <see cref="string"/> representing either colorized or raw output.
    /// </summary>
    /// <param name="value">The text value representing output content to render.</param>
    /// <param name="ansiColor">The ANSI color sequence representing the desired text color.</param>
    /// <param name="colorize">The value indicating whether color output is enabled.</param>
    /// <returns>A <see cref="string"/> representing the rendered output value.</returns>
    static string ColorizeValue(string value, string ansiColor, bool colorize)
    {
        return colorize ? $"{ansiColor}{value}\u001b[0m" : value;
    }

    /// <summary>
    /// Parses search-mode text and returns a <see cref="ProjectSearchMode"/> indicating which advanced search strategy should be executed.
    /// </summary>
    /// <param name="raw">The raw mode string representing user-provided mode input.</param>
    /// <returns>A <see cref="ProjectSearchMode"/> representing the parsed search mode.</returns>
    static ProjectSearchMode ParseSearchMode(string raw)
    {
        return raw.Trim().ToUpperInvariant() switch
        {
            "FULL-TEXT" or "FULLTEXT" => ProjectSearchMode.FullText,
            "PROPERTY" or "PROPERTIES" => ProjectSearchMode.Property,
            "KEYWORD" or "KEYWORD-USAGE" or "USAGE" => ProjectSearchMode.KeywordUsage,
            "XREF" or "CROSS-REFERENCE" or "CROSS-REFERENCES" => ProjectSearchMode.CrossReference,
            "CATALOG" => throw new ArgumentOutOfRangeException(nameof(raw), "catalog mode is handled separately."),
            _ => throw new ArgumentOutOfRangeException(nameof(raw), $"Unsupported search mode '{raw}'. Use: catalog, full-text, property, keyword-usage, cross-reference."),
        };
    }

    /// <summary>
    /// Creates the HTTP client used for D&amp;D Beyond sync and returns an <see cref="HttpClient"/> representing either direct-proxy or default transport behavior.
    /// </summary>
    /// <param name="selfProxy">The value indicating whether the built-in direct proxy transport should be used.</param>
    /// <returns>An <see cref="HttpClient"/> representing the configured outbound transport.</returns>
    static HttpClient CreateDndbHttpClient(bool selfProxy)
    {
        return selfProxy && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DND_BEYOND_PROXY"))
#pragma warning disable CA2000
            ? new HttpClient(new DndBeyondDirectProxyHandler())
#pragma warning restore CA2000
            : new HttpClient();
    }

    /// <summary>
    /// Resolves the self-proxy base URI and returns a <see cref="Uri"/> representing the D&amp;D Beyond proxy endpoint.
    /// </summary>
    /// <param name="localizer">The localizer representing text resources for validation error messages.</param>
    /// <returns>A <see cref="Uri"/> representing the validated proxy base address.</returns>
    static Uri ResolveSelfProxyUri(IStringLocalizer localizer)
    {
        string value = Environment.GetEnvironmentVariable("DND_BEYOND_PROXY") ?? "https://grimoire.local";
        if (!Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException(Localize(localizer, "Cli:Errors:InvalidProxyUri", value));
        }

        return uri;
    }

    /// <summary>
    /// Resolves a localized text resource and returns a <see cref="string"/> representing the formatted message value.
    /// </summary>
    /// <param name="localizer">The localizer representing the translation source.</param>
    /// <param name="key">The localization key representing the message template.</param>
    /// <param name="arguments">The formatting arguments representing template substitution values.</param>
    /// <returns>A <see cref="string"/> representing the resolved localized message.</returns>
    static string Localize(IStringLocalizer localizer, string key, params object[] arguments)
    {
        return arguments.Length == 0 ? localizer[key].Value : localizer[key, arguments].Value;
    }

    /// <summary>
    /// Creates the CLI logger factory and returns an <see cref="ILoggerFactory"/> representing console logging configuration for the requested verbosity.
    /// </summary>
    /// <param name="verbose">The value indicating whether debug-level logging should be enabled.</param>
    /// <returns>An <see cref="ILoggerFactory"/> representing configured console logging services.</returns>
    static ILoggerFactory CreateLoggerFactory(bool verbose)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
            builder.AddSimpleConsole(static options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
                options.UseUtcTimestamp = false;
            });
        });
    }
}