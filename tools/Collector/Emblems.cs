using System.Text.RegularExpressions;

namespace Collector;

// Parses emblem names, which differ across game eras ("Gold Red Emblem+ [STR]" vs "Gold Emblem Strength").
static partial class Emblems
{
    // Old names spell the effect out, new ones abbreviate it.
    static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = "STR",
        ["Intelligence"] = "INT",
        ["Spirit"] = "SPR",
        ["Vitality"] = "VIT",
        ["Physical Critical Chance"] = "Physical Critical",
        ["Magical Critical Chance"] = "Magical Critical",
    };

    // Everything up to "Emblem": metal tier and color, which are progression.
    [GeneratedRegex(@"^.*?\bEmblem\+?\s*")]
    private static partial Regex EmblemPrefix();

    [GeneratedRegex(@"\[(.+)\]")]
    private static partial Regex Bracket();

    // The effect of a non-platinum emblem, or null.
    internal static string? Effect(string itemName)
    {
        if (!itemName.Contains("Emblem")) return null;

        var rest = EmblemPrefix().Replace(itemName, "", 1).Trim();
        var bracket = Bracket().Match(rest);
        if (bracket.Success) rest = bracket.Groups[1].Value.Trim();
        if (rest.Length == 0) return null;

        var parts = rest.Split(" + ", StringSplitOptions.TrimEntries).Select(p => Synonyms.GetValueOrDefault(p, p));
        return string.Join(" + ", parts);
    }

    // The skill a platinum emblem boosts, or null if none is chosen.
    internal static string? PlatinumSkill(string itemName)
    {
        var m = Bracket().Match(itemName);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
