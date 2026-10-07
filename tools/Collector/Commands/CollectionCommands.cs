using System.Text.Json;
using System.Text.Json.Nodes;

namespace Collector.Commands;

// Rosters and character downloads: the real pipeline.

class ScanCommand : ICommand
{
    public string Name => "scan";
    public string Usage => "scan [floor]                              roster every class above a fame floor (default 91582)";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var floor = args.Length > 0 ? int.Parse(args[0]) : 91_582;
        var jobsRoot = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.RawDir, "jobs.json")))!["rows"]!.AsArray();
        var summary = new List<string>();

        foreach (var baseClass in jobsRoot)
        {
            foreach (var chain in baseClass!["rows"]!.AsArray())
            {
                // The last `next` link in a chain is the final ("Neo:") advancement.
                var last = chain!;
                while (last["next"] != null) last = last["next"]!;
                var growId = last["jobGrowId"]!.GetValue<string>();
                var growName = last["jobGrowName"]!.GetValue<string>();
                var baseName = baseClass["jobName"]!.GetValue<string>();

                // jobGrowId repeats across base classes, so key by base class + advancement.
                var classKey = ClassKey.From(baseName, growName);
                var roster = await Roster.BuildAsync(ctx.Client, baseClass["jobId"]!.GetValue<string>(), growId, floorFame: floor);
                await File.WriteAllTextAsync(ctx.RosterPath(classKey), JsonSerializer.Serialize(roster, Json.Pretty));
                var line = $"{classKey}: {roster.Count}";
                summary.Add(line);
                Console.WriteLine(line);
            }
        }
        await File.WriteAllLinesAsync(Path.Combine(ctx.RawDir, "scan_summary.txt"), summary);
        return 0;
    }
}

class RosterCommand : ICommand
{
    public string Name => "roster";
    public string Usage => "roster <classKey> <jobId> <jobGrowId>     full roster of one class";
    public int MinArgs => 3;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var entries = await Roster.BuildAsync(ctx.Client, args[1], args[2]);
        await File.WriteAllTextAsync(ctx.RosterPath(args[0]), JsonSerializer.Serialize(entries, Json.Pretty));
        Console.WriteLine($"Roster: {entries.Count} characters.");
        return 0;
    }
}

class PullCommand : ICommand
{
    public string Name => "pull";
    public string Usage => "pull <classKey> <topN>                    download the top N characters of a saved roster";
    public int MinArgs => 2;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var roster = await ctx.LoadRosterAsync(args[0]);
        await CharacterPuller.PullAsync(ctx.Client, roster.Take(int.Parse(args[1])).ToList(), ctx.CharsDir(args[0]));
        return 0;
    }
}

class SampleCommand : ICommand
{
    public string Name => "sample";
    public string Usage => "sample <classKey> <min> <max> <count>     equipment-only sample inside a fame band";
    public int MinArgs => 4;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var roster = await ctx.LoadRosterAsync(args[0]);
        var band = roster.Where(e => e.Fame >= int.Parse(args[1]) && e.Fame <= int.Parse(args[2])).ToList();
        var want = Math.Min(int.Parse(args[3]), band.Count);
        var picked = Enumerable.Range(0, want).Select(i => band[i * band.Count / want]).ToList();
        await CharacterPuller.PullAsync(ctx.Client, picked, ctx.CharsDir(args[0]), only: ["equipment"]);
        return 0;
    }
}

class SampleAllCommand : ICommand
{
    public string Name => "sample-all";
    public string Usage => "sample-all [fraction]                     sample every class, gear and oath only (default 0.1)";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var fraction = double.Parse(args.Length > 0 ? args[0] : "0.1", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var classKey in ctx.ScannedClassKeys())
        {
            var roster = await ctx.LoadRosterAsync(classKey);
            if (roster.Count == 0) continue;

            var chosen = Sampling.EvenlySpaced(roster, fraction);
            Console.WriteLine($"{classKey}: {chosen.Count} of {roster.Count}");
            await CharacterPuller.PullAsync(ctx.Client, chosen, ctx.CharsDir(classKey), only: ["equipment", "oath"]);
        }
        return 0;
    }
}

class PullAllCommand : ICommand
{
    public string Name => "pull-all";
    public string Usage => "pull-all [delayMs]                        download every character of every scanned class (hours; resumable; default delay 250 ms)";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        if (args.Length > 0)
        {
            ctx.Client.MinGap = TimeSpan.FromMilliseconds(int.Parse(args[0]));
            Console.WriteLine($"Pause between calls: {args[0]} ms");
        }

        var classes = new List<(string Key, List<RosterEntry> Roster)>();
        foreach (var key in ctx.ScannedClassKeys())
        {
            var roster = await ctx.LoadRosterAsync(key);
            if (roster.Count > 0) classes.Add((key, roster));
        }

        Console.WriteLine($"{classes.Count} classes, {classes.Sum(c => c.Roster.Count)} characters in total.");
        // Smallest classes first, so a stopped run leaves most classes complete.
        foreach (var (key, roster) in classes.OrderBy(c => c.Roster.Count))
        {
            Console.WriteLine($"== {key} ({roster.Count})");
            await CharacterPuller.PullAsync(ctx.Client, roster, ctx.CharsDir(key));
        }
        return 0;
    }
}
