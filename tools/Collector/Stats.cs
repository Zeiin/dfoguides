namespace Collector;

record RateRow(string Name, int Count, double Percent);
record SkillRow(string Skill, double TakePercent, double AverageLevelAll, double AverageLevelTakers);

record ClassStats(
    int Sample,
    List<RateRow> Sets,
    int OathSample,
    List<RateRow> OathSets,
    List<RateRow> OathSetOptions,
    List<RateRow> Weapons,
    List<RateRow> Titles,
    List<RateRow> DistinctBySlot,
    double AnyDistinctPercent,
    List<RateRow> Exalted,
    int BlackFangSample,                  // excludes 1 or 2 pieces
    double BlackFangAveragePercent,
    List<RateRow> BlackFangCounts,
    List<RateRow> VpSkills,
    List<RateRow> VpSets,
    List<RateRow> EnhancementPicks,
    List<RateRow> EnhancementSets,
    int WeaponAvatarSample,               // Rare Clone wearers only
    List<RateRow> WeaponAvatarLevels,
    List<RateRow> Creatures,
    List<RateRow> PlatinumTop,
    List<RateRow> PlatinumBottom,
    List<RateRow> PlatinumAura,
    List<RateRow> EmblemEffects,          // percent within each socket color
    List<SkillRow> Skills);

// Statistics that mean the same in every class.
record AgnosticStats(
    int Sample,
    List<RateRow> ClassShare,
    List<RateRow> Sets,
    int OathSample,
    List<RateRow> OathSets,
    List<RateRow> OathSetOptions,
    List<RateRow> Titles,
    List<RateRow> DistinctBySlot,
    double AnyDistinctPercent,
    List<RateRow> Exalted,
    int BlackFangSample,
    double BlackFangAveragePercent,
    List<RateRow> BlackFangCounts);

// All classes, DPS only and support only.
record AgnosticReport(AgnosticStats All, AgnosticStats Dps, AgnosticStats Support, double SupportSharePercent);

static class Stats
{
    const int BlackFangSlots = 3;   // ring, necklace, bracelet

    public static AgnosticReport ComputeAgnosticReport(IReadOnlyList<CharacterRecord> chars)
    {
        var support = chars.Where(c => ClassRoles.IsSupport(c.Class)).ToList();
        var dps = chars.Where(c => !ClassRoles.IsSupport(c.Class)).ToList();
        return new AgnosticReport(ComputeAgnostic(chars), ComputeAgnostic(dps), ComputeAgnostic(support), 100.0 * support.Count / chars.Count);
    }

    public static AgnosticStats ComputeAgnostic(IReadOnlyList<CharacterRecord> chars)
    {
        var n = chars.Count;
        var withOath = WithOath(chars);
        var blackFang = FullOrNoBlackFang(chars);
        return new AgnosticStats(
            n,
            Rates(chars.Select(c => c.Class), n),
            Rates(chars.Select(c => c.Set), n),
            withOath.Count,
            Rates(withOath.Select(c => c.OathSet), withOath.Count),
            Rates(withOath.Select(c => $"{c.OathSet} | {c.OathOption}"), withOath.Count),
            Rates(chars.Select(c => c.TitleLevel is { } l ? $"{c.Title} [{l}]" : c.Title), n),
            Rates(chars.SelectMany(c => c.DistinctSlots.Select(SlotNames.Display)), n),
            Percent(chars.Count(c => c.DistinctSlots.Length > 0), n),
            Rates(chars.SelectMany(c => c.ExaltedSlots.Select(SlotNames.Display)), n),
            blackFang.Count,
            BlackFangAveragePercent(chars),
            BlackFangCounts(chars));
    }

    // Characters with no oath are excluded from the oath tables.
    internal static List<CharacterRecord> WithOath(IReadOnlyList<CharacterRecord> chars) =>
        chars.Where(c => c.OathSet != "(none)").ToList();

    // Only characters with none or all three Black Fang pieces are counted.
    internal static List<CharacterRecord> FullOrNoBlackFang(IReadOnlyList<CharacterRecord> chars) =>
        chars.Where(c => c.BlackFangCount is 0 or BlackFangSlots).ToList();

    // Averaged across the three slots; slot choice does not matter.
    internal static double BlackFangAveragePercent(IReadOnlyList<CharacterRecord> chars)
    {
        var counted = FullOrNoBlackFang(chars);
        return Percent(counted.Sum(c => c.BlackFangCount), BlackFangSlots * counted.Count);
    }

    internal static List<RateRow> BlackFangCounts(IReadOnlyList<CharacterRecord> chars)
    {
        var counted = FullOrNoBlackFang(chars);
        return Rates(counted.Select(c => $"{c.BlackFangCount} of {BlackFangSlots}"), counted.Count);
    }

    // 0 instead of NaN when nothing is counted.
    static double Percent(int part, int total) => total == 0 ? 0 : 100.0 * part / total;

