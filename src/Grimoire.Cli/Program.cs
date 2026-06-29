using System.CommandLine;
using Grimoire.Core.Localization;
using Microsoft.Extensions.Localization;

IStringLocalizer localizer = new GrimoireLocalizationFactory().CreateDefault();
string rootDescription = string.Join(
    Environment.NewLine + Environment.NewLine,
    Localize(localizer, "Cli:RootDescription"),
    Localize(localizer, "Cli:CopyrightNotices"));

RootCommand root = new(rootDescription);
Option<bool> verboseOption = new("--verbose")
{
    Description = Localize(localizer, "Cli:Options:Verbose"),
};

verboseOption.Aliases.Add("-v");
root.Options.Add(verboseOption);
root.Subcommands.Add(BuildCompileCommand(localizer, verboseOption));
root.Subcommands.Add(BuildNewCommand(localizer, verboseOption));
root.Subcommands.Add(BuildMcpCommand(localizer, verboseOption));
root.Subcommands.Add(BuildDndBeyondCommand(localizer, verboseOption));
root.Subcommands.Add(BuildSearchCommand(localizer, verboseOption));

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);

/// <summary>
/// Represents the generated backing type for top-level CLI methods.
/// </summary>
internal static partial class Program;
