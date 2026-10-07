using System.Text;

namespace Collector;

// Renders statistics as Markdown tables.
static class ReportWriter
{
    const int TopRows = 15;

    public static string Render(string classKey, ClassStats s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {classKey}").AppendLine().AppendLine($"Sample: **{s.Sample}** characters (fame cutoff applied).").AppendLine();
        Table(sb, "Sets", s.Sets);
        Table(sb, $"Oath sets (of {s.OathSample} characters with an oath)", s.OathSets);
        Table(sb, "Oath set and option", s.OathSetOptions);
        Table(sb, "Weapons", s.Weapons);
        Table(sb, "Titles", s.Titles);
        Table(sb, $"Distinct pieces by slot (any Distinct: {s.AnyDistinctPercent:F1}%)", s.DistinctBySlot);
        Table(sb, "Exalted pieces", s.Exalted);
        Table(sb, $"Black Fang accessories (average across ring, necklace, bracelet: {s.BlackFangAveragePercent:F1}%; {s.BlackFangSample} characters with none or all three)", s.BlackFangCounts);
        Table(sb, "VP: skills picked", s.VpSkills);
        Table(sb, "VP: full sets", s.VpSets);
        Table(sb, "Enhancement: picks", s.EnhancementPicks);
        Table(sb, "Enhancement: full sets", s.EnhancementSets);
        Table(sb, "Creatures", s.Creatures);
        Table(sb, $"Weapon avatar: Rare Clone level (of {s.WeaponAvatarSample} characters wearing a Rare Clone)", s.WeaponAvatarLevels);
        Table(sb, "Platinum emblem: top avatar", s.PlatinumTop);
        Table(sb, "Platinum emblem: bottom avatar", s.PlatinumBottom);
        Table(sb, "Platinum emblem: aura avatar", s.PlatinumAura);
        Table(sb, "Emblem effects (percent within each socket color)", s.EmblemEffects, take: 60);

        sb.AppendLine("## Skills").AppendLine();
        sb.AppendLine("| Skill | Take % | Avg level (all) | Avg level (takers) |").AppendLine("|---|---|---|---|");
        foreach (var r in s.Skills)
            sb.AppendLine($"| {Clean(r.Skill)} | {r.TakePercent:F1} | {r.AverageLevelAll:F1} | {r.AverageLevelTakers:F1} |");
        return sb.ToString();
    }

    public static string RenderAgnostic(AgnosticReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Class-agnostic statistics").AppendLine();
        sb.AppendLine($"Role split: **DPS {r.Dps.Sample}** ({100 - r.SupportSharePercent:F1}%) / **Support {r.Support.Sample}** ({r.SupportSharePercent:F1}%).").AppendLine();
        sb.AppendLine("Support classes: Crusader (F), Crusader (M), Enchantress, Muse, Paramedic.").AppendLine();
        AgnosticSection(sb, "All classes", r.All);
        AgnosticSection(sb, "DPS classes", r.Dps);
        AgnosticSection(sb, "Support classes", r.Support);
        return sb.ToString();
    }

    static void AgnosticSection(StringBuilder sb, string title, AgnosticStats s)
    {
        sb.AppendLine($"# {title}").AppendLine().AppendLine($"Sample: **{s.Sample}** characters.").AppendLine();
        Table(sb, "Class share", s.ClassShare, take: 80);
        Table(sb, "Sets", s.Sets);
        Table(sb, $"Oath sets (of {s.OathSample} characters with an oath)", s.OathSets);
        Table(sb, "Oath set and option", s.OathSetOptions);
        Table(sb, "Titles", s.Titles);
        Table(sb, $"Distinct pieces by slot (any Distinct: {s.AnyDistinctPercent:F1}%)", s.DistinctBySlot);
        Table(sb, "Exalted pieces", s.Exalted);
        Table(sb, $"Black Fang accessories (average across ring, necklace, bracelet: {s.BlackFangAveragePercent:F1}%; {s.BlackFangSample} characters with none or all three)", s.BlackFangCounts);
    }

    static void Table(StringBuilder sb, string title, List<RateRow> rows, int take = TopRows)
    {
        sb.AppendLine($"## {title}").AppendLine();
        sb.AppendLine("| Name | Count | % |").AppendLine("|---|---|---|");
        foreach (var r in rows.Take(take)) sb.AppendLine($"| {Clean(r.Name)} | {r.Count} | {r.Percent:F1} |");
        sb.AppendLine();
    }

    // '|' breaks a Markdown table cell.
    static string Clean(string text) => text.Replace("|", "/");
}
