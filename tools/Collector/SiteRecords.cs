using System.Text.Json;

namespace Collector;

// Each class's characters for the website's filters, in a compact form: every string is stored once per class in a table and rows hold numbers.
// The page recomputes the statistics from these rows (stats/filter.js), so the row layout below is shared with that file.
static class SiteRecords
{
    sealed class StringTable
    {
        readonly Dictionary<string, int> _index = new();
        public List<string> Items { get; } = [];

        public int Id(string text)
        {
            if (_index.TryGetValue(text, out var id)) return id;
            _index[text] = id = Items.Count;
            Items.Add(text);
            return id;
        }
    }

    // Row: [fame, set, oathSet, oathOption, weapon, title, distinct[], exalted[], blackFang, skillLevels[skill, level, ...],
    //       vp[skill, type, ...], enhancement[skill, type, ...], vpSetKey, enhancementSetKey, creature, rareCloneLevel (-1 = none),
    //       platinum[slot, skill, ...], emblems[colorAndEffect, count, ...]]. Strings are indices into the table.
    internal static object[] Row(CharacterRecord c, Func<string, int> id) =>
    [
        c.Fame,
        id(c.Set),
        id(c.OathSet),
        id(c.OathOption),
        id(c.Weapon),
        id(c.TitleLevel is { } tl ? $"{c.Title} [{tl}]" : c.Title),
        c.DistinctSlots.Select(s => id(SlotNames.Display(s))).ToArray(),
        c.ExaltedSlots.Select(s => id(SlotNames.Display(s))).ToArray(),
        c.BlackFangCount,
        c.SkillLevels.SelectMany(kv => new[] { id(kv.Key), kv.Value }).ToArray(),
        c.Vp.SelectMany(v => new[] { id(v.Skill), v.Type }).ToArray(),
        c.Enhancement.SelectMany(e => new[] { id(e.Skill), e.Type }).ToArray(),
        id(string.Join(" + ", c.Vp.Select(v => $"{v.Skill} VP{v.Type}").Order())),
        id(string.Join(" + ", c.Enhancement.Select(e => $"{e.Skill}:{e.Type}").Order())),
        id(c.CreatureLevel is { } cl ? $"{c.Creature} [{cl}]" : c.Creature),
        c.RareCloneWeaponLevel ?? -1,
        c.PlatinumBySlot.SelectMany(kv => new[] { id(kv.Key), id(kv.Value) }).ToArray(),
        c.EmblemCounts.SelectMany(kv => new[] { id(kv.Key), kv.Value }).ToArray(),
    ];

    // Writes stats/data/records/<classKey>.js.
    public static async Task WriteAsync(string recordsDir, string classKey, IReadOnlyList<CharacterRecord> records)
    {
        var table = new StringTable();
        var rows = records.Select(c => Row(c, table.Id)).ToList();
        var json = JsonSerializer.Serialize(new { s = table.Items, r = rows });
        await File.WriteAllTextAsync(Path.Combine(recordsDir, $"{classKey}.js"),
            $"window.DFO_RECORDS=window.DFO_RECORDS||{{}};window.DFO_RECORDS[{JsonSerializer.Serialize(classKey)}]={json};");
    }
}
