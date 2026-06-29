using System.Text.Json;
using System.Text;
using Humanizer;
using System.Globalization;
using System.Collections.Immutable;

namespace Grimoire.Core;

/// <summary>
/// Represents a formatter that converts JSON element values into human-readable text for rendering and indexing.
/// </summary>
internal sealed class JsonValueFormatter
{
    /// <summary>
    /// An <see cref="ImmutableDictionary{TKey, TValue}"/> representing challenge-rating identifier mappings to display labels.
    /// </summary>
    private readonly ImmutableDictionary<int, string> _creatureChallengeRatings = ImmutableDictionary.CreateRange(new Dictionary<int, string>
    {
        [1] = "0",
        [2] = "1/8",
        [3] = "1/4",
        [4] = "1/2",
        [5] = "1",
        [6] = "2",
        [7] = "3",
        [8] = "4",
        [9] = "5",
        [10] = "6",
        [11] = "7",
        [12] = "8",
        [13] = "9",
        [14] = "10",
        [15] = "11",
        [16] = "12",
        [17] = "13",
        [18] = "14",
        [19] = "15",
        [20] = "16",
        [21] = "17",
        [22] = "18",
        [23] = "19",
        [24] = "20",
        [25] = "21",
        [26] = "22",
        [27] = "23",
        [29] = "24",
        [30] = "25",
        [31] = "26",
        [32] = "27",
        [33] = "28",
        [34] = "29",
        [35] = "30",
    });

    /// <summary>
    /// An <see cref="ImmutableDictionary{TKey, TValue}"/> representing creature-size identifier mappings to display labels.
    /// </summary>
    private readonly ImmutableDictionary<int, string> _creatureSizes = ImmutableDictionary.CreateRange(new Dictionary<int, string>
    {
        [2] = "Tiny",
        [3] = "Small",
        [4] = "Medium",
        [5] = "Large",
        [6] = "Huge",
        [7] = "Gargantuan",
        [10] = "Medium or Small",
    });

    /// <summary>
    /// An <see cref="ImmutableDictionary{TKey, TValue}"/> representing creature-type identifier mappings to display labels.
    /// </summary>
    private readonly ImmutableDictionary<int, string> _creatureTypes = ImmutableDictionary.CreateRange(new Dictionary<int, string>
    {
        [1] = "Aberration",
        [2] = "Beast",
        [3] = "Celestial",
        [4] = "Construct",
        [6] = "Dragon",
        [7] = "Elemental",
        [8] = "Fey",
        [9] = "Fiend",
        [10] = "Giant",
        [11] = "Humanoid",
        [13] = "Monstrosity",
        [14] = "Ooze",
        [15] = "Plant",
        [16] = "Undead",
    });

    /// <summary>
    /// An <see cref="ImmutableDictionary{TKey, TValue}"/> representing alignment identifier mappings to display labels.
    /// </summary>
    private readonly ImmutableDictionary<int, string> _creatureAlignments = ImmutableDictionary.CreateRange(new Dictionary<int, string>
    {
        [1] = "Lawful Good",
        [2] = "Neutral Good",
        [3] = "Chaotic Good",
        [4] = "Lawful Neutral",
        [5] = "Neutral",
        [6] = "Chaotic Neutral",
        [7] = "Lawful Evil",
        [8] = "Neutral Evil",
        [9] = "Chaotic Evil",
        [10] = "Unaligned",
        [11] = "Any Alignment",
        [13] = "Any Evil Alignment",
        [14] = "Any Good Alignment",
        [15] = "Any Chaotic Alignment",
        [16] = "Any Lawful Alignment",
        [18] = "Any Non-Good Alignment",
        [19] = "Any Non-Lawful Alignment",
        [20] = "Typically Chaotic Neutral",
        [21] = "Typically Neutral Good",
        [22] = "Typically Lawful Good",
        [23] = "Typically Chaotic Evil",
        [24] = "Typically Neutral Evil",
        [25] = "Typically Chaotic Good",
        [26] = "Typically Neutral",
        [27] = "Typically Lawful Evil",
        [28] = "Typically Lawful Neutral",
    });

