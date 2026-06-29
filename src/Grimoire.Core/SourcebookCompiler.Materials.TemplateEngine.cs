using YamlDotNet.Core;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grimoire.Core;

/// <summary>
/// Implements template parsing and JSON path resolution for material rendering.
/// </summary>
public sealed partial class SourcebookCompiler
{
    /// <summary>
    /// Applies a template against a JSON root model and removes empty output properties.
    /// </summary>
    private static string ApplyTemplate(string template, JsonElement root, string jsonPath)
    {
        string rendered = RenderTemplateFragment(template, root, null, jsonPath);

        return RemoveEmptyTemplateProperties(rendered);
    }

    /// <summary>
    /// Renders a template fragment against the root model and optional current loop item.
    /// </summary>
    private static string RenderTemplateFragment(string template, JsonElement root, JsonElement? currentItem, string jsonPath)
    {
        string expanded = ExpandEachBlocks(template, root, currentItem, jsonPath);
        return ReplaceTemplateTokens(expanded, token => ResolveTemplateValue(root, currentItem, token, jsonPath));
    }

    /// <summary>
    /// Replaces template tokens by invoking the provided token resolver.
    /// </summary>
    private static string ReplaceTemplateTokens(string template, Func<string, string> resolver)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        StringBuilder output = new(template.Length);
        int cursor = 0;
        while (cursor < template.Length)
        {
            int start = template.IndexOf("{{", cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                output.Append(template, cursor, template.Length - cursor);
                break;
            }

            output.Append(template, cursor, start - cursor);
            int end = FindTemplateTokenEnd(template, start + 2);
            if (end < 0)
            {
                output.Append(template, start, template.Length - start);
                break;
            }

            string token = template.Substring(start + 2, end - start - 2).Trim();
            output.Append(resolver(token));
            cursor = end + 2;
        }

