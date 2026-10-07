using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Collector;

// Key = stable id used in file names ("Archer_Neo_Chimera"); Display = what the website shows ("Chimera").
record ClassInfo(string Key, string BaseClass, string Advancement, string Display, string Role);

static class ClassCatalog
{
    // Final ("Neo:") advancement of every chain in jobs.json, with a short display name.
    public static List<ClassInfo> FromJobs(string jobsJson)
    {
        var found = new List<(string Base, string Advancement, string Key)>();
        foreach (var baseClass in JsonNode.Parse(jobsJson)!["rows"]!.AsArray())
        {
            foreach (var chain in baseClass!["rows"]!.AsArray())
            {
                var last = chain!;
                while (last["next"] != null) last = last["next"]!;
                var baseName = baseClass["jobName"]!.GetValue<string>();
                var growName = last["jobGrowName"]!.GetValue<string>();
                found.Add((baseName, ShortName(growName), ClassKey.From(baseName, growName)));
            }
        }

        // Short names shared by several classes get a gender suffix.
        var shared = found.GroupBy(f => f.Advancement).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return found
            .Select(f => new ClassInfo(
                f.Key,
                f.Base,
                f.Advancement,
                shared.Contains(f.Advancement) ? $"{f.Advancement} {Suffix(f.Base)}" : f.Advancement,
                ClassRoles.IsSupport(f.Key) ? "Support" : "DPS"))
            .ToList();
    }

    // "Neo: Chimera" -> "Chimera"
    internal static string ShortName(string growName) =>
        growName.StartsWith("Neo: ") ? growName["Neo: ".Length..] : growName;

    // "Priest (F)" -> "(F)". A base class without a gender falls back to its own name in parentheses.
    internal static string Suffix(string baseName)
    {
        var gender = Regex.Match(baseName, @"\((M|F)\)");
        return gender.Success ? gender.Value : $"({baseName})";
    }
}
