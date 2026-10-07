namespace Collector;

// Maps API slot ids to in-game names (SUPPORT is Sub Equipment).
static class SlotNames
{
    static readonly Dictionary<string, string> Names = new()
    {
        ["WEAPON"] = "Weapon",
        ["TITLE"] = "Title",
        ["JACKET"] = "Top",
        ["SHOULDER"] = "Head/Shoulder",
        ["PANTS"] = "Bottom",
        ["SHOES"] = "Shoes",
        ["WAIST"] = "Belt",
        ["AMULET"] = "Necklace",
        ["WRIST"] = "Bracelet",
        ["RING"] = "Ring",
        ["SUPPORT"] = "Sub Equipment",
        ["MAGIC_STON"] = "Magic Stone",
        ["EARRING"] = "Earrings",
    };

    // Unknown ids pass through.
    public static string Display(string slotId) => Names.GetValueOrDefault(slotId, slotId);
}
