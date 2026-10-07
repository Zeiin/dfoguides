using System.Text.Json;
using Collector.Commands;

namespace Collector;

// Reads saved raw files into records, statistics and reports. Offline.
static class Analysis
{
    public static async Task<ClassStats?> AnalyzeClassAsync(CommandContext ctx, string classKey, int cutoff)
    {
        var roster = await ctx.LoadRosterAsync(classKey);
        var charDir = ctx.CharsDir(classKey);
        var records = new List<CharacterRecord>();
        var skipped = 0;

        foreach (var entry in roster.Where(e => e.Fame >= cutoff))
        {
            var record = await CharacterReader.ReadAsync(charDir, entry, classKey);
            if (record == null) skipped++;
            else records.Add(record);
        }
        if (records.Count == 0) return null;

        Directory.CreateDirectory(ctx.OutDir);
        var stats = Stats.Compute(records);
        var recordsPath = Path.Combine(ctx.OutDir, $"records_{classKey}.json");
        await File.WriteAllTextAsync(recordsPath, JsonSerializer.Serialize(records, Json.Compact));
        await File.WriteAllTextAsync(Path.Combine(ctx.OutDir, $"stats_{classKey}.json"), JsonSerializer.Serialize(stats, Json.Pretty));
        var display = ctx.LoadClassCatalog().GetValueOrDefault(classKey)?.Display ?? classKey;
        await File.WriteAllTextAsync(Path.Combine(ctx.OutDir, $"report_{classKey}.md"), ReportWriter.Render(display, stats));

        Console.WriteLine($"{display} ({classKey}): {records.Count} characters, {skipped} skipped (missing files), records {new FileInfo(recordsPath).Length / 1024} KB");
        return stats;
    }

    // classes.json: display name, role and portrait per class, for the website.
    public static async Task WriteClassIndexAsync(CommandContext ctx)
    {
        Directory.CreateDirectory(ctx.OutDir);
        var index = ctx.LoadClassCatalog().Values
            .Select(c => new
            {
                c.Key,
                c.Display,
                c.BaseClass,
                c.Advancement,
                c.Role,
                Portrait = ctx.PortraitSiteName(c.Key),
            })
            .OrderBy(c => c.Display)
            .ToList();
        await File.WriteAllTextAsync(Path.Combine(ctx.OutDir, "classes.json"), JsonSerializer.Serialize(index, Json.Pretty));
        Console.WriteLine($"classes.json: {index.Count} classes, {index.Count(c => c.Portrait != null)} with portraits");
    }

    // Swaps class keys for display names in the class share rows.
    static AgnosticStats WithDisplayNames(AgnosticStats s, IReadOnlyDictionary<string, ClassInfo> catalog) =>
        s with { ClassShare = s.ClassShare.Select(r => r with { Name = catalog.TryGetValue(r.Name, out var c) ? c.Display : r.Name }).ToList() };

    // fraction < 1 matches the sample sample-all pulls; 1.0 uses everything.
    public static async Task<AgnosticReport?> AnalyzeAllClassesAsync(CommandContext ctx, double fraction)
    {
        var records = new List<CharacterRecord>();
        foreach (var classKey in ctx.ScannedClassKeys())
        {
            var charDir = ctx.CharsDir(classKey);
            if (!Directory.Exists(charDir)) continue;

            var roster = await ctx.LoadRosterAsync(classKey);
            if (roster.Count == 0) continue;
            foreach (var entry in Sampling.EvenlySpaced(roster, fraction))
            {
                var record = await CharacterReader.ReadAsync(charDir, entry, classKey, requireSkills: false);
                if (record != null) records.Add(record);
            }
        }
        if (records.Count == 0) return null;

        Directory.CreateDirectory(ctx.OutDir);
        var catalog = ctx.LoadClassCatalog();
        var computed = Stats.ComputeAgnosticReport(records);
        var report = computed with
        {
            All = WithDisplayNames(computed.All, catalog),
            Dps = WithDisplayNames(computed.Dps, catalog),
            Support = WithDisplayNames(computed.Support, catalog),
        };
        await File.WriteAllTextAsync(Path.Combine(ctx.OutDir, "stats_all_classes.json"), JsonSerializer.Serialize(report, Json.Pretty));
        await File.WriteAllTextAsync(Path.Combine(ctx.OutDir, "report_all_classes.md"), ReportWriter.RenderAgnostic(report));
        Console.WriteLine($"All classes: {records.Count} characters across {report.All.ClassShare.Count} classes ({report.Support.Sample} support, {report.Dps.Sample} DPS).");
        return report;
    }
}
