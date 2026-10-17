namespace Collector;

// The API reports a progression tier ("Cleansing Darkness: Balance Set"); this maps top-tier labels to the base set.
static class SetNames
{
    static readonly Dictionary<string, string> Families = new()
    {
        ["Cleansing Darkness: Balance Set"] = "Cleansing Darkness",
        ["Dragon Arena Emperor Set"] = "Dragon Arena",
        ["Death Plane of Existence Set"] = "Death in the Shadows",
        ["Nature - Cataclysm Set"] = "Overwhelming Nature",
        ["Overflowing Magic Domain Set"] = "Magic Domain",
        ["Fantastic Ethereal Orb Arts Set"] = "Ethereal Orb Arts",
        ["Phenomenal Serendipity Set"] = "Serendipity",
        ["Royal Fairy Set"] = "Soul Fairy",
        ["Valkyrie, Guide to Valhalla Set"] = "Ancient Battlefield Valkyrie",
        ["Pack Alpha Set"] = "Alpha of the Pack Hunt",
        ["Hideout's Endless Gold Set"] = "Hideout's Endless Gold",
        ["Beyond Limit Energy Set"] = "Beyond Limit Energy",
    };

    // Unknown labels pass through without the trailing " Set".
    public static string Display(string tierLabel) =>
        Families.TryGetValue(tierLabel, out var family) ? family : tierLabel.RemoveSuffix(" Set");

    // The set a character wears: the set name most of its pieces report. Every tier of one set ("Paradise Found", "Paradise of Dazzling Gold",
    // ... "Hideout's Endless Gold") has the same pieces and only renames its bonus, so the tier label must not split the statistics.
    public static string Of(EquipmentResponse? equipment)
    {
        var piece = equipment?.Equipment?.Select(i => i.SetItemName).Where(n => !string.IsNullOrEmpty(n)).GroupBy(n => n).MaxBy(g => g.Count())?.Key;
        if (piece != null) return piece.RemoveSuffix(" Set");
        var label = equipment?.SetItemInfo?.FirstOrDefault()?.SetItemName;
        return label == null ? "(none)" : Display(label);
    }

    static string RemoveSuffix(this string text, string suffix) =>
        text.EndsWith(suffix) ? text[..^suffix.Length] : text;
}
