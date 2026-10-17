using System.Text.Json;

namespace Collector;

// One character as the last refresh left it. Fame belongs to the snapshot on disk, Checked is the day it was last pulled or re-examined.
// KeptOriginal marks a snapshot kept on purpose (see Refresh.KeepOriginal); SeenFame is the lower fame the character had at that point.
record CharState(string ServerId, int Fame, string Checked, bool KeptOriginal = false, int SeenFame = 0);

// Rules of the incremental refresh: which characters to pull today, and which new snapshots to refuse.
static class Refresh
{
    internal const int StaleDays = 7;
    const string DateFormat = "yyyy-MM-dd";

    internal static string Day(DateOnly day) => day.ToString(DateFormat);

    internal static int AgeDays(string checkedDay, DateOnly today) =>
        today.DayNumber - DateOnly.ParseExact(checkedDay, DateFormat).DayNumber;

    // New characters and characters whose fame changed are always pulled. Of the rest, the oldest 1/StaleDays go each day (and anything older
    // than StaleDays), so every snapshot is refreshed about once a week.
    internal static List<RosterEntry> Select(IReadOnlyList<RosterEntry> roster, IReadOnlyDictionary<string, CharState> state, DateOnly today)
    {
        var pull = new List<RosterEntry>();
        var unchanged = new List<(RosterEntry Entry, int Age)>();
        foreach (var entry in roster)
        {
            if (!state.TryGetValue(entry.CharacterId, out var known)) { pull.Add(entry); continue; }
            var changed = known.KeptOriginal ? entry.Fame != known.SeenFame : entry.Fame != known.Fame;
            if (changed) pull.Add(entry);
            else unchanged.Add((entry, AgeDays(known.Checked, today)));
        }

        var daily = (int)Math.Ceiling(unchanged.Count / (double)StaleDays);
        var take = Math.Max(daily, unchanged.Count(x => x.Age >= StaleDays));
        pull.AddRange(unchanged.Where(x => x.Age >= 1).OrderByDescending(x => x.Age).Take(take).Select(x => x.Entry));
        return pull;
    }

    // Magic Domain (gear set or oath) is worn to farm faster, not to run content. A character that dropped in fame by switching into it keeps its
    // original snapshot.
    internal static bool KeepOriginal(int oldFame, int newFame, bool oldWearsMagicDomain, bool newWearsMagicDomain) =>
        newFame < oldFame && newWearsMagicDomain && !oldWearsMagicDomain;

    // familyOfSetName maps a page set name to its set family (reference/setfamilies.json).
    internal static bool WearsMagicDomain(string setName, string oathSet, IReadOnlyDictionary<string, string> familyOfSetName) =>
        familyOfSetName.GetValueOrDefault(setName, setName) == "Magic Domain" || oathSet == "Magic Verge Oath";

    // The set and oath set a saved snapshot wears, named as the statistics name them.
    internal static (string Set, string Oath) ReadWear(string dir, string characterId)
    {
        T? Load<T>(string suffix)
        {
            var path = Path.Combine(dir, $"{characterId}_{suffix}.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Options) : default;
        }
        var equipment = Load<EquipmentResponse>("equipment");
        var oath = Load<OathResponse>("oath");
        return (SetNames.Of(equipment), oath?.Oath?.SetInfo?.SetName ?? "(none)");
    }
}

// The refresh state of every class: class key -> character id -> state (data/raw/refresh_state.json).
static class RefreshState
{
    public static Dictionary<string, Dictionary<string, CharState>> Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, CharState>>>(File.ReadAllText(path), Json.Options)!
            : [];

    // Write then rename so an interrupted run cannot leave a half file.
    public static void Save(string path, Dictionary<string, Dictionary<string, CharState>> state)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json.Options));
        File.Move(temp, path, overwrite: true);
    }
}