    /// <summary>
    /// Formats a JSON value into display text and returns a <see cref="string"/> representing the rendered value.
    /// </summary>
    /// <param name="value">The JSON value representing data to render.</param>
    /// <param name="keyPath">The optional key path representing contextual formatting hints.</param>
    /// <param name="formatHint">The optional format hint representing forced formatting behavior.</param>
    /// <returns>A <see cref="string"/> representing the formatted display value.</returns>
    internal static string ToDisplayString(JsonElement value, string? keyPath = null, string? formatHint = null)
    {
        return new JsonValueFormatter().ToDisplayStringCore(value, keyPath, formatHint);
    }

    /// <summary>
    /// Formats a JSON value using contextual key and hint information and returns a <see cref="string"/> representing the rendered value.
    /// </summary>
    /// <param name="value">The JSON value representing data to render.</param>
    /// <param name="keyPath">The optional key path representing contextual formatting hints.</param>
    /// <param name="formatHint">The optional format hint representing forced formatting behavior.</param>
    /// <returns>A <see cref="string"/> representing the formatted display value.</returns>
    private string ToDisplayStringCore(JsonElement value, string? keyPath, string? formatHint)
    {
        if (TryGetFormatParameter(formatHint, "unit", out string? unitSuffix))
        {
            string raw = ToDisplayStringCore(value, keyPath, null);
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : $"{raw.Trim()} {unitSuffix}".Trim();
        }

        if (IsFormat(formatHint, "ddb-link"))
        {
            return FormatDndBeyondLink(value, "characters");
        }

        if (IsFormat(formatHint, "ddb-spell-link"))
        {
            return FormatDndBeyondLink(value, "spells");
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => FormatString(value.GetString(), keyPath),
            JsonValueKind.Number => FormatNumber(value, keyPath, formatHint),
            JsonValueKind.True => FormatBoolean(true, keyPath),
            JsonValueKind.False => FormatBoolean(false, keyPath),
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.Array => FormatArray(value, keyPath, formatHint),
            JsonValueKind.Object => FormatObject(value, keyPath, formatHint),
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// Normalizes entity-name text for display and returns a <see cref="string"/> representing normalized casing when appropriate.
    /// </summary>
    /// <param name="value">The raw text value representing an entity name candidate.</param>
    /// <param name="keyPath">The optional key path representing contextual field identity.</param>
    /// <returns>A <see cref="string"/> representing normalized entity text.</returns>
    internal static string NormalizeEntityText(string value, string? keyPath = null)
    {
        return NormalizeEntityTextCore(value, keyPath);
    }

    /// <summary>
    /// Applies all-caps normalization rules to entity text and returns a <see cref="string"/> representing normalized output.
    /// </summary>
    /// <param name="value">The raw text value representing an entity name candidate.</param>
    /// <param name="keyPath">The optional key path representing contextual field identity.</param>
    /// <returns>A <see cref="string"/> representing normalized entity text.</returns>
    private static string NormalizeEntityTextCore(string value, string? keyPath)
    {
        if (!ShouldNormalizeAllCapsEntity(value, keyPath))
        {
            return value;
        }

        string lowered = value.Trim().Transform(To.LowerCase);
        return lowered.Transform(To.TitleCase);
    }

    /// <summary>
    /// Formats a JSON string value and returns a <see cref="string"/> representing normalized display text.
    /// </summary>
    /// <param name="value">The JSON string value representing text to format.</param>
    /// <param name="keyPath">The optional key path representing contextual field identity.</param>
    /// <returns>A <see cref="string"/> representing formatted text output.</returns>
    private static string FormatString(string? value, string? keyPath)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizeEntityTextCore(value, keyPath);
    }

    /// <summary>
    /// Formats numeric JSON values with domain-specific mappings and returns a <see cref="string"/> representing rendered numeric output.
    /// </summary>
    /// <param name="value">The numeric JSON value representing data to format.</param>
    /// <param name="keyPath">The optional key path representing contextual field identity.</param>
    /// <param name="formatHint">The optional format hint representing forced formatting behavior.</param>
    /// <returns>A <see cref="string"/> representing formatted numeric output.</returns>
    private string FormatNumber(JsonElement value, string? keyPath, string? formatHint)
    {
        if (IsPathSegment(keyPath, "bundleSize")
            && value.TryGetInt32(out int bundleSize)
            && bundleSize <= 1 || IsPathSegment(keyPath, "weight")
            && value.TryGetDouble(out double weight)
            && weight <= 0)
        {
            return string.Empty;
        }

        if ((IsFormat(formatHint, "attack-type") || IsPathSegment(keyPath, "attackType")) &&
            value.TryGetInt32(out int attackType))
        {
            return attackType switch
            {
                1 => "Melee",
                2 => "Ranged",
                3 => "Melee or Ranged",
                _ => attackType.ToString(CultureInfo.InvariantCulture),
            };
        }

        if ((IsFormat(formatHint, "save-ability") || IsPathSegment(keyPath, "saveDcAbilityId")) &&
            value.TryGetInt32(out int saveAbilityId))
        {
            return saveAbilityId switch
            {
                1 => "Strength",
                2 => "Dexterity",
                3 => "Constitution",
                4 => "Intelligence",
                5 => "Wisdom",
                6 => "Charisma",
                _ => saveAbilityId.ToString(CultureInfo.InvariantCulture),
            };
        }

        if ((IsFormat(formatHint, "challenge-rating") || IsFormat(formatHint, "cr") || IsPathSegment(keyPath, "challengeRatingId")) &&
            value.TryGetInt32(out int challengeRatingId))
        {
            return _creatureChallengeRatings.TryGetValue(challengeRatingId, out string? challengeRating)
                ? challengeRating
                : challengeRatingId.ToString(CultureInfo.InvariantCulture);
        }

        if ((IsFormat(formatHint, "creature-size") || IsFormat(formatHint, "size") || IsPathSegment(keyPath, "sizeId")) &&
            value.TryGetInt32(out int sizeId))
        {
            return _creatureSizes.TryGetValue(sizeId, out string? size)
                ? size
                : sizeId.ToString(CultureInfo.InvariantCulture);
        }

        if ((IsFormat(formatHint, "creature-type") || IsFormat(formatHint, "type") || IsPathSegment(keyPath, "typeId")) &&
            value.TryGetInt32(out int typeId))
        {
            return _creatureTypes.TryGetValue(typeId, out string? creatureType)
                ? creatureType
                : typeId.ToString(CultureInfo.InvariantCulture);
        }

        if ((IsFormat(formatHint, "alignment") || IsPathSegment(keyPath, "alignmentId")) &&
            value.TryGetInt32(out int alignmentId))
        {
            return _creatureAlignments.TryGetValue(alignmentId, out string? alignment)
                ? alignment
                : alignmentId.ToString(CultureInfo.InvariantCulture);
        }

        if ((IsFormat(formatHint, "ordinal") || ShouldOrdinalizeLevel(keyPath)) && value.TryGetInt32(out int wholeNumber))
        {
            return wholeNumber.Ordinalize(CultureInfo.GetCultureInfo("en-US"));
        }

        return value.ToString();
    }

    /// <summary>
    /// Formats boolean JSON values with omission rules and returns a <see cref="string"/> representing rendered boolean output.
    /// </summary>
    /// <param name="value">The boolean value representing data to format.</param>
    /// <param name="keyPath">The optional key path representing contextual field identity.</param>
    /// <returns>A <see cref="string"/> representing formatted boolean output.</returns>
    private static string FormatBoolean(bool value, string? keyPath)
    {
        if (!value &&
            (IsPathSegment(keyPath, "isConsumable") ||
             IsPathSegment(keyPath, "consumable") ||
             IsPathSegment(keyPath, "canAttune") ||
             IsPathSegment(keyPath, "attunement") ||
             IsPathSegment(keyPath, "requiresAttackRoll") ||
             IsPathSegment(keyPath, "requiresSavingThrow") ||
             IsPathSegment(keyPath, "isLegendary") ||
             IsPathSegment(keyPath, "isMythic") ||
             IsPathSegment(keyPath, "hasLair") ||
             IsPathSegment(keyPath, "isHomebrew") ||
             IsPathSegment(keyPath, "isLegacy")))
        {
            return string.Empty;
        }

        return value ? "Yes" : "No";
    }

    /// <summary>
    /// Formats a JSON array by formatting each element and returns a <see cref="string"/> representing joined array output.
    /// </summary>
    /// <param name="value">The array value representing data to format.</param>
    /// <param name="keyPath">The optional key path representing contextual field identity.</param>
    /// <param name="formatHint">The optional format hint representing forced formatting behavior.</param>
    /// <returns>A <see cref="string"/> representing formatted array output.</returns>
    private static string FormatArray(JsonElement value, string? keyPath, string? formatHint)
    {
        if ((IsFormat(formatHint, "components") || IsPathSegment(keyPath, "components")) &&
        TryFormatComponents(value, out string components))
        {
            return components;
        }

        if ((IsFormat(formatHint, "higher-levels") || IsFormat(formatHint, "higherlevels") || IsPathSegment(keyPath, "atHigherLevels")) &&
        TryFormatAtHigherLevels(value, out string atHigherLevels))
        {
            return atHigherLevels;
        }

        List<string> parts = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            string formatted = ToDisplayString(item, keyPath, IsFormat(formatHint, "csv") ? null : formatHint);
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                parts.Add(formatted.Trim());
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Formats a JSON object by applying specialized object formatters and returns a <see cref="string"/> representing rendered object output.
    /// </summary>
    /// <param name="value">The object value representing data to format.</param>
    /// <param name="keyPath">The optional key path representing contextual field identity.</param>
    /// <param name="formatHint">The optional format hint representing forced formatting behavior.</param>
    /// <returns>A <see cref="string"/> representing formatted object output.</returns>
    private static string FormatObject(JsonElement value, string? keyPath, string? formatHint)
    {
        if (IsPathSegment(keyPath, "range") && TryFormatRange(value, out string range))
        {
            return range;
        }

        if (IsPathSegment(keyPath, "duration") && TryFormatDuration(value, out string duration))
        {
            return duration;
        }

        if (IsFormat(formatHint, "damage"))
        {
            return TryFormatDamage(value, out string forcedDamage) ? forcedDamage : string.Empty;
        }

        if (TryFormatDamage(value, out string damage))
        {
            return damage;
        }

        if (TryGetPropertyIgnoreCase(value, "name", out JsonElement name))
        {
            string formattedName = ToDisplayString(name, keyPath, formatHint);
            if (!string.IsNullOrWhiteSpace(formattedName))
            {
                return formattedName;
            }
        }

        if (TryGetPropertyIgnoreCase(value, "title", out JsonElement title))
        {
            string formattedTitle = ToDisplayString(title, keyPath, formatHint);
            if (!string.IsNullOrWhiteSpace(formattedTitle))
            {
                return formattedTitle;
            }
        }

        List<string> properties = [];
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string formattedProperty = ToDisplayString(property.Value, property.Name, formatHint);
            if (string.IsNullOrWhiteSpace(formattedProperty))
            {
                continue;
            }

            properties.Add($"{ToDisplayLabel(property.Name)} {formattedProperty}");
        }

        return string.Join("; ", properties);
    }

    /// <summary>
    /// Attempts to format a damage object and returns a <see cref="bool"/> indicating whether a damage string was produced.
    /// </summary>
    /// <param name="value">The object value representing potential damage fields.</param>
    /// <param name="formatted">The formatted damage string representing rendered output when formatting succeeds.</param>
    /// <returns><see langword="true"/> indicating damage formatting succeeded; otherwise, <see langword="false"/>.</returns>
    private static bool TryFormatDamage(JsonElement value, out string formatted)
    {
        formatted = string.Empty;
        if (!TryGetPropertyIgnoreCase(value, "diceString", out JsonElement dice) &&
            !TryGetPropertyIgnoreCase(value, "dice", out dice))
        {
            return false;
        }

        string diceText = ToDisplayString(dice, "diceString");
        if (string.IsNullOrWhiteSpace(diceText))
        {
            return false;
        }

        string damageTypeText = string.Empty;
        if (TryGetPropertyIgnoreCase(value, "damageType", out JsonElement damageType))
        {
            damageTypeText = ToDisplayString(damageType, "damageType");
        }

        formatted = string.IsNullOrWhiteSpace(damageTypeText)
            ? diceText.Trim()
            : $"{diceText.Trim()} {damageTypeText.Trim()}";
        return true;
    }

    /// <summary>
    /// Retrieves an object property with case-insensitive matching and returns a <see cref="bool"/> indicating whether a property was found.
    /// </summary>
    /// <param name="value">The object value representing the property source.</param>
    /// <param name="propertyName">The property name representing the requested field.</param>
    /// <param name="propertyValue">The JSON value representing the matched property when lookup succeeds.</param>
    /// <returns><see langword="true"/> indicating the property was found; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetPropertyIgnoreCase(JsonElement value, string propertyName, out JsonElement propertyValue)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            propertyValue = default;
            return false;
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                propertyValue = property.Value;
                return true;
            }
        }

        propertyValue = default;
        return false;
    }

    /// <summary>
    /// Attempts to format a spell range object and returns a <see cref="bool"/> indicating whether a range string was produced.
    /// </summary>
    /// <param name="value">The object value representing range metadata.</param>
    /// <param name="formatted">The formatted range string representing rendered output when formatting succeeds.</param>
    /// <returns><see langword="true"/> indicating range formatting succeeded; otherwise, <see langword="false"/>.</returns>
    private static bool TryFormatRange(JsonElement value, out string formatted)
    {
        formatted = string.Empty;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string origin = TryGetPropertyIgnoreCase(value, "origin", out JsonElement originElement) ? originElement.ToString().Trim() : string.Empty;
        int rangeValue = TryGetPropertyIgnoreCase(value, "rangeValue", out JsonElement rangeValueElement) &&
            rangeValueElement.ValueKind == JsonValueKind.Number &&
            rangeValueElement.TryGetInt32(out int parsedRange)
            ? parsedRange
            : 0;
        string aoeType = TryGetPropertyIgnoreCase(value, "aoeType", out JsonElement aoeTypeElement) ? aoeTypeElement.ToString().Trim() : string.Empty;
        int aoeValue = TryGetPropertyIgnoreCase(value, "aoeValue", out JsonElement aoeValueElement) &&
            aoeValueElement.ValueKind == JsonValueKind.Number &&
            aoeValueElement.TryGetInt32(out int parsedAoe)
            ? parsedAoe
            : 0;

        string baseRange = string.Empty;
        if (!string.IsNullOrWhiteSpace(origin) && !string.Equals(origin, "Ranged", StringComparison.OrdinalIgnoreCase) && rangeValue <= 0)
        {
            baseRange = origin;
        }
        else if (rangeValue > 0)
        {
            baseRange = $"{rangeValue} ft.";
            if (!string.IsNullOrWhiteSpace(origin) && !string.Equals(origin, "Ranged", StringComparison.OrdinalIgnoreCase))
            {
                baseRange = $"{origin} ({baseRange})";
            }
        }

        string area = aoeValue > 0 && !string.IsNullOrWhiteSpace(aoeType)
            ? $"{aoeValue} ft. {aoeType}"
            : string.Empty;

        if (string.IsNullOrWhiteSpace(baseRange) && string.IsNullOrWhiteSpace(area))
        {
            return false;
        }

        formatted = string.IsNullOrWhiteSpace(area)
            ? baseRange
            : string.IsNullOrWhiteSpace(baseRange)
                ? area
                : $"{baseRange} ({area})";
        return true;
    }

    /// <summary>
    /// Attempts to format a spell duration object and returns a <see cref="bool"/> indicating whether a duration string was produced.
    /// </summary>
    /// <param name="value">The object value representing duration metadata.</param>
    /// <param name="formatted">The formatted duration string representing rendered output when formatting succeeds.</param>
    /// <returns><see langword="true"/> indicating duration formatting succeeded; otherwise, <see langword="false"/>.</returns>
    private static bool TryFormatDuration(JsonElement value, out string formatted)
    {
        formatted = string.Empty;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string durationType = TryGetPropertyIgnoreCase(value, "durationType", out JsonElement durationTypeElement)
            ? durationTypeElement.ToString().Trim()
            : string.Empty;
        int interval = TryGetPropertyIgnoreCase(value, "durationInterval", out JsonElement intervalElement) &&
            intervalElement.ValueKind == JsonValueKind.Number &&
            intervalElement.TryGetInt32(out int parsedInterval)
            ? parsedInterval
            : 0;
        string unit = TryGetPropertyIgnoreCase(value, "durationUnit", out JsonElement unitElement)
            ? unitElement.ToString().Trim()
            : string.Empty;

        if (string.Equals(durationType, "Instantaneous", StringComparison.OrdinalIgnoreCase))
        {
            formatted = "Instantaneous";
            return true;
        }

        string timeText = string.Empty;
        if (interval > 0 && !string.IsNullOrWhiteSpace(unit))
        {
            string normalizedUnit = interval == 1 ? unit : unit.EndsWith('s') ? unit : unit + "s";
            timeText = $"{interval} {normalizedUnit}";
        }

        if (string.Equals(durationType, "Concentration", StringComparison.OrdinalIgnoreCase))
        {
            formatted = string.IsNullOrWhiteSpace(timeText)
                ? "Concentration"
                : $"Concentration, up to {timeText}";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(timeText))
        {
            formatted = timeText;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(durationType))
        {
            formatted = durationType;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a key path matches a target segment and returns a <see cref="bool"/> indicating segment presence.
    /// </summary>
    /// <param name="keyPath">The key path representing contextual field identity.</param>
    /// <param name="segment">The target segment representing the field to match.</param>
    /// <returns><see langword="true"/> indicating the segment matches the path; otherwise, <see langword="false"/>.</returns>
    private static bool IsPathSegment(string? keyPath, string segment)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        if (string.Equals(keyPath, segment, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return keyPath.EndsWith($".{segment}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether entity text should be normalized from all-caps form and returns a <see cref="bool"/> indicating normalization eligibility.
    /// </summary>
    /// <param name="value">The entity text representing candidate display content.</param>
    /// <param name="keyPath">The key path representing contextual field identity.</param>
    /// <returns><see langword="true"/> indicating all-caps normalization should be applied; otherwise, <see langword="false"/>.</returns>
    private static bool ShouldNormalizeAllCapsEntity(string value, string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IsEntityPath(keyPath))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        int letterCount = 0;
        foreach (char c in trimmed)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letterCount++;
            if (char.IsLower(c))
            {
                return false;
            }
        }

        if (letterCount == 0)
        {
            return false;
        }

        bool isSingleToken = trimmed.IndexOfAny([' ', '-', '_']) < 0;
        if (isSingleToken && letterCount <= 3)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a key path identifies an entity-name field and returns a <see cref="bool"/> indicating entity-field membership.
    /// </summary>
    /// <param name="keyPath">The key path representing contextual field identity.</param>
    /// <returns><see langword="true"/> indicating the path targets an entity-name field; otherwise, <see langword="false"/>.</returns>
    private static bool IsEntityPath(string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        return IsPathSegment(keyPath, "name") ||
               IsPathSegment(keyPath, "title") ||
               IsPathSegment(keyPath, "fullName");
    }

    /// <summary>
    /// Compares a format hint to an expected value and returns a <see cref="bool"/> indicating whether the hint matches.
    /// </summary>
    /// <param name="formatHint">The format hint representing caller-specified formatting behavior.</param>
    /// <param name="expected">The expected format token representing a target format.</param>
    /// <returns><see langword="true"/> indicating the format hint matches; otherwise, <see langword="false"/>.</returns>
    private static bool IsFormat(string? formatHint, string expected)
    {
        if (string.IsNullOrWhiteSpace(formatHint))
        {
            return false;
        }

        return string.Equals(formatHint.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts a parameterized format value and returns a <see cref="bool"/> indicating whether the parameter was present.
    /// </summary>
    /// <param name="formatHint">The format hint representing caller-specified formatting behavior.</param>
    /// <param name="formatName">The format name representing the expected parameterized prefix.</param>
    /// <param name="parameter">The parameter string representing extracted format arguments when parsing succeeds.</param>
    /// <returns><see langword="true"/> indicating parameter extraction succeeded; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetFormatParameter(string? formatHint, string formatName, out string? parameter)
    {
        parameter = null;
        if (string.IsNullOrWhiteSpace(formatHint))
        {
            return false;
        }

        string expectedPrefix = formatName + ":";
        if (!formatHint.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        parameter = formatHint[expectedPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(parameter);
    }

    /// <summary>
    /// Formats a D&amp;D Beyond entity identifier as markdown link text and returns a <see cref="string"/> representing hyperlink output.
    /// </summary>
    /// <param name="value">The JSON value representing an entity identifier source.</param>
    /// <param name="entityPathSegment">The URL path segment representing the D&amp;D Beyond entity type.</param>
    /// <returns>A <see cref="string"/> representing a markdown hyperlink for the entity identifier.</returns>
    private static string FormatDndBeyondLink(JsonElement value, string entityPathSegment)
    {
        string raw = value.ValueKind switch
        {
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string id = string.Concat(raw.Where(char.IsDigit));
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        return $"[{id}](https://www.dndbeyond.com/{entityPathSegment}/{id})";
    }

    /// <summary>
    /// Determines whether a key path should be ordinalized and returns a <see cref="bool"/> indicating ordinal formatting eligibility.
    /// </summary>
    /// <param name="keyPath">The key path representing contextual field identity.</param>
    /// <returns><see langword="true"/> indicating ordinal formatting should be applied; otherwise, <see langword="false"/>.</returns>
    private static bool ShouldOrdinalizeLevel(string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        return IsPathSegment(keyPath, "level") || IsPathSegment(keyPath, "slotLevel");
    }

    /// <summary>
    /// Attempts to format spell components and returns a <see cref="bool"/> indicating whether component text was produced.
    /// </summary>
    /// <param name="value">The array value representing component entries.</param>
    /// <param name="formatted">The formatted component string representing rendered output when formatting succeeds.</param>
    /// <returns><see langword="true"/> indicating component formatting succeeded; otherwise, <see langword="false"/>.</returns>
    private static bool TryFormatComponents(JsonElement value, out string formatted)
    {
        formatted = string.Empty;
        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        List<string> components = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            string? token = item.ValueKind switch
            {
                JsonValueKind.Number when item.TryGetInt32(out int numberValue) => MapComponentCode(numberValue),
                JsonValueKind.String => MapComponentToken(item.GetString()),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(token) && !components.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                components.Add(token);
            }
        }

        if (components.Count == 0)
        {
            return false;
        }

        formatted = string.Join(", ", components);
        return true;
    }

    /// <summary>
    /// Maps a numeric component code and returns a <see cref="string"/> representing canonical component shorthand.
    /// </summary>
    /// <param name="code">The numeric component code representing verbal, somatic, or material components.</param>
    /// <returns>A <see cref="string"/> representing the component shorthand, or <see langword="null"/> when the code is unknown.</returns>
    private static string? MapComponentCode(int code)
    {
        return code switch
        {
            1 => "V",
            2 => "S",
            3 => "M",
            _ => null,
        };
    }

    /// <summary>
    /// Maps a textual component token and returns a <see cref="string"/> representing canonical component shorthand.
    /// </summary>
    /// <param name="token">The component token representing textual or numeric component input.</param>
    /// <returns>A <see cref="string"/> representing the component shorthand, or <see langword="null"/> when the token is unknown.</returns>
    private static string? MapComponentToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string normalized = token.Trim();
        if (int.TryParse(normalized, out int numeric))
        {
            return MapComponentCode(numeric);
        }

        if (string.Equals(normalized, "verbal", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "v", StringComparison.OrdinalIgnoreCase))
        {
            return "V";
        }

        if (string.Equals(normalized, "somatic", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "s", StringComparison.OrdinalIgnoreCase))
        {
            return "S";
        }

        if (string.Equals(normalized, "material", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "m", StringComparison.OrdinalIgnoreCase))
        {
            return "M";
        }

        return null;
    }

    /// <summary>
    /// Attempts to format higher-level spell text and returns a <see cref="bool"/> indicating whether formatted output was produced.
    /// </summary>
    /// <param name="value">The array value representing higher-level entries.</param>
    /// <param name="formatted">The formatted higher-level string representing rendered output when formatting succeeds.</param>
    /// <returns><see langword="true"/> indicating higher-level formatting succeeded; otherwise, <see langword="false"/>.</returns>
    private static bool TryFormatAtHigherLevels(JsonElement value, out string formatted)
    {
        formatted = string.Empty;
        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        List<string> sentences = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            string sentence = BuildHigherLevelSentence(item);
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                sentences.Add(sentence);
            }
        }

        if (sentences.Count == 0)
        {
            return false;
        }

        formatted = $"At Higher Levels: {string.Join(" ", sentences)}";
        return true;
    }

    /// <summary>
    /// Builds a sentence from a higher-level entry and returns a <see cref="string"/> representing normalized sentence output.
    /// </summary>
    /// <param name="value">The higher-level entry representing scalar or object content.</param>
    /// <returns>A <see cref="string"/> representing a sentence describing the higher-level effect.</returns>
    private static string BuildHigherLevelSentence(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            string scalar = ToDisplayString(value);
            if (string.IsNullOrWhiteSpace(scalar))
            {
                return string.Empty;
            }

            return EnsureSentence(scalar.Trim());
        }

        string? level = null;
        if (TryGetPropertyIgnoreCase(value, "level", out JsonElement levelElement))
        {
            level = ToDisplayString(levelElement, "level", "ordinal");
        }
        else if (TryGetPropertyIgnoreCase(value, "slotLevel", out JsonElement slotLevelElement))
        {
            level = ToDisplayString(slotLevelElement, "slotLevel", "ordinal");
        }

        List<string> parts = [];
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (string.Equals(property.Name, "level", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, "slotLevel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string propertyValue = ToDisplayString(property.Value, property.Name);
            if (string.IsNullOrWhiteSpace(propertyValue))
            {
                continue;
            }

            parts.Add($"{ToDisplayLabel(property.Name)} {propertyValue.Trim()}");
        }

        string sentence = string.Empty;
        switch (string.IsNullOrWhiteSpace(level))
        {
            case false when parts.Count > 0:
                sentence = $"At level {level!.Trim()}, {string.Join("; ", parts)}";
                break;
            case false:
                sentence = $"At level {level!.Trim()}";
                break;
            default:
            {
                if (parts.Count > 0)
                {
                    sentence = string.Join("; ", parts);
                }

                break;
            }
        }

        return EnsureSentence(sentence);
    }

    /// <summary>
    /// Ensures a text fragment ends with terminal punctuation and returns a <see cref="string"/> representing normalized sentence text.
    /// </summary>
    /// <param name="value">The value representing text to normalize as a sentence.</param>
    /// <returns>A <see cref="string"/> representing sentence text with terminal punctuation.</returns>
    private static string EnsureSentence(string value)
    {
        string trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        char last = trimmed[^1];
        if (last is '.' or '!' or '?')
        {
            return trimmed;
        }

        return trimmed + ".";
    }

    /// <summary>
    /// Converts a property token into a human-readable label and returns a <see cref="string"/> representing display text.
    /// </summary>
    /// <param name="value">The value representing a raw property name token.</param>
    /// <returns>A <see cref="string"/> representing a display-friendly label.</returns>
    private static string ToDisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length + 8);
        bool previousWasLowerOrDigit = false;
        foreach (char c in value)
        {
            if (c == '_' || c == '-')
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                previousWasLowerOrDigit = false;
                continue;
            }

            bool shouldInsertSpace = char.IsUpper(c) && previousWasLowerOrDigit;
            if (shouldInsertSpace)
            {
                builder.Append(' ');
            }

            builder.Append(builder.Length == 0 ? char.ToUpperInvariant(c) : c);
            previousWasLowerOrDigit = char.IsLower(c) || char.IsDigit(c);
        }

        return builder.ToString().Trim();
    }
}
