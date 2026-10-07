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

    static string RemoveSuffix(this string text, string suffix) =>
        text.EndsWith(suffix) ? text[..^suffix.Length] : text;
}
