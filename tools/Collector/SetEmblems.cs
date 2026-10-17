using System.Text.Json;

namespace Collector;

// A set is drawn with the set's own emblem (as the game and dfmax do), not with a piece of its gear. Each of the 12 gear sets has one
// emblem, and each oath set uses the emblem of the gear set it is themed with.
static class SetEmblems
{
    // dfmax's Korean gear set names -> the set family name equipment responses use (without " Set").
    public static readonly Dictionary<string, string> KoreanFamilies = new()
    {
        ["칠흑의 정화 세트"] = "Cleansing Darkness",
        ["소울 페어리 세트"] = "Soul Fairy",
        ["용투장의 난 세트"] = "Dragon Arena Uprising",
        ["압도적인 자연 세트"] = "Overwhelming Nature",
        ["영원히 이어지는 황금향 세트"] = "Hideout's Endless Gold",
        ["에테리얼 오브 아츠 세트"] = "Ethereal Orb Arts",
        ["무리 사냥의 길잡이 세트"] = "Alpha of the Pack Hunt",
        ["그림자에 숨은 죽음 세트"] = "Death in the Shadows",
        ["마력의 영역 세트"] = "Magic Domain",
        ["고대 전장의 발키리 세트"] = "Ancient Battlefield Valkyrie",
        ["세렌디피티 세트"] = "Serendipity",
        ["한계를 넘어선 에너지 세트"] = "Beyond Limit Energy",
    };

    // Oath set -> gear set family of the same theme. dfmax draws each oath set with exactly the emblem of its gear set (same image).
    public static readonly Dictionary<string, string> OathFamilies = new()
    {
        ["Cleansing Flawlessness Oath"] = "Cleansing Darkness",
        ["Fairy of Ages Oath"] = "Soul Fairy",
        ["Dragon Arena Ambition Oath"] = "Dragon Arena Uprising",
        ["Origin Nature Oath"] = "Overwhelming Nature",
        ["Utopian Gold Oath"] = "Hideout's Endless Gold",
        ["Fox of Harmony Oath"] = "Ethereal Orb Arts",
        ["Pack Solidarity Oath"] = "Alpha of the Pack Hunt",
        ["Shadow in the Darkness Oath"] = "Death in the Shadows",
        ["Magic Verge Oath"] = "Magic Domain",
        ["Valkyrie of Honor Oath"] = "Ancient Battlefield Valkyrie",
        ["Destiny Luck Oath"] = "Serendipity",
        ["Limit Mastery Oath"] = "Beyond Limit Energy",
    };

    // "Hideout's Endless Gold" -> "hideouts-endless-gold".
    internal static string Slug(string family) =>
        new string(family.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Replace(' ', '-');

    public static string FileName(string family) => $"set-{Slug(family)}.png";

    // Page set name (a tier of a family, such as "Paradise Found") -> its family; written by fetch-set-icons from the saved equipment.
    public static Dictionary<string, string> ReadFamilies(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))! : [];

    // Gives every set and oath-set name that has an emblem on disk the emblem as its icon; other names keep their piece icon.
    public static void Apply(Dictionary<string, Dictionary<string, string>> icons, Dictionary<string, HashSet<string>> names, string iconDir,
        IReadOnlyDictionary<string, string> familyOfSetName)
    {
        void Use(string category, string name, string? family)
        {
            if (family == null || !File.Exists(Path.Combine(iconDir, FileName(family)))) return;
            if (!icons.TryGetValue(category, out var byName)) icons[category] = byName = new();
            byName[name] = FileName(family);
        }
        foreach (var name in names.GetValueOrDefault("sets") ?? []) Use("sets", name, familyOfSetName.GetValueOrDefault(name, name));
        foreach (var name in names.GetValueOrDefault("oath") ?? []) Use("oath", name, OathFamilies.GetValueOrDefault(name));
    }
}