        return output.ToString();
    }

    /// <summary>
    /// Finds the end position of a template token while respecting nested token delimiters.
    /// </summary>
    private static int FindTemplateTokenEnd(string template, int searchStart)
    {
        int depth = 1;
        for (int index = searchStart; index < template.Length - 1; index++)
        {
            if (template[index] == '{' && template[index + 1] == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (template[index] == '}' && template[index + 1] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }

                index++;
            }
        }

        return -1;
    }

    /// <summary>
    /// Expands each-loop blocks in a template before token replacement.
    /// </summary>
    private static string ExpandEachBlocks(string template, JsonElement root, JsonElement? currentItem, string jsonPath)
    {
        string expanded = template;
        while (true)
        {
            string next = EachBlockRegex.Replace(expanded, match =>
            {
                string token = match.Groups["path"].Value.Trim();
                (string arrayPath, _) = ParseTemplateToken(token);
                if (!TryResolveTemplateValue(root, currentItem, arrayPath, out JsonElement arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
                {
                    return string.Empty;
                }

                StringBuilder loopOutput = new();
                string body = match.Groups["body"].Value;
                foreach (JsonElement element in EnumerateTemplateArrayElements(arrayElement, arrayPath))
                {
                    loopOutput.Append(RenderTemplateFragment(body, root, element, jsonPath));
                }

                return loopOutput.ToString();
            });

            if (string.Equals(next, expanded, StringComparison.Ordinal))
            {
                return expanded;
            }

            expanded = next;
        }
    }

    /// <summary>
    /// Enumerates template array elements, applying sorting and deduplication rules.
    /// </summary>
    private static List<JsonElement> EnumerateTemplateArrayElements(JsonElement arrayElement, string arrayPath)
    {
        List<JsonElement> elements = [.. arrayElement.EnumerateArray()];
        if (ShouldSortCharacterSpellList(arrayPath))
        {
            elements.Sort(static (left, right) =>
            {
                int leftLevel = ResolveSpellLevel(left);
                int rightLevel = ResolveSpellLevel(right);
                int levelComparison = leftLevel.CompareTo(rightLevel);
                if (levelComparison != 0)
                {
                    return levelComparison;
                }

                string leftName = ResolveSpellName(left);
                string rightName = ResolveSpellName(right);
                return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            });
        }

        return ShouldDeduplicateTemplateArray(arrayPath) ? DeduplicateTemplateArrayElements(elements) : elements;
    }

    /// <summary>
    /// Determines whether an array path should be sorted as a character spell list.
    /// </summary>
    private static bool ShouldSortCharacterSpellList(string arrayPath)
    {
        return !string.IsNullOrWhiteSpace(arrayPath)
               && arrayPath.StartsWith("ddb.character.spells.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether an array path should be deduplicated during template rendering.
    /// </summary>
    private static bool ShouldDeduplicateTemplateArray(string arrayPath)
    {
        if (string.IsNullOrWhiteSpace(arrayPath))
        {
            return false;
        }

        return string.Equals(arrayPath, "classFeatures", StringComparison.OrdinalIgnoreCase) ||
               arrayPath.StartsWith("ddb.character.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes duplicate template array elements based on semantic key fields.
    /// </summary>
    private static List<JsonElement> DeduplicateTemplateArrayElements(IEnumerable<JsonElement> elements)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<JsonElement> deduped = [];
        foreach (JsonElement element in elements)
        {
            string key = BuildTemplateElementDeduplicationKey(element);
            if (!seen.Add(key))
            {
                continue;
            }

            deduped.Add(element);
        }

        return deduped;
    }

    /// <summary>
    /// Builds a deduplication key for a template array element.
    /// </summary>
    private static string BuildTemplateElementDeduplicationKey(JsonElement element)
    {
        string name = ResolveTemplateElementString(element, "definition.name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = ResolveTemplateElementString(element, "name");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = ResolveTemplateElementString(element, "label");
        }

        string description = ResolveTemplateElementString(element, "definition.description");
        if (string.IsNullOrWhiteSpace(description))
        {
            description = ResolveTemplateElementString(element, "description");
        }

        return $"{name.Trim()}|{description.Trim()}";
    }

    /// <summary>
    /// Resolves a string value from a JSON element using a dotted path.
    /// </summary>
    private static string ResolveTemplateElementString(JsonElement element, string path)
    {
        if (!TryResolveJsonPath(element, path, out JsonElement value))
        {
            return string.Empty;
        }

        string result = value.ToString();
        return string.IsNullOrWhiteSpace(result) ? string.Empty : result;
    }

    /// <summary>
    /// Resolves a spell level from the preferred spell element properties.
    /// </summary>
    private static int ResolveSpellLevel(JsonElement spellElement)
    {
        if (TryResolveJsonPath(spellElement, "definition.level", out JsonElement definitionLevel) &&
            definitionLevel.ValueKind == JsonValueKind.Number &&
            definitionLevel.TryGetInt32(out int level))
        {
            return level;
        }

        if (TryResolveJsonPath(spellElement, "level", out JsonElement rootLevel) &&
            rootLevel.ValueKind == JsonValueKind.Number &&
            rootLevel.TryGetInt32(out int fallbackLevel))
        {
            return fallbackLevel;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Resolves a spell name from the preferred spell element properties.
    /// </summary>
    private static string ResolveSpellName(JsonElement spellElement)
    {
        if (TryResolveJsonPath(spellElement, "definition.name", out JsonElement definitionName))
        {
            string value = definitionName.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (TryResolveJsonPath(spellElement, "name", out JsonElement name))
        {
            string value = name.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Removes empty placeholder rows and redundant whitespace from rendered markdown.
    /// </summary>
    private static string RemoveEmptyTemplateProperties(string markdown)
    {
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = normalized.Split('\n');
        List<string> filteredLines = [];

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmed = line.Trim();
            if (EmptyTemplateTableRowRegex.IsMatch(trimmed) ||
                EmptyTemplateSlashOnlyRowRegex.IsMatch(trimmed) ||
                EmptyTemplateBulletRegex.IsMatch(trimmed))
            {
                continue;
            }

            if (MarkdownHeadingRegex.IsMatch(trimmed))
            {
                Match headingMatch = MarkdownHeadingRegex.Match(trimmed);
                int headingLevel = headingMatch.Groups["level"].Value.Length;
                if (headingLevel <= 1)
                {
                    filteredLines.Add(line);
                    continue;
                }

                int nextIndex = index + 1;
                while (nextIndex < lines.Length && string.IsNullOrWhiteSpace(lines[nextIndex]))
                {
                    nextIndex++;
                }

                if (nextIndex >= lines.Length || MarkdownHeadingRegex.IsMatch(lines[nextIndex].Trim()))
                {
                    continue;
                }
            }

            filteredLines.Add(line);
        }

        List<string> withoutEmptyTables = [];
        for (int index = 0; index < filteredLines.Count; index++)
        {
            string line = filteredLines[index];
            if (!MarkdownTableRowRegex.IsMatch(line.Trim()))
            {
                withoutEmptyTables.Add(line);
                continue;
            }

            List<string> tableLines = [];
            int cursor = index;
            while (cursor < filteredLines.Count && MarkdownTableRowRegex.IsMatch(filteredLines[cursor].Trim()))
            {
                tableLines.Add(filteredLines[cursor]);
                cursor++;
            }

            bool hasDataRow = false;
            for (int rowIndex = 2; rowIndex < tableLines.Count; rowIndex++)
            {
                string row = tableLines[rowIndex].Trim();
                if (!MarkdownTableSeparatorRegex.IsMatch(row) &&
                    !EmptyTemplateTableRowRegex.IsMatch(row) &&
                    !EmptyTemplateSlashOnlyRowRegex.IsMatch(row))
                {
                    hasDataRow = true;
                    break;
                }
            }

            if (hasDataRow || tableLines.Count < 2)
            {
                withoutEmptyTables.AddRange(tableLines);
            }

            index = cursor - 1;
        }

        string compact = string.Join('\n', withoutEmptyTables).Trim();
        return ExtraBlankLinesRegex.Replace(compact, "\n\n");
    }

    /// <summary>
    /// Resolves a template token value with fallback handling and computed fields.
    /// </summary>
    private static string ResolveTemplateValue(JsonElement root, JsonElement? currentItem, string key, string jsonPath)
    {
        (string keyPath, string? formatHint) = ParseTemplateToken(key);
        bool hasFallback = TryParseFallbackFormat(formatHint, out string fallbackTemplate);
        if (hasFallback)
        {
            formatHint = null;
        }

        if (TryResolveTemplateValue(root, currentItem, keyPath, out JsonElement value))
        {
            if (ShouldSuppressLongRange(root, keyPath, value))
            {
                return hasFallback
                    ? RenderTemplateFragment(fallbackTemplate, root, currentItem, jsonPath)
                    : string.Empty;
            }

            string formattedValue = JsonValueFormatter.ToDisplayString(value, keyPath, formatHint);
            if (!string.IsNullOrWhiteSpace(formattedValue))
            {
                return formattedValue;
            }
        }

        if (TryResolveComputedAbilityScore(root, keyPath, out string computedAbilityScore))
        {
            return computedAbilityScore;
        }

        if (TryResolveDerivedTemplateValue(root, keyPath, formatHint, out string derivedValue))
        {
            return derivedValue;
        }

        if (TryResolveSyntheticTemplateValue(jsonPath, keyPath, formatHint, out string syntheticValue) &&
            !string.IsNullOrWhiteSpace(syntheticValue))
        {
            return syntheticValue;
        }

        return hasFallback
            ? RenderTemplateFragment(fallbackTemplate, root, currentItem, jsonPath)
            : string.Empty;
    }

    /// <summary>
    /// Tries to resolve a template value from the current item scope, then the root model.
    /// </summary>
    private static bool TryResolveTemplateValue(JsonElement root, JsonElement? currentItem, string keyPath, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        if (!currentItem.HasValue) return TryResolveJsonPath(root, keyPath, out value);
        if (string.Equals(keyPath, "this", StringComparison.OrdinalIgnoreCase))
        {
            value = currentItem.Value;
            return true;
        }

        if (keyPath.StartsWith("this.", StringComparison.OrdinalIgnoreCase))
        {
            string localPath = keyPath[5..];
            if (TryResolveJsonPath(currentItem.Value, localPath, out JsonElement localValue))
            {
                value = localValue;
                return true;
            }
        }

        if (keyPath.StartsWith("this[", StringComparison.OrdinalIgnoreCase))
        {
            string localPath = keyPath[4..];
            if (TryResolveJsonPath(currentItem.Value, localPath, out JsonElement localValue))
            {
                value = localValue;
                return true;
            }
        }

        if (!TryResolveJsonPath(currentItem.Value, keyPath, out JsonElement implicitLocalValue))
            return TryResolveJsonPath(root, keyPath, out value);
        value = implicitLocalValue;
        return true;

    }

    /// <summary>
    /// Parses a template token into its value path and optional format hint.
    /// </summary>
    private static (string Path, string? FormatHint) ParseTemplateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (string.Empty, null);
        }

        int formatSeparator = token.IndexOf("::", StringComparison.Ordinal);
        int separatorLength = 2;
        if (formatSeparator < 0)
        {
            formatSeparator = token.IndexOf('|', StringComparison.Ordinal);
            separatorLength = 1;
        }

        if (formatSeparator < 0)
        {
            return (token.Trim(), null);
        }

        string path = token[..formatSeparator].Trim();
        string formatHint = token[(formatSeparator + separatorLength)..].Trim();
        return (path, string.IsNullOrWhiteSpace(formatHint) ? null : formatHint);
    }

    /// <summary>
    /// Tries to parse a fallback template format hint from a token format value.
    /// </summary>
    private static bool TryParseFallbackFormat(string? formatHint, out string fallbackTemplate)
    {
        fallbackTemplate = string.Empty;
        if (string.IsNullOrWhiteSpace(formatHint) || formatHint[0] != '-')
        {
            return false;
        }

        fallbackTemplate = formatHint[1..].Trim();
        return true;
    }

    /// <summary>
    /// Tries to resolve synthetic template values derived from source file naming conventions.
    /// </summary>
    private static bool TryResolveSyntheticTemplateValue(string jsonPath, string keyPath, string? formatHint, out string value)
    {
        value = string.Empty;
        if (!TryParseDndBeyondNumberedFileInfo(jsonPath, out string id, out string name, out string entityDirectory))
        {
            return false;
        }

        if (string.Equals(keyPath, "_dndBeyondId", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(formatHint, "ddb-link", StringComparison.OrdinalIgnoreCase))
            {
                value = $"[{id}](https://www.dndbeyond.com/characters/{id})";
                return true;
            }

            if (string.Equals(formatHint, "ddb-spell-link", StringComparison.OrdinalIgnoreCase))
            {
                value = $"[{id}](https://www.dndbeyond.com/spells/{id})";
                return true;
            }

            value = id;
            return true;
        }

        if (string.Equals(keyPath, "_nameFromFile", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyPath, "_playerNameFromFile", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyPath, "name", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyPath, "title", StringComparison.OrdinalIgnoreCase))
        {
            value = name;
            return true;
        }

        if (!string.Equals(keyPath, "_entityDirectory", StringComparison.OrdinalIgnoreCase)) return false;
        value = entityDirectory;
        return true;

    }

    /// <summary>
    /// Tries to resolve derived template values, including definition and inferred title fields.
    /// </summary>
    private static bool TryResolveDerivedTemplateValue(JsonElement root, string keyPath, string? formatHint, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(keyPath) ||
            keyPath.Contains('.', StringComparison.Ordinal) ||
            keyPath.Contains('[', StringComparison.Ordinal))
        {
            return false;
        }

        bool isNameOrTitle =
            string.Equals(keyPath, "name", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyPath, "title", StringComparison.OrdinalIgnoreCase);

        if (isNameOrTitle && TryResolveJsonPath(root, "ddb.character.name", out JsonElement characterNameElement))
        {
            string characterName = JsonValueFormatter.ToDisplayString(characterNameElement, keyPath, formatHint);
            if (!string.IsNullOrWhiteSpace(characterName))
            {
                value = characterName;
                return true;
            }
        }

        if (!TryResolveJsonPath(root, $"definition.{keyPath}", out JsonElement definitionValue)) return false;
        string formattedDefinitionValue = JsonValueFormatter.ToDisplayString(definitionValue, keyPath, formatHint);
        if (string.IsNullOrWhiteSpace(formattedDefinitionValue)) return false;
        value = formattedDefinitionValue;
        return true;
    }

    /// <summary>
    /// Tries to resolve a computed ability score from D&amp;D Beyond stat arrays.
    /// </summary>
    private static bool TryResolveComputedAbilityScore(JsonElement root, string keyPath, out string value)
    {
        value = string.Empty;
        int abilityIndex;
        if (string.Equals(keyPath, "strength", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyPath, "str", StringComparison.OrdinalIgnoreCase))
        {
            abilityIndex = 0;
        }
        else if (string.Equals(keyPath, "dexterity", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(keyPath, "dex", StringComparison.OrdinalIgnoreCase))
        {
            abilityIndex = 1;
        }
        else if (string.Equals(keyPath, "constitution", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(keyPath, "con", StringComparison.OrdinalIgnoreCase))
        {
            abilityIndex = 2;
        }
        else if (string.Equals(keyPath, "intelligence", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(keyPath, "int", StringComparison.OrdinalIgnoreCase))
        {
            abilityIndex = 3;
        }
        else if (string.Equals(keyPath, "wisdom", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(keyPath, "wis", StringComparison.OrdinalIgnoreCase))
        {
            abilityIndex = 4;
        }
        else if (string.Equals(keyPath, "charisma", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(keyPath, "cha", StringComparison.OrdinalIgnoreCase))
        {
            abilityIndex = 5;
        }
        else
        {
            abilityIndex = -1;
        }

        if (abilityIndex < 0)
        {
            return false;
        }

        if (!TryResolveIntegerPath(root, $"ddb.character.stats[{abilityIndex}].value", out int baseScore))
        {
            return false;
        }

        TryResolveIntegerPath(root, $"ddb.character.bonusStats[{abilityIndex}].value", out int bonusScore);
        bool hasOverride = TryResolveIntegerPath(root, $"ddb.character.overrideStats[{abilityIndex}].value", out int overrideScore) && overrideScore > 0;
        int computedScore = hasOverride ? overrideScore : baseScore + bonusScore;
        value = computedScore.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// Determines whether a long-range value should be suppressed because it matches range.
    /// </summary>
    private static bool ShouldSuppressLongRange(JsonElement root, string keyPath, JsonElement longRangeValue)
    {
        if (!IsLongRangePath(keyPath))
        {
            return false;
        }

        string rangePath = keyPath[..^"longRange".Length] + "range";
        return TryResolveJsonPath(root, rangePath, out JsonElement rangeValue)
               && JsonValueSemanticallyEquals(longRangeValue, rangeValue);
    }

    /// <summary>
    /// Determines whether a key path refers to a long-range property.
    /// </summary>
    private static bool IsLongRangePath(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        return string.Equals(keyPath, "longRange", StringComparison.OrdinalIgnoreCase) ||
               keyPath.EndsWith(".longRange", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares two JSON values for semantic equality across numeric and string representations.
    /// </summary>
    private static bool JsonValueSemanticallyEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
        {
            if (left.TryGetDecimal(out decimal leftDecimal) && right.TryGetDecimal(out decimal rightDecimal))
            {
                return leftDecimal == rightDecimal;
            }

            return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
        }

        if (left.ValueKind == JsonValueKind.String || right.ValueKind == JsonValueKind.String)
        {
            return string.Equals(left.ToString().Trim(), right.ToString().Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tries to resolve an integer value at a JSON path.
    /// </summary>
    private static bool TryResolveIntegerPath(JsonElement root, string path, out int value)
    {
        value = 0;
        if (!TryResolveJsonPath(root, path, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int numeric))
        {
            value = numeric;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to resolve a JSON element using dotted path syntax with array index support.
    /// </summary>
    private static bool TryResolveJsonPath(JsonElement root, string keyPath, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        string[] segments = keyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (string segment in segments)
        {
            if (!TryResolveJsonPathSegment(value, segment, out JsonElement resolvedSegment))
            {
                value = default;
                return false;
            }

            value = resolvedSegment;
        }

        return true;
    }

    /// <summary>
    /// Tries to resolve a single JSON path segment, including bracketed array indices.
    /// </summary>
    private static bool TryResolveJsonPathSegment(JsonElement current, string segment, out JsonElement value)
    {
        value = current;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        int bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
        string propertyName = bracketIndex >= 0 ? segment[..bracketIndex] : segment;

        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out JsonElement nestedProperty))
            {
                return false;
            }

            value = nestedProperty;
        }

        int cursor = bracketIndex;
        while (cursor >= 0)
        {
            int closeOffset = segment[(cursor + 1)..].IndexOf(']', StringComparison.Ordinal);
            if (closeOffset < 0)
            {
                return false;
            }

            int closeIndex = cursor + 1 + closeOffset;

            string rawIndex = segment[(cursor + 1)..closeIndex];
            if (!int.TryParse(rawIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                return false;
            }

            if (value.ValueKind != JsonValueKind.Array || index < 0 || index >= value.GetArrayLength())
            {
                return false;
            }

            value = value[index];
            int nextOpenBracketOffset = segment[(closeIndex + 1)..].IndexOf('[', StringComparison.Ordinal);
            cursor = nextOpenBracketOffset < 0 ? -1 : closeIndex + 1 + nextOpenBracketOffset;
        }

        return true;
    }

    /// <summary>
    /// Gets the default template body for a category name when one is registered.
    /// </summary>
    private static string? GetDefaultCategoryTemplate(string? categoryName)
    {
        return new DefaultCategoryTemplates().TryGetTemplate(categoryName);
    }

    /// <summary>
    /// Parses markdown into front matter and body content.
    /// </summary>
    private static ParsedMarkdown ParseMarkdownDocument(string raw)
    {
        Match fenced = MarkdownFrontMatterFencedRegex().Match(raw);
        if (!fenced.Success)
        {
            Match loose = MarkdownFrontMatterLooseRegex().Match(raw);
            if (!loose.Success || !LooksLikeLooseFrontMatter(loose.Groups["yaml"].Value))
            {
                return new([], raw);
            }

            fenced = loose;
        }

        string yamlSegment = fenced.Groups["yaml"].Value.Trim();
        string body = fenced.Groups["body"].Success ? fenced.Groups["body"].Value : string.Empty;
        Dictionary<string, string> frontMatter = [];

        object deserialized;
        try
        {
            deserialized = YamlDeserializer.Deserialize<object>(yamlSegment);
        }
        catch (YamlException)
        {
            return new([], raw);
        }

        if (deserialized is not Dictionary<object, object> map)
            return new(frontMatter, body);

        foreach ((object key, object value) in map)
        {
            frontMatter[Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return new(frontMatter, body);
    }

    /// <summary>
    /// Determines whether loose YAML text resembles markdown front matter.
    /// </summary>
    private static bool LooksLikeLooseFrontMatter(string yamlSegment)
    {
        string[] lines = yamlSegment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        bool hasTopLevelKey = false;
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (LooseFrontMatterKeyRegex.IsMatch(trimmed))
            {
                hasTopLevelKey = true;
                continue;
            }

            if (char.IsWhiteSpace(line[0]) || trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return hasTopLevelKey;
    }
}
