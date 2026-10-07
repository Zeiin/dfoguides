using System.Text.Json;
using System.Text.RegularExpressions;

namespace Collector;

static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    // For the website: no whitespace, nulls omitted.
    public static readonly JsonSerializerOptions Compact = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}

record SkillPick(string Skill, int Type);

// One anonymous character reduced to the choices that get counted.
record CharacterRecord(
    string Class,
    int Fame,
    string Set,
    string OathSet,
    string OathOption,
    string Weapon,
    string Title,
    int? TitleLevel,
    string[] DistinctSlots,
    string[] ExaltedSlots,
    int BlackFangCount,
    Dictionary<string, int> SkillLevels,
    SkillPick[] Vp,
    SkillPick[] Enhancement,
    string Creature,
    int? CreatureLevel,
    int? RareCloneWeaponLevel,                  // null unless a Rare Clone
    Dictionary<string, string> PlatinumBySlot,  // avatar slot id -> skill (JACKET top, PANTS bottom, AURORA aura)
    Dictionary<string, int> EmblemCounts);      // "Color|Effect" -> count

static partial class CharacterReader
{
    // The only Primeval items possible in these slots.
    internal static readonly Dictionary<string, string> ExaltedItems = new()
    {
        ["SUPPORT"] = "Disease Origin Plague Heart",
        ["MAGIC_STON"] = "Perfume of Graceful Elegance",
        ["EARRING"] = "Brilliant Weather Cube",
    };

    [GeneratedRegex(@"^(?<base>.*?)\s+Platinum\b.*?(?<level>\d+)")]
    private static partial Regex PlatinumTitle();

    // "[Lv. 80]", "[Lv.80]" or "[80Lv]" at the end of a name.
    [GeneratedRegex(@"^(?<base>.*?)\s*\[(?:Lv\.?\s*(?<numberAfterLv>\d+)|(?<numberBeforeLv>\d+)\s*Lv\.?)\]", RegexOptions.IgnoreCase)]
    private static partial Regex LevelMarker();

    // "Rare Clone Weapon Avatar [75Lv]" -> 75
    [GeneratedRegex(@"^Rare Clone Weapon Avatar.*?(?<level>\d+)")]
    private static partial Regex RareCloneWeapon();

    // requireSkills=false is for samples where skill files were not pulled.
    public static async Task<CharacterRecord?> ReadAsync(string dir, RosterEntry entry, string classKey, bool requireSkills = true)
    {
        async Task<T?> Load<T>(string suffix)
        {
            var path = Path.Combine(dir, $"{entry.CharacterId}_{suffix}.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Json.Options) : default;
        }

        var equipment = await Load<EquipmentResponse>("equipment");
        var oath = await Load<OathResponse>("oath");
        var skills = await Load<SkillResponse>("skill_style");
        var avatar = await Load<AvatarResponse>("avatar");
        var creature = await Load<CreatureResponse>("creature");
        // Gear is required; having no oath is still a valid record.
        if (equipment?.Equipment == null) return null;
        if (requireSkills && skills?.Skill?.Style == null) return null;

        var items = equipment.Equipment;
        var style = skills?.Skill?.Style ?? new SkillStyle(null, null, null, null);
        var allSkills = (style.Active ?? []).Concat(style.Passive ?? []).ToList();
        var nameById = allSkills.ToDictionary(s => s.SkillId, s => s.Name);
        string NameOf(string id) => nameById.GetValueOrDefault(id, id);

        var (title, titleLevel) = ParseTitle(items.FirstOrDefault(i => i.SlotId == "TITLE")?.ItemName ?? "(none)");
        var (creatureName, creatureLevel) = ParseTitle(creature?.Creature?.ItemName ?? "(none)");

        // Only Rare Clone weapon avatars count.
        var avatarSlots = avatar?.Avatar ?? [];
        var weaponAvatar = avatarSlots.FirstOrDefault(s => s.SlotId == "WEAPON")?.ItemName ?? "";

        var emblems = avatarSlots.SelectMany(s => s.Emblems ?? []).Where(e => !string.IsNullOrEmpty(e.ItemName)).ToList();
        // Platinum skill per avatar slot.
        var platinumBySlot = avatarSlots
            .Select(s => (s.SlotId, Skill: (s.Emblems ?? [])
                .Where(e => e.SlotColor == "Platinum" && !string.IsNullOrEmpty(e.ItemName))
                .Select(e => Emblems.PlatinumSkill(e.ItemName!))
                .FirstOrDefault(skill => skill != null)))
            .Where(x => x.Skill != null)
            .ToDictionary(x => x.SlotId, x => x.Skill!);
        var emblemCounts = emblems
            .Where(e => e.SlotColor != "Platinum" && !string.IsNullOrEmpty(e.SlotColor))
            .Select(e => (Color: e.SlotColor!, Effect: Emblems.Effect(e.ItemName!)))
            .Where(x => x.Effect != null)
            .GroupBy(x => $"{x.Color}|{x.Effect}")
            .ToDictionary(g => g.Key, g => g.Count());

        return new CharacterRecord(
            classKey,
            entry.Fame,
            SetNames.Display(equipment.SetItemInfo?.FirstOrDefault()?.SetItemName ?? "(none)"),
            oath?.Oath?.SetInfo?.SetName ?? "(none)",
            oath?.Oath?.SetInfo?.SetOptionName ?? "(none)",
            items.FirstOrDefault(i => i.SlotId == "WEAPON")?.ItemName ?? "(none)",
            title,
            titleLevel,
            items.Where(i => i.ItemName.Contains("Distinct")).Select(i => i.SlotId).ToArray(),
            items.Where(i => ExaltedItems.TryGetValue(i.SlotId, out var n) && n == i.ItemName).Select(i => i.SlotId).ToArray(),
            items.Count(i => (i.SlotId is "AMULET" or "WRIST" or "RING") && i.ItemName.StartsWith("Black Fang:")),
            allSkills.Where(s => s.Level > 0).ToDictionary(s => s.Name, s => s.Level),
            (style.Evolution ?? []).Select(e => new SkillPick(NameOf(e.SkillId), e.Type)).ToArray(),
            (style.Enhancement ?? []).Select(e => new SkillPick(NameOf(e.SkillId), e.Type)).ToArray(),
            creatureName,
            creatureLevel,
            RareCloneLevel(weaponAvatar),
            platinumBySlot,
            emblemCounts);
    }

    // -> level, or null for any other weapon avatar.
    internal static int? RareCloneLevel(string avatarName)
    {
        var m = RareCloneWeapon().Match(avatarName);
        return m.Success ? int.Parse(m.Groups["level"].Value) : null;
    }

    // A level in the name means platinum even without the word; rebuilds the name as "<base> Platinum" with the level separate.
    internal static (string Title, int? Level) ParseTitle(string name)
    {
        var withWord = PlatinumTitle().Match(name);
        if (withWord.Success)
            return ($"{withWord.Groups["base"].Value} Platinum", int.Parse(withWord.Groups["level"].Value));

        var bracketed = LevelMarker().Match(name);
        if (!bracketed.Success) return (name, null);

        var level = bracketed.Groups["numberAfterLv"].Success ? bracketed.Groups["numberAfterLv"].Value : bracketed.Groups["numberBeforeLv"].Value;
        return ($"{bracketed.Groups["base"].Value.Trim()} Platinum", int.Parse(level));
    }
}
