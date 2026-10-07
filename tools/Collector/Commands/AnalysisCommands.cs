namespace Collector.Commands;

// Offline: reads saved files only, no API key.

class AnalyzeCommand : ICommand
{
    public string Name => "analyze";
    public string Usage => "analyze <classKey> [minFame]              records, stats and report for one class (default cutoff 91582)";
    public int MinArgs => 1;
    public bool NeedsApiKey => false;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var cutoff = args.Length > 1 ? int.Parse(args[1]) : 91_582;
        var stats = await Analysis.AnalyzeClassAsync(ctx, args[0], cutoff);
        if (stats == null)
        {
            Console.WriteLine("No characters found (is the data pulled?).");
            return 1;
        }

        foreach (var (label, table) in new[] { ("Sets", stats.Sets), ("Oath sets", stats.OathSets), ("Weapons", stats.Weapons), ("VP sets", stats.VpSets) })
            Console.WriteLine($"{label}: " + string.Join("; ", table.Take(3).Select(r => $"{r.Name} {r.Percent:F1}%")));
        return 0;
    }
}

class AnalyzeAllCommand : ICommand
{
    public string Name => "analyze-all";
    public string Usage => "analyze-all [fraction]                    class-agnostic stats (default 0.1 matches sample-all; 1 uses everything)";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var fraction = double.Parse(args.Length > 0 ? args[0] : "0.1", System.Globalization.CultureInfo.InvariantCulture);
        var report = await Analysis.AnalyzeAllClassesAsync(ctx, fraction);
        if (report == null) return 1;

        var stats = report.All;
        foreach (var (label, table) in new[] { ("Sets", stats.Sets), ("Oath sets", stats.OathSets), ("Titles", stats.Titles) })
            Console.WriteLine($"{label}: " + string.Join("; ", table.Take(5).Select(r => $"{r.Name} {r.Percent:F1}%")));
        return 0;
    }
}

class AnalyzeEveryCommand : ICommand
{
    public string Name => "analyze-every";
    public string Usage => "analyze-every [minFame]                   analyze every pulled class, then all classes together";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var cutoff = args.Length > 0 ? int.Parse(args[0]) : 91_582;
        foreach (var classKey in ctx.ScannedClassKeys())
        {
            if (!Directory.Exists(ctx.CharsDir(classKey))) continue;
            await Analysis.AnalyzeClassAsync(ctx, classKey, cutoff);
        }
        await Analysis.AnalyzeAllClassesAsync(ctx, 1.0);
        await Analysis.WriteClassIndexAsync(ctx);
        Console.WriteLine($"Reports are in {ctx.OutDir}");
        return 0;
    }
}