    internal static List<RateRow> Rates(IEnumerable<string> values, int total) =>
        values.GroupBy(v => v)
            .Select(g => new RateRow(g.Key, g.Count(), 100.0 * g.Count() / total))
            .OrderByDescending(r => r.Count)
            .ToList();

    public static ClassStats Compute(IReadOnlyList<CharacterRecord> chars)
    {
        var n = chars.Count;
        var withOath = WithOath(chars);

        // The skill list only holds learned skills; missing counts as level 0.
        var skillRows = chars.SelectMany(c => c.SkillLevels.Keys).Distinct()
            .Select(skill =>
            {
                var levels = chars.Where(c => c.SkillLevels.ContainsKey(skill)).Select(c => c.SkillLevels[skill]).ToList();
                return new SkillRow(skill, 100.0 * levels.Count / n, levels.Sum() / (double)n, levels.Average());
            })
            .OrderByDescending(r => r.TakePercent)
            .ToList();

        return new ClassStats(
            n,
            Rates(chars.Select(c => c.Set), n),
            withOath.Count,
            Rates(withOath.Select(c => c.OathSet), withOath.Count),
            Rates(withOath.Select(c => $"{c.OathSet} | {c.OathOption}"), withOath.Count),
            Rates(chars.Select(c => c.Weapon), n),
            Rates(chars.Select(c => c.TitleLevel is { } l ? $"{c.Title} [{l}]" : c.Title), n),
            Rates(chars.SelectMany(c => c.DistinctSlots.Select(SlotNames.Display)), n),
            Percent(chars.Count(c => c.DistinctSlots.Length > 0), n),
            Rates(chars.SelectMany(c => c.ExaltedSlots.Select(SlotNames.Display)), n),
            FullOrNoBlackFang(chars).Count,
            BlackFangAveragePercent(chars),
            BlackFangCounts(chars),
            Rates(chars.SelectMany(c => c.Vp.Select(v => $"{v.Skill} (VP{v.Type})")), n),
            Rates(chars.Select(c => string.Join(" + ", c.Vp.Select(v => $"{v.Skill} VP{v.Type}").Order())), n),
            Rates(chars.SelectMany(c => c.Enhancement.Select(e => $"{e.Skill} ({(e.Type == 1 ? "damage" : "cooldown")})")), n),
            Rates(chars.Select(c => string.Join(" + ", c.Enhancement.Select(e => $"{e.Skill}:{e.Type}").Order())), n),
            RareClone(chars).Count,
            WeaponAvatarLevels(chars),
            Rates(chars.Select(c => c.CreatureLevel is { } l ? $"{c.Creature} [{l}]" : c.Creature), n),
            PlatinumSkillsForSlot(chars, "JACKET"),
            PlatinumSkillsForSlot(chars, "PANTS"),
            PlatinumSkillsForSlot(chars, "AURORA"),
            EmblemEffects(chars),
            skillRows);
    }

    // Only Rare Clone weapon avatars count.
    internal static List<CharacterRecord> RareClone(IReadOnlyList<CharacterRecord> chars) =>
        chars.Where(c => c.RareCloneWeaponLevel != null).ToList();

    internal static List<RateRow> WeaponAvatarLevels(IReadOnlyList<CharacterRecord> chars)
    {
        var clones = RareClone(chars);
        return Rates(clones.Select(c => $"Rare Clone [{c.RareCloneWeaponLevel}Lv]"), clones.Count);
    }

    // Platinum emblem skill on one avatar slot (JACKET top, PANTS bottom, AURORA aura).
    internal static List<RateRow> PlatinumSkillsForSlot(IReadOnlyList<CharacterRecord> chars, string slotId)
    {
        var withSkill = chars.Where(c => c.PlatinumBySlot.ContainsKey(slotId)).ToList();
        return Rates(withSkill.Select(c => c.PlatinumBySlot[slotId]), withSkill.Count);
    }

    static readonly string[] EmblemColorOrder = ["Red", "Yellow", "Green", "Blue", "Multicolored"];

    // Percent within a socket color.
    internal static List<RateRow> EmblemEffects(IReadOnlyList<CharacterRecord> chars) =>
        chars.SelectMany(c => c.EmblemCounts)
            .GroupBy(kv => kv.Key)
            .Select(g => (Color: g.Key.Split('|')[0], Effect: g.Key.Split('|')[1], Count: g.Sum(kv => kv.Value)))
            .GroupBy(x => x.Color)
            .OrderBy(colorGroup => Array.IndexOf(EmblemColorOrder, colorGroup.Key))
            .SelectMany(colorGroup =>
            {
                var total = colorGroup.Sum(x => x.Count);
                return colorGroup.OrderByDescending(x => x.Count)
                    .Select(x => new RateRow($"{colorGroup.Key}: {x.Effect}", x.Count, 100.0 * x.Count / total));
            })
            .ToList();
}
