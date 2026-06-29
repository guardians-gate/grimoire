using System.Reflection;
using System.Text.RegularExpressions;

namespace Grimoire.Core.Tests;


/// <summary>
/// Contains additional tests that verify sourcebook compilation and preview behavior.
/// </summary>
public sealed partial class SourcebookCompilerTests
{
    /// <summary>
    /// Verifies that attack type values are formatted and duplicate long-range rows are suppressed.
    /// </summary>
    [Fact]
    public async Task CompileAsyncFormatsAttackTypeAndSuppressesDuplicateLongRangeAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "items"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "TEMPLATE.md"),
            """
            # {{name}}

            | Property    | Value        |
            |-------------|--------------|
            | Attack Type | {{attackType}} |
            | Range       | {{range}}      |
            | Long Range  | {{longRange}}  |
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "longbow.json"),
            """{"name":"Longbow","attackType":2,"range":"150/600","longRange":"150/600"}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Arms
            ---
            ![Longbow](../items/longbow.json?inline)
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("Ranged", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Long Range", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that template fallback formatting resolves nested substitutions.
    /// </summary>
    [Fact]
    public async Task CompileAsyncTemplateFallbackFormatResolvesNestedSubstitutionsAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "items"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "TEMPLATE.md"),
            """
            # {{name}}
            {{description::-{{content}}}}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "ring.json"),
            """{"name":"Ring of Echoes","content":"Fallback description from content."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Treasure
            ---
            ![Ring](../items/ring.json?inline)
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("Fallback description from content.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{content}}", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies template override, disable, and missing-template fallback behavior across entity types.
    /// </summary>
    [Fact]
    public async Task CompileAsyncTemplatePropertySupportsOverrideDisableAndMissingTemplateFallbackAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "items"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "creatures"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "factions"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "TEMPLATE.md"),
            """
            ---
            enabled: false
            ---
            DISABLED TEMPLATE SHOULD NOT RENDER
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "custom-template.md"),
            """CUSTOM TEMPLATE {{name}}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "default-item.json"),
            """{"name":"Default Item","content":"Default item body."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "custom-item.json"),
            """{"name":"Custom Item","content":"Custom item body.","template":"./custom-template.md"}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "null-item.json"),
            """{"name":"Null Item","content":"Null item body.","template":null}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "empty-item.json"),
            """{"name":"Empty Item","content":"Empty item body.","template":""}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "tilde-item.json"),
            """{"name":"Tilde Item","content":"Tilde item body.","template":"~"}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "TEMPLATE.md"),
            """
            ---
            enabled: false
            ---
            CREATURE TEMPLATE SHOULD NOT RENDER
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "manual.md"),
            """
            ---
            template: ~
            ---
            Manual creature markdown body.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "creatures", "missing-template.json"),
            """{"name":"Missing Template Creature","content":"Missing template body."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "no-template-file.json"),
            """{"name":"No Template File Spell","content":"No template file body."}""");
        await File.WriteAllTextAsync(Path.Combine(inputDirectory, "factions", "TEMPLATE.md"), string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "factions", "empty-template-file.json"),
            """{"name":"Empty Template File Faction","content":"Empty template file body."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Template Overrides
            ---
            ![](../items/default-item.json?inline)
            ![](../items/custom-item.json?inline)
            ![](../items/null-item.json?inline)
            ![](../items/empty-item.json?inline)
            ![](../items/tilde-item.json?inline)
            ![](../creatures/manual.md?inline)
            ![](../creatures/missing-template.json?inline)
            ![](../spells/no-template-file.json?inline)
            ![](../factions/empty-template-file.json?inline)
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("Default item body.", html, StringComparison.Ordinal);
        Assert.Contains("CUSTOM TEMPLATE Custom Item", html, StringComparison.Ordinal);
        Assert.Contains("Null item body.", html, StringComparison.Ordinal);
        Assert.Contains("Empty item body.", html, StringComparison.Ordinal);
        Assert.Contains("Tilde item body.", html, StringComparison.Ordinal);
        Assert.Contains("Manual creature markdown body.", html, StringComparison.Ordinal);
        Assert.Contains("Missing template body.", html, StringComparison.Ordinal);
        Assert.Contains("No template file body.", html, StringComparison.Ordinal);
        Assert.Contains("Empty template file body.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DISABLED TEMPLATE SHOULD NOT RENDER", html, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATURE TEMPLATE SHOULD NOT RENDER", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that DDB character and spell definition names are preferred over filenames.
    /// </summary>
    [Fact]
    public async Task CompileAsyncUsesDdbCharacterNameAndSpellDefinitionNameOverFilenameAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "players"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "players", "12345-placeholder.json"),
            """{"ddb":{"character":{"name":"NEKTREOR MAUGHTHAR"}}}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "67890-placeholder.json"),
            """{"definition":{"name":"ARCANE LANCE"}}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Names
            ---
            ![](../players/12345-placeholder.json?inline)
            ![](../spells/67890-placeholder.json?inline)
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("Nektreor Maughthar", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">placeholder<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Arcane Lance", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies auto-linking and index behavior for included mentions, spell ordering, and feature deduplication.
    /// </summary>
    [Fact]
    public async Task CompileAsyncAutoLinksIncludedMentionsAddsIndexTargetsSortsSpellsAndDeduplicatesFeaturesAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "players"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              autoLink: true
              dictionary:
                unreferenced: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "players", "TEMPLATE.md"),
            """
            # {{name}}
            {{description}}

            ## Spells
            {{#each ddb.character.spells.class}}
            - {{definition.name}} ({{definition.level}})
            {{/each}}

            ## Features
            {{#each ddb.character.classes[0].classFeatures}}
            - {{definition.name}}
            {{/each}}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "players", "145475065-NEKTREOR-MAUGHTHAR.json"),
            """
            {
              "ddb": {
                "character": {
                  "name": "NEKTREOR MAUGHTHAR",
                  "spells": {
                    "class": [
                      { "definition": { "name": "Fireball", "level": 3 } },
                      { "definition": { "name": "Magic Missile", "level": 1 } },
                      { "definition": { "name": "Misty Step", "level": 2 } }
                    ]
                  },
                  "classes": [
                    {
                      "classFeatures": [
                        { "definition": { "name": "Creating Spell Slots", "description": "Create slots from sorcery points." } },
                        { "definition": { "name": "Creating Spell Slots", "description": "Create slots from sorcery points." } }
                      ]
                    }
                  ]
                }
              },
              "description": "Draconic Healing. You learn the cure wounds spell."
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "136566-CURE-WOUNDS.json"),
            """{"name":"Cure Wounds","description":"A creature regains hit points."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Character Chapter
            ---
            ![](../players/145475065-NEKTREOR-MAUGHTHAR.json?inline)
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));

        Assert.Contains("chapter-appendix-snippets.html#ref-136566-cure-wounds", chapterHtml, StringComparison.OrdinalIgnoreCase);
        int cureWoundsIndex = indexHtml.IndexOf("Cure Wounds", StringComparison.OrdinalIgnoreCase);
        Assert.True(cureWoundsIndex >= 0);
        string cureWoundsSegment = indexHtml[cureWoundsIndex..Math.Min(indexHtml.Length, cureWoundsIndex + 800)];
        Assert.Contains("chapter-001.html#001", cureWoundsSegment, StringComparison.Ordinal);

        int magicMissileIndex = chapterHtml.IndexOf("Magic Missile", StringComparison.Ordinal);
        int mistyStepIndex = chapterHtml.IndexOf("Misty Step", StringComparison.Ordinal);
        int fireballIndex = chapterHtml.IndexOf("Fireball", StringComparison.Ordinal);
        Assert.True(magicMissileIndex >= 0 && mistyStepIndex >= 0 && fireballIndex >= 0);
        Assert.True(magicMissileIndex < mistyStepIndex);
        Assert.True(mistyStepIndex < fireballIndex);

        int firstFeature = chapterHtml.IndexOf("Creating Spell Slots", StringComparison.Ordinal);
        Assert.True(firstFeature >= 0);
        Assert.Equal(firstFeature, chapterHtml.LastIndexOf("Creating Spell Slots", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that exclude-links front matter prevents auto-linking for matching keywords.
    /// </summary>
    [Fact]
    public async Task CompileAsyncExcludeLinksFrontMatterPreventsAutoLinkingMatchingKeywordsAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Exclude Links Chapter
            excludeLinks: "^Cure Wounds$"
            ---
            Cure Wounds and cure wounds and Fireball are all known spells.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "136566-CURE-WOUNDS.json"),
            """{"name":"Cure Wounds","description":"A creature regains hit points."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "2877-FIREBALL.json"),
            """{"name":"Fireball","description":"A bright streak flashes and explodes."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));
        string indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index-topics.html"));

        Assert.Contains("Cure Wounds", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"chapter-appendix-reference-dictionary.html#dict-ref-136566-cure-wounds\">Cure Wounds</a>", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"chapter-appendix-reference-dictionary.html#dict-ref-136566-cure-wounds\">cure wounds</a>", chapterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dict-ref-2877-fireball", chapterHtml, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Cure Wounds", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("Fireball", dictionaryHtml, StringComparison.Ordinal);

        Assert.Contains("dict-ref-136566-cure-wounds", indexHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dict-ref-2877-fireball", indexHtml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that heading auto-linking is disabled by default.
    /// </summary>
    [Fact]
    public async Task CompileAsyncHeadingLinksDefaultsFalseAndDoesNotAutoLinkHeadingsAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Heading Links Off
            ---
            ## Cure Wounds
            The party relies on cure wounds for recovery.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "136566-CURE-WOUNDS.json"),
            """{"name":"Cure Wounds","description":"A creature regains hit points."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("<h2 id=\"cure-wounds\">Cure Wounds</h2>", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2 id=\"cure-wounds\"><a href=", chapterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"chapter-appendix-reference-dictionary.html#dict-ref-136566-cure-wounds\">cure wounds</a>", chapterHtml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that heading auto-linking is enabled when explicitly requested.
    /// </summary>
    [Fact]
    public async Task CompileAsyncHeadingLinksTrueAllowsAutoLinksInHeadingsAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Heading Links On
            headingLinks: true
            ---
            ## Cure Wounds
            The party relies on cure wounds for recovery.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "136566-CURE-WOUNDS.json"),
            """{"name":"Cure Wounds","description":"A creature regains hit points."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("<h2 id=\"cure-wounds\"><span id=\"001-mention-cure-wounds\"></span><a href=\"chapter-appendix-reference-dictionary.html#dict-ref-136566-cure-wounds\">Cure Wounds</a></h2>", chapterHtml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that auto-linking prefers the longest matching keyword.
    /// </summary>
    [Fact]
    public async Task CompileAsyncAutoLinkingUsesGreedyLongestKeywordMatchAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "items"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Greedy Match Chapter
            ---
            Cade grabbed the staff of the deep and went crazy.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "100-STAFF.json"),
            """{"name":"Staff","description":"A simple magical staff."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "items", "200-STAFF-OF-THE-DEEP.json"),
            """{"name":"Staff of the Deep","description":"A dangerous relic."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("href=\"chapter-appendix-reference-dictionary.html#dict-ref-200-staff-of-the-deep\">staff of the deep</a>", chapterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"chapter-appendix-reference-dictionary.html#dict-ref-100-staff\">staff</a>", chapterHtml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that duplicate entity names resolve to the highest identifier during auto-linking.
    /// </summary>
    [Fact]
    public async Task CompileAsyncPrefersNewestDuplicateEntityByHighestIdForAutoLinksAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            compiler:
              dictionary:
                enabled: true
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Duplicate Name Chapter
            ---
            Sleep can be powerful.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "100-SLEEP-OLD.json"),
            """{"name":"Sleep","description":"Old sleep definition.","content":"Old sleep definition."}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "200-SLEEP-NEW.json"),
            """{"name":"Sleep","description":"Newest sleep definition.","content":"Newest sleep definition."}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        string dictionaryHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-appendix-reference-dictionary.html"));

        Assert.Contains("dict-ref-200-sleep-new", chapterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dict-ref-100-sleep-old", chapterHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"dict-ref-200-sleep-new\"", dictionaryHtml, StringComparison.Ordinal);
        Assert.Contains("Newest sleep definition.", dictionaryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"dict-ref-100-sleep-old\"", dictionaryHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that compile-time setting substitutions resolve and unknown tokens remain literal.
    /// </summary>
    [Fact]
    public async Task CompileAsyncSubstitutesAnyCompileTimeSettingTokenAndLeavesUnknownTokensLiteralAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "global.yml"),
            """
            custom:
              feature:
                flag: beta
            compiler:
              print:
                columns: 2
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "html.yml"),
            """
            custom:
              feature:
                flag: alpha
            compiler:
              print:
                columns: 4
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Compile Time Settings
            ---
            Flag={{custom.feature.flag}}
            Columns={{compiler.print.columns}}
            Unknown={{project.someObj.missing}}
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("Flag=alpha", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("Columns=4", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("Unknown={{project.someObj.missing}}", chapterHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that substitutions resolve from appsettings and environment configuration sources.
    /// </summary>
    [Fact]
    public async Task CompileAsyncSubstitutesAppSettingsAndEnvironmentValuesViaConfigurationAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "appsettings.json"),
            """
            {
              "runtime": {
                "build": {
                  "stamp": "from-appsettings"
                }
              }
            }
            """);

        string environmentPrefix = $"grimoire_subst_{Guid.NewGuid():N}";
        string environmentVariableName = $"{environmentPrefix}__token";
        string environmentToken = $"{environmentPrefix}.token";
        Environment.SetEnvironmentVariable(environmentVariableName, "from-env");

        try
        {
            string markdown = "---\n" +
                              "title: Configuration Sources\n" +
                              "---\n" +
                              "App={{runtime.build.stamp}}\n" +
                              "Env={{" + environmentToken + "}}\n";
            await File.WriteAllTextAsync(
                Path.Combine(inputDirectory, "content", "001.md"),
                markdown);

            string outputDirectory = Path.Combine(workspace.RootPath, "site");
            CompilationPlanner planner = new(new(), new());
            CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
            SourcebookCompiler compiler = new();
            await compiler.CompileAsync(request, CancellationToken.None);

            string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
            Assert.Contains("App=from-appsettings", chapterHtml, StringComparison.Ordinal);
            Assert.Contains("Env=from-env", chapterHtml, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariableName, null);
        }
    }

    /// <summary>
    /// Verifies that dynamic project macro properties resolve during compilation.
    /// </summary>
    [Fact]
    public async Task CompileAsyncSubstitutesDynamicProjectPropertiesAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "settings"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "settings", "global.yml"),
            """
            project:
              author: Archmage Tester
              license: CC-BY-4.0
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Dynamic Metrics
            ---
            MacroTitle={{macro.title}}
            MacroAuthor={{macro.author}}
            MacroLicense={{macro.license}}
            MacroDateUtc={{macro.dateUtc}}
            MacroChapterCount={{macro.chapterCount}}
            MacroIndexTopicCount={{macro.indexTopicCount}}
            MacroReferenceCount={{macro.referenceCount}}
            MacroGeneratedUtc={{macro.generatedUtc}}
            MacroPageCount={{macro.pageCount}}
            MacroSeeAlso={{macro.seeAlso:Missing Topic}}
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("MacroTitle=", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("{{macro.author}}", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("{{macro.license}}", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("MacroChapterCount=1", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("MacroReferenceCount=0", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("MacroSeeAlso=#REF", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("{{macro.pageCount}}", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("{{macro.dateUtc}}", chapterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("{{macro.generatedUtc}}", chapterHtml, StringComparison.Ordinal);
        Match macroDateMatch = MacroDateUtcRegex().Match(chapterHtml);
        Assert.True(macroDateMatch.Success);
        Match macroPageCountMatch = MacroPageCountRegex().Match(chapterHtml);
        Assert.True(macroPageCountMatch.Success);
        Match indexTopicCountMatch = MacroIndexTopicCountRegex().Match(chapterHtml);
        Assert.True(indexTopicCountMatch.Success);
        int parsedIndexTopicCount = int.Parse(indexTopicCountMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(parsedIndexTopicCount >= 1);
    }

    /// <summary>
    /// Verifies that page title macros resolve correctly for chapter and included content.
    /// </summary>
    [Fact]
    public async Task CompileAsyncMacroPageTitleAndContentPageTitleResolveForChapterAndIncludedContentAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "snippets"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Parent Chapter
            ---
            ChapterPage={{macro.pageTitle}}
            ![](../snippets/lore.md?inline)
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "snippets", "lore.md"),
            """
            ---
            title: Included Lore
            ---
            IncludedPage={{macro.pageTitle}}
            IncludedParent={{macro.contentPageTitle}}
            """);

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("ChapterPage=Parent Chapter", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("IncludedPage=Included Lore", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("IncludedParent=Parent Chapter", chapterHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that entity-name lookup substitutions resolve to the newest matching entity.
    /// </summary>
    [Fact]
    public async Task CompileAsyncEntityNameLookupSubstitutionResolvesNewestEntityByNameAsync()
    {
        using var workspace = TestWorkspace.Create();
        string inputDirectory = workspace.CreateInputDirectory();
        Directory.CreateDirectory(Path.Combine(inputDirectory, "content"));
        Directory.CreateDirectory(Path.Combine(inputDirectory, "spells"));

        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "content", "001.md"),
            """
            ---
            title: Entity Lookup
            ---
            School={{%acid arrow:definition.school}}
            Unknown={{%Not Real:definition.school}}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "100-ACID-ARROW.json"),
            """{"name":"Acid Arrow","definition":{"school":"Old School"}}""");
        await File.WriteAllTextAsync(
            Path.Combine(inputDirectory, "spells", "2323-ACID-ARROW.json"),
            """{"name":"Acid Arrow","definition":{"school":"Conjuration"}}""");

        string outputDirectory = Path.Combine(workspace.RootPath, "site");
        CompilationPlanner planner = new(new(), new());
        CompilationRequest request = planner.Plan(inputDirectory, outputDirectory);
        SourcebookCompiler compiler = new();
        await compiler.CompileAsync(request, CancellationToken.None);

        string chapterHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "chapter-001.html"));
        Assert.Contains("School=Conjuration", chapterHtml, StringComparison.Ordinal);
        Assert.Contains("Unknown={{%Not Real:definition.school}}", chapterHtml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Invokes a private static method using reflection.
    /// </summary>
    /// <param name="type">The <see cref="Type"/> containing the target method.</param>
    /// <param name="method">The method name.</param>
    /// <param name="args">The arguments to pass to the method.</param>
    /// <returns>The method return value.</returns>
    private static object? InvokePrivateStatic(Type type, string method, params object?[] args)
    {
        MethodInfo? methodInfo = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(methodInfo);
        return methodInfo.Invoke(null, args);
    }

    /// <summary>
    /// Creates a regex for extracting the <c>MacroDateUtc</c> value from rendered output.
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> for matching macro date values.</returns>
    [GeneratedRegex("MacroDateUtc=(\\d{4}-\\d{2}-\\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex MacroDateUtcRegex();

    /// <summary>
    /// Creates a regex for extracting the <c>MacroPageCount</c> value from rendered output.
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> for matching page-count values.</returns>
    [GeneratedRegex("MacroPageCount=(\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex MacroPageCountRegex();

    /// <summary>
    /// Creates a regex for extracting the <c>MacroIndexTopicCount</c> value from rendered output.
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> for matching index-topic-count values.</returns>
    [GeneratedRegex("MacroIndexTopicCount=(\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex MacroIndexTopicCountRegex();

    /// <summary>
    /// Provides an isolated filesystem workspace for compiler tests.
    /// </summary>
    private sealed class TestWorkspace : IDisposable
    {
        /// <summary>
        /// Indicates whether the workspace has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestWorkspace"/> class.
        /// </summary>
        /// <param name="rootPath">The root path for the workspace.</param>
        private TestWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        /// <summary>
        /// Gets a <see cref="string"/> representing the workspace root path.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Creates a new test workspace rooted in a unique temporary directory.
        /// </summary>
        /// <returns>A <see cref="TestWorkspace"/> instance.</returns>
        public static TestWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"grimoire-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new(rootPath);
        }

        /// <summary>
        /// Creates and returns the input directory path for this workspace.
        /// </summary>
        /// <returns>A <see cref="string"/> representing the input directory path.</returns>
        public string CreateInputDirectory()
        {
            string inputDirectory = Path.Combine(RootPath, "input");
            Directory.CreateDirectory(inputDirectory);
            return inputDirectory;
        }

        /// <summary>
        /// Releases workspace resources and deletes the workspace directory when possible.
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
                try
                {
                    Directory.Delete(RootPath, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
