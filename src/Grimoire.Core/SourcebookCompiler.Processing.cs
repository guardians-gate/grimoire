using Markdig;
using Humanizer;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Processes markdown tokens, substitutions, and automatic entity mention linking.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Processes inline substitutions, includes, macros, and mention links for markdown content.
    /// </summary>
    private string ProcessInlineTokens(
        string markdown,
        string currentFilePath,
        string sourceRoot,
        string? mentionTargetIdOverride = null,
        string? currentPageTitleOverride = null,
        string? contentPageTitleOverride = null)
    {
        int substitutionCount = ProjectAndFileSubstitutionRegex.Count(markdown);
        int includeCount = IncludeRegex.Count(markdown);
        int macroCount = MacroRegex.Count(markdown);
        ProcessingInlineTokens(currentFilePath, substitutionCount, macroCount, includeCount);
        string currentPageTitle = currentPageTitleOverride ?? ResolveMaterialTitle(currentFilePath, null);
        string contentPageTitle = string.IsNullOrWhiteSpace(contentPageTitleOverride) ? currentPageTitle : contentPageTitleOverride;
        string withProjectSubstitutions = ApplyProjectAndFileSubstitutions(markdown, currentFilePath, sourceRoot, currentPageTitle, contentPageTitle);
        string? activeMentionTargetId = mentionTargetIdOverride ?? ResolveReferenceTargetId(sourceRoot, currentFilePath);
        string? currentEntityTitle = IsReferenceableMaterialPath(sourceRoot, currentFilePath)
            ? ResolveMaterialTitle(currentFilePath, null)
            : null;
        Regex? excludeLinksRegex = ResolveExcludeLinksRegex(currentFilePath);
        bool headingLinksEnabled = ResolveHeadingLinksEnabled(currentFilePath);
        string withMacros = MacroRegex.Replace(withProjectSubstitutions, match =>
        {
            string relativePath = match.Groups["path"].Value.Trim();
            string? property = match.Groups["prop"].Success ? match.Groups["prop"].Value.Trim() : null;
            string absolutePath = ResolveReferencePath(currentFilePath, relativePath);
            return ResolveReferenceValue(absolutePath, property);
        });

        string withIncludes = IncludeRegex.Replace(withMacros, match =>
        {
            string includePath = match.Groups["path"].Value.Trim();
            string title = match.Groups["title"].Value.Trim();
            if (!TryParseIncludePath(includePath, out string includePathWithoutQuery, out bool inline))
            {
                return match.Value;
            }

            if (!IsStructuredInclude(includePathWithoutQuery))
            {
                string rewrittenPath = RewriteAssetPathForOutput(includePath, currentFilePath, sourceRoot);
                return !string.Equals(rewrittenPath, includePath, StringComparison.Ordinal)
                    ? $"![{title}]({rewrittenPath})"
                    : match.Value;
            }

            string absolutePath = ResolveReferencePath(currentFilePath, includePathWithoutQuery);
            IncludingMaterial(absolutePath, inline, currentFilePath);
            return BuildIncludedMaterialHtml(title, absolutePath, sourceRoot, inline, activeMentionTargetId, contentPageTitle);
        });

        return _autoLinkEntityMentions
            ? ApplyEntityMentionLinks(withIncludes, activeMentionTargetId, currentEntityTitle, excludeLinksRegex, headingLinksEnabled)
            : withIncludes;
    }

    /// <summary>
    /// Applies project, entity, and file substitution tokens to markdown content.
    /// </summary>
    private string ApplyProjectAndFileSubstitutions(string markdown, string currentFilePath, string sourceRoot, string currentPageTitle, string contentPageTitle)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        return ProjectAndFileSubstitutionRegex.Replace(markdown, match =>
        {
            string token = match.Groups["token"].Value.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return match.Value;
            }

            if (string.Equals(token, "macro.pageCount", StringComparison.OrdinalIgnoreCase))
            {
                SubstitutingMacroPageCount(currentFilePath, token);
                return ProjectPageCountPlaceholder;
            }

            if (token.StartsWith("macro.seeAlso:", StringComparison.OrdinalIgnoreCase))
            {
                string topic = token["macro.seeAlso:".Length..].Trim();
                if (string.IsNullOrWhiteSpace(topic))
                {
                    return "#REF";
                }

                SubstitutingMacroSeeAlso(currentFilePath, token);
                return $"{ProjectSeeAlsoPlaceholderPrefix}{EncodePlaceholderToken(topic)}{ProjectSeeAlsoPlaceholderSuffix}";
            }

            if (string.Equals(token, "macro.pageTitle", StringComparison.OrdinalIgnoreCase))
            {
                SubstitutingMacroPageTitle(currentFilePath, token, currentPageTitle);
                return currentPageTitle;
            }

            if (string.Equals(token, "macro.contentPageTitle", StringComparison.OrdinalIgnoreCase))
            {
                SubstitutingMacroContentPageTitle(currentFilePath, token, contentPageTitle);
                return contentPageTitle;
            }

            if (DynamicProjectSubstitutionTokens.Contains(token))
            {
                SubstitutingDynamicMacroPlaceholder(currentFilePath, token);
                return $"{ProjectDynamicPlaceholderPrefix}{EncodePlaceholderToken(token)}{ProjectDynamicPlaceholderSuffix}";
            }

            if (_activeProjectSubstitutionSettings.TryGetValue(token, out string? compileTimeValue))
            {
                SubstitutingCompileTimeSetting(currentFilePath, token);
                return compileTimeValue;
            }

            if (token.StartsWith('%'))
            {
                string entitySpec = token[1..].Trim();
                int separatorIndex = entitySpec.LastIndexOf(':');
                string entityName;
                string property;
                if (separatorIndex <= 0)
                {
                    entityName = entitySpec;
                    property = "content";
                }
                else
                {
                    entityName = entitySpec[..separatorIndex].Trim();
                    property = entitySpec[(separatorIndex + 1)..].Trim();
                    if (string.IsNullOrWhiteSpace(property))
                    {
                        property = "content";
                    }
                }

                if (string.IsNullOrWhiteSpace(entityName))
                {
                    return match.Value;
                }

                string? entityPath = ResolveEntityPathByName(sourceRoot, entityName);
                if (string.IsNullOrWhiteSpace(entityPath))
                {
                    EntityLookupNoMatch(currentFilePath, entityName);
                    return match.Value;
                }

                EntityLookupResolved(currentFilePath, entityName, property, entityPath);
                return ResolveReferenceValue(entityPath, property);
            }

            if (!token.StartsWith('@')) return match.Value;

            {
                string fileSpec = token[1..].Trim();
                int separatorIndex = fileSpec.LastIndexOf(':');
                string relativePath;
                string property;
                if (separatorIndex <= 0)
                {
                    relativePath = fileSpec;
                    property = "content";
                }
                else
                {
                    relativePath = fileSpec[..separatorIndex].Trim();
                    property = fileSpec[(separatorIndex + 1)..].Trim();
                    if (string.IsNullOrWhiteSpace(property))
                    {
                        property = "content";
                    }
                }

                if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(property))
                {
                    return match.Value;
                }

                string absolutePath = ResolveReferencePath(currentFilePath, relativePath);
                FileSubstitutionResolved(currentFilePath, relativePath, property, absolutePath);
                return ResolveReferenceValue(absolutePath, property);
            }

        });
    }

    /// <summary>
    /// Renders markdown to HTML after normalizing supported alert syntax.
    /// </summary>
    private static string RenderMarkdownToHtml(string markdown)
    {
        string normalized = NormalizeGithubAlertSyntax(markdown);
        string html = Markdown.ToHtml(normalized, MarkdownPipeline);
        return ConvertGithubAlertBlockquotes(html);
    }

    /// <summary>
    /// Normalizes GitHub-style alert headers into emphasized markdown text.
    /// </summary>
    private static string NormalizeGithubAlertSyntax(string markdown)
    {
        return GithubAlertHeaderRegex.Replace(markdown, match =>
        {
            string alertType = match.Groups["type"].Value.Trim();
            if (!TryResolveAlertTitle(alertType, out string alertTitle))
            {
                return match.Value;
            }

            string prefix = match.Groups["prefix"].Value;
            string tail = match.Groups["tail"].Value.Trim();
            return string.IsNullOrWhiteSpace(tail)
                ? $"{prefix}**{alertTitle}:**"
                : $"{prefix}**{alertTitle}:** {tail}";
        });
    }

    /// <summary>
    /// Converts rendered GitHub-style alert blockquotes into styled alert markup.
    /// </summary>
    private static string ConvertGithubAlertBlockquotes(string html)
    {
        return GithubAlertBlockquoteRegex.Replace(html, match =>
        {
            string alertType = match.Groups["type"].Value.Trim();
            if (!TryResolveAlertTitle(alertType, out string alertTitle))
            {
                return match.Value;
            }

            string slug = alertTitle switch
            {
                "Note" => "note",
                "Tip" => "tip",
                "Important" => "important",
                "Warning" => "warning",
                "Caution" => "caution",
                _ => "note",
            };
            return $"<blockquote class=\"alert alert-{EscapeHtml(slug)}\"><p><strong class=\"alert-title\">{EscapeHtml(alertTitle)}:</strong>";
        });
    }

    /// <summary>
    /// Tries to resolve a supported alert title from an alert type token.
    /// </summary>
    private static bool TryResolveAlertTitle(string alertType, out string alertTitle)
    {
        alertTitle = alertType.Transform(To.LowerCase, To.TitleCase);
        return SupportedAlertTitles.Contains(alertTitle);
    }

    /// <summary>
    /// Configures mention-link lookup tables from index topics and dictionary candidates.
    /// </summary>
    private void ConfigureEntityMentionLinks(
        IReadOnlyCollection<IndexTopic> topics,
        ExportTarget target,
        bool enabled,
        bool includeReferenceDictionaryCandidates,
        string sourceRoot)
    {
        _entityMentionLinks.Clear();
        _entityMentionTargets.Clear();
        _entityMentionPreferredTargetIds.Clear();
        _entityMentionSourcePaths.Clear();
        _autoLinkEntityMentions = enabled || includeReferenceDictionaryCandidates;
        if (!_autoLinkEntityMentions)
        {
            return;
        }

        void RegisterMentionTopic(string title, string href, string targetId, string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length < 3 || string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(targetId))
            {
                return;
            }

            if (href.Contains("index-topics.html", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_entityMentionLinks.TryAdd(title, href))
            {
                _entityMentionPreferredTargetIds[title] = targetId;
                if (!string.IsNullOrWhiteSpace(sourcePath))
                {
                    _entityMentionSourcePaths[title] = sourcePath;
                }

                return;
            }

            string? existingSourcePath = _entityMentionSourcePaths.GetValueOrDefault(title);
            if (!ShouldPreferMentionCandidate(existingSourcePath, sourcePath))
            {
                return;
            }

            _entityMentionLinks[title] = href;
            _entityMentionPreferredTargetIds[title] = targetId;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                _entityMentionSourcePaths[title] = sourcePath;
            }
        }

        foreach (IndexTopic topic in topics)
        {
            if (string.IsNullOrWhiteSpace(topic.Title) || topic.Title.Length < 3)
            {
                continue;
            }

            string targetId = ResolvePreferredMentionTargetId(topic);
            string href = target == ExportTarget.Website
                ? ResolveWebsiteMentionHref(topic)
                : "#" + targetId;
            string? sourcePath = null;
            if (!string.IsNullOrWhiteSpace(topic.SourcePath))
            {
                sourcePath = Path.GetFullPath(Path.Combine(sourceRoot, topic.SourcePath));
            }

            RegisterMentionTopic(topic.Title, href, targetId, sourcePath);
        }

        if (!includeReferenceDictionaryCandidates)
        {
            return;
        }

        foreach (ReferenceDictionaryMentionCandidate candidate in LoadReferenceDictionaryMentionCandidates(sourceRoot))
        {
            string href = target == ExportTarget.Website
                ? $"chapter-appendix-reference-dictionary.html#{candidate.TargetId}"
                : "#" + candidate.TargetId;
            RegisterMentionTopic(candidate.Title, href, candidate.TargetId, candidate.SourcePath);
        }
    }

    /// <summary>
    /// Resolves the preferred mention target identifier for an index topic.
    /// </summary>
    private static string ResolvePreferredMentionTargetId(IndexTopic topic)
    {
        foreach (string targetId in topic.TargetIds)
        {
            if (targetId.StartsWith("dict-ref-", StringComparison.OrdinalIgnoreCase))
            {
                return targetId;
            }
        }

        foreach (string targetId in topic.TargetIds)
        {
            if (targetId.StartsWith("ref-", StringComparison.OrdinalIgnoreCase))
            {
                return targetId;
            }
        }

        return topic.TargetIds.Length > 0 ? topic.TargetIds[0] : topic.Id;
    }

    /// <summary>
    /// Resolves the website hyperlink target for an index topic mention.
    /// </summary>
    private static string ResolveWebsiteMentionHref(IndexTopic topic)
    {
        string targetId = ResolvePreferredMentionTargetId(topic);
        if (targetId.StartsWith("dict-ref-", StringComparison.OrdinalIgnoreCase))
        {
            return $"chapter-appendix-reference-dictionary.html#{targetId}";
        }

        if (targetId.StartsWith("ref-", StringComparison.OrdinalIgnoreCase))
        {
            return $"chapter-appendix-snippets.html#{targetId}";
        }

        if (string.Equals(targetId, "foreword", StringComparison.OrdinalIgnoreCase))
        {
            return "chapter-foreword.html#foreword";
        }

        if (string.Equals(targetId, "bibliography", StringComparison.OrdinalIgnoreCase))
        {
            return "bibliography.html#bibliography";
        }

        if (string.Equals(targetId, "index", StringComparison.OrdinalIgnoreCase))
        {
            return "index-topics.html#index";
        }

        return string.Equals(targetId, "cover", StringComparison.OrdinalIgnoreCase)
            ? "index.html#cover"
            : $"index.html#{targetId}";
    }

    /// <summary>
    /// Determines whether a candidate mention source should replace the existing source.
    /// </summary>
    private static bool ShouldPreferMentionCandidate(string? existingSourcePath, string? candidateSourcePath)
    {
        if (string.IsNullOrWhiteSpace(candidateSourcePath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(existingSourcePath))
        {
            return true;
        }

        bool existingHasId = TryParseLeadingNumericIdentifier(existingSourcePath, out long existingId);
        bool candidateHasId = TryParseLeadingNumericIdentifier(candidateSourcePath, out long candidateId);
        if (existingHasId && candidateHasId && existingId != candidateId)
        {
            return candidateId > existingId;
        }

        if (candidateHasId && !existingHasId)
        {
            return true;
        }

        if (existingHasId && !candidateHasId)
        {
            return false;
        }

        DateTime existingTimestamp = File.Exists(existingSourcePath)
            ? File.GetLastWriteTimeUtc(existingSourcePath)
            : DateTime.MinValue;
        DateTime candidateTimestamp = File.Exists(candidateSourcePath)
            ? File.GetLastWriteTimeUtc(candidateSourcePath)
            : DateTime.MinValue;

        return candidateTimestamp > existingTimestamp;
    }

    /// <summary>
    /// Tries to parse a leading numeric identifier from a file name stem.
    /// </summary>
    private static bool TryParseLeadingNumericIdentifier(string path, out long identifier)
    {
        identifier = 0;
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        int separatorIndex = fileName.IndexOfAny(['-', '_']);
        string leading = separatorIndex > 0 ? fileName[..separatorIndex] : fileName;
        return long.TryParse(leading, NumberStyles.Integer, CultureInfo.InvariantCulture, out identifier);
    }

    /// <summary>
    /// Applies collected mention-target anchors back onto index topics.
    /// </summary>
    private void ApplyMentionTargetsToIndexTopics(List<IndexTopic> topics, string sourceRoot)
    {
        if (topics.Count == 0 || _entityMentionTargets.Count == 0)
        {
            return;
        }

        Dictionary<string, int> topicIndexesByTitle = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < topics.Count; index++)
        {
            topicIndexesByTitle[topics[index].Title] = index;
        }

        foreach ((string title, HashSet<string> mentionTargets) in _entityMentionTargets)
        {
            if (mentionTargets.Count == 0)
            {
                continue;
            }

            if (!topicIndexesByTitle.TryGetValue(title, out int existingIndex))
            {
                if (!_entityMentionPreferredTargetIds.TryGetValue(title, out string? preferredTargetId) || string.IsNullOrWhiteSpace(preferredTargetId))
                {
                    continue;
                }

                string sourcePath = string.Empty;
                if (_entityMentionSourcePaths.TryGetValue(title, out string? mentionSourcePath) &&
                    !string.IsNullOrWhiteSpace(mentionSourcePath))
                {
                    string relativeSourcePath = Path.GetRelativePath(sourceRoot, mentionSourcePath)
                        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    if (!relativeSourcePath.StartsWith("..", StringComparison.Ordinal))
                    {
                        sourcePath = relativeSourcePath;
                    }
                }

                ImmutableArray<string> synthesizedTargets =
                [
                    .. mentionTargets
                    .Concat([preferredTargetId])
                    .Where(static target => !string.IsNullOrWhiteSpace(target))
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                ];
                if (synthesizedTargets.Length == 0)
                {
                    continue;
                }

                topics.Add(new IndexTopic(title, sourcePath, $"idx-{BuildSectionId(title)}", synthesizedTargets));
                topicIndexesByTitle[title] = topics.Count - 1;
                continue;
            }

            IndexTopic topic = topics[existingIndex];
            ImmutableArray<string> combinedTargets =
            [
                .. topic.TargetIds
                .Concat(mentionTargets)
                .Where(static target => !string.IsNullOrWhiteSpace(target))
                .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
            topics[existingIndex] = topic with { TargetIds = combinedTargets };
        }

        topics.Sort(static (left, right) => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Registers a mention target anchor for a topic title.
    /// </summary>
    private void RegisterEntityMentionTarget(string topicTitle, string? mentionTargetId)
    {
        if (string.IsNullOrWhiteSpace(mentionTargetId))
        {
            return;
        }

        if (!_entityMentionTargets.TryGetValue(topicTitle, out HashSet<string>? targets))
        {
            targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _entityMentionTargets[topicTitle] = targets;
        }

        targets.Add(mentionTargetId);
    }

    /// <summary>
    /// Tries to resolve a mention hyperlink for a candidate title.
    /// </summary>
    private bool TryResolveMentionLink(string candidate, out string href, out string topicTitle)
    {
        if (_entityMentionLinks.TryGetValue(candidate, out string? resolvedHref))
        {
            href = resolvedHref;
            topicTitle = candidate;
            return true;
        }

        href = string.Empty;
        topicTitle = string.Empty;
        return false;
    }

    /// <summary>
    /// Applies entity mention links to markdown while preserving protected segments.
    /// </summary>
    private string ApplyEntityMentionLinks(string markdown, string? mentionTargetId, string? currentEntityTitle, Regex? excludeLinksRegex, bool headingLinksEnabled)
    {
        if (string.IsNullOrWhiteSpace(markdown) || _entityMentionLinks.Count == 0)
        {
            return markdown;
        }

        List<string> protectedSegments = [];
        string prepared = ProtectedMarkdownRegex.Replace(markdown, match =>
        {
            protectedSegments.Add(match.Value);
            return $"@@GRIMOIRE{protectedSegments.Count - 1}@@";
        });

        if (!headingLinksEnabled)
        {
            prepared = ProtectHeadingSegments(prepared, protectedSegments);
        }

        HashSet<string> emittedDdbAnchors = [];
        prepared = DdbTagMentionRegex.Replace(prepared, match =>
        {
            string mention = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(currentEntityTitle) &&
                string.Equals(mention, currentEntityTitle, StringComparison.OrdinalIgnoreCase)
                || excludeLinksRegex is not null && excludeLinksRegex.IsMatch(mention)
                || !TryResolveMentionLink(mention, out string href, out string topicTitle))
            {
                return mention;
            }

            protectedSegments.Add(BuildMentionMarkup(mention, href, topicTitle, mentionTargetId, emittedDdbAnchors));
            return $"@@GRIMOIRE{protectedSegments.Count - 1}@@";
        });

        HashSet<string> emittedAnchors = [];
        KeyValuePair<string, string>[] mentionLinks =
        [
            .. _entityMentionLinks
            .Where(static entry => !entry.Value.Contains("index-topics.html", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static entry => entry.Key.Length)
        ];
        for (int mentionIndex = 0; mentionIndex < mentionLinks.Length; mentionIndex++)
        {
            int displayIndex = mentionIndex + 1;
            if (ShouldLogPreviewAutoLinkProgress(displayIndex, mentionLinks.Length))
            {
                AutoLinkingMention(displayIndex, mentionLinks.Length, mentionLinks[mentionIndex].Key);
            }
        }

        prepared = ApplyEntityMentionLinks(prepared, mentionLinks, currentEntityTitle, excludeLinksRegex, mentionTargetId, emittedAnchors, protectedSegments);
        AutoLinkingMentionsCompleted(mentionLinks.Length);
        return MentionPlaceholderRegex.Replace(prepared, match =>
        {
            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                index < 0 ||
                index >= protectedSegments.Count)
            {
                return match.Value;
            }

            return protectedSegments[index];
        });
    }

    /// <summary>
    /// Applies mention links using a precomputed mention list and emitted-anchor tracking.
    /// </summary>
    private string ApplyEntityMentionLinks(
        string markdown,
        KeyValuePair<string, string>[] mentionLinks,
        string? currentEntityTitle,
        Regex? excludeLinksRegex,
        string? mentionTargetId,
        HashSet<string> emittedAnchors,
        List<string> protectedSegments)
    {
        if (mentionLinks.Length == 0)
        {
            return markdown;
        }

        Dictionary<char, List<KeyValuePair<string, string>>> mentionsByFirstCharacter = BuildMentionLookup(mentionLinks);
        StringBuilder result = new(markdown.Length);
        int segmentStart = 0;
        int position = 0;
        while (position < markdown.Length)
        {
            char current = markdown[position];
            if (!IsMentionBoundaryBefore(markdown, position) ||
                !mentionsByFirstCharacter.TryGetValue(char.ToUpperInvariant(current), out List<KeyValuePair<string, string>>? candidates) ||
                !TryMatchMention(markdown, position, candidates, currentEntityTitle, excludeLinksRegex, out string matchedText, out string matchedName, out string href))
            {
                position++;
                continue;
            }

            result.Append(markdown, segmentStart, position - segmentStart);
            protectedSegments.Add(BuildMentionMarkup(matchedText, href, matchedName, mentionTargetId, emittedAnchors));
            result.Append(CultureInfo.InvariantCulture, $"@@GRIMOIRE{protectedSegments.Count - 1}@@");
            position += matchedText.Length;
            segmentStart = position;
        }

        result.Append(markdown, segmentStart, markdown.Length - segmentStart);
        return result.ToString();
    }

    /// <summary>
    /// Builds a first-character lookup map for mention link candidates.
    /// </summary>
    private static Dictionary<char, List<KeyValuePair<string, string>>> BuildMentionLookup(KeyValuePair<string, string>[] mentionLinks)
    {
        Dictionary<char, List<KeyValuePair<string, string>>> lookup = [];
        foreach (KeyValuePair<string, string> mentionLink in mentionLinks)
        {
            if (string.IsNullOrWhiteSpace(mentionLink.Key))
            {
                continue;
            }

            char key = char.ToUpperInvariant(mentionLink.Key[0]);
            if (!lookup.TryGetValue(key, out List<KeyValuePair<string, string>>? mentions))
            {
                mentions = [];
                lookup[key] = mentions;
            }

            mentions.Add(mentionLink);
        }

        foreach (List<KeyValuePair<string, string>> mentions in lookup.Values)
        {
            mentions.Sort(static (left, right) => right.Key.Length.CompareTo(left.Key.Length));
        }

        return lookup;
    }
}
