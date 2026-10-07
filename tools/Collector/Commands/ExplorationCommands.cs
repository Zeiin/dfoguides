using System.Text.Json.Nodes;

namespace Collector.Commands;

// Developer tools for inspecting raw API responses.

class JobsCommand : ICommand
{
    public string Name => "jobs";
    public string Usage => "jobs                                      save the server and class lists";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        await ctx.SaveRawAsync("servers.json", await ctx.Client.GetAsync("/servers"));
        await ctx.SaveRawAsync("jobs.json", await ctx.Client.GetAsync("/jobs"));
        return 0;
    }
}

class ProbeCommand : ICommand
{
    public string Name => "probe";
    public string Usage => "probe <jobId> <jobGrowId> [count]         look at the top characters of one class";
    public int MinArgs => 2;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var jobId = args[0];
        var jobGrowId = args[1];
        var count = args.Length > 2 ? int.Parse(args[2]) : 3;

        // serverId "all" searches every server; isAllJobGrow=false restricts to this advancement.
        var fame = await ctx.Client.GetAsync("/servers/all/characters-fame",
            $"jobId={jobId}&jobGrowId={jobGrowId}&isAllJobGrow=false&limit=200");
        await ctx.SaveRawAsync($"fame_{jobGrowId}.json", fame);

        var rows = JsonNode.Parse(fame)?["rows"]?.AsArray();
        if (rows == null || rows.Count == 0)
        {
            Console.WriteLine("No rows returned. Check jobId/jobGrowId.");
            return 1;
        }

        foreach (var row in rows.Take(count))
        {
            var serverId = row!["serverId"]!.GetValue<string>();
            var characterId = row["characterId"]!.GetValue<string>();
            Console.WriteLine($"Character {row["characterName"]} ({serverId})");

            var basePath = $"/servers/{serverId}/characters/{characterId}";
            var tag = characterId[..8];
            await ctx.SaveRawAsync($"{tag}_equipment.json", await ctx.Client.GetAsync($"{basePath}/equip/equipment"));
            await ctx.SaveRawAsync($"{tag}_oath.json", await ctx.Client.GetAsync($"{basePath}/equip/oath"));
            await ctx.SaveRawAsync($"{tag}_skill_style.json", await ctx.Client.GetAsync($"{basePath}/skill/style"));
        }
        return 0;
    }
}

class CharacterCommand : ICommand
{
    public string Name => "character";
    public string Usage => "character <server> <name>                 look up one character by exact name";
    public int MinArgs => 2;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var server = args[0].ToLowerInvariant();
        var name = args[1];
        var search = await ctx.Client.GetAsync($"/servers/{server}/characters",
            $"characterName={Uri.EscapeDataString(name)}&wordType=match");
        var found = JsonNode.Parse(search)?["rows"]?.AsArray();
        if (found == null || found.Count == 0)
        {
            Console.WriteLine("No character found.");
            return 1;
        }

        // Detail endpoints need the character id.
        var id = found[0]!["characterId"]!.GetValue<string>();
        var path = $"/servers/{server}/characters/{id}";
        var prefix = $"char_{name}";
        await ctx.SaveRawAsync($"{prefix}_equipment.json", await ctx.Client.GetAsync($"{path}/equip/equipment"));
        await ctx.SaveRawAsync($"{prefix}_oath.json", await ctx.Client.GetAsync($"{path}/equip/oath"));
        await ctx.SaveRawAsync($"{prefix}_skill_style.json", await ctx.Client.GetAsync($"{path}/skill/style"));
        return 0;
    }
}

// Guesses Fixed vs Percent from skill text: Percent classes write "Atk.: {value1}%", Fixed classes a flat number.
class DamageTypesCommand : ICommand
{
    public string Name => "damage-types";
    public string Usage => "damage-types                              guess Fixed or Percent for every class from skill text";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    static readonly System.Text.RegularExpressions.Regex PercentAtk = new(@"Atk\.[^{\n]*\{value\d+\}%");
    static readonly System.Text.RegularExpressions.Regex FlatAtk = new(@"Atk\.[^{\n]*\{value\d+\}(?!%)");

    // (percent lines, flat lines) of attack text.
    internal static (int Percent, int Flat) CountAttackLines(string optionDesc) =>
        (PercentAtk.Matches(optionDesc).Count, FlatAtk.Matches(optionDesc).Count);

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var jobs = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.RawDir, "jobs.json")))!["rows"]!.AsArray();
        var results = new List<object>();

        foreach (var baseClass in jobs)
        {
            foreach (var chain in baseClass!["rows"]!.AsArray())
            {
                var last = chain!;
                while (last["next"] != null) last = last["next"]!;
                var jobId = baseClass["jobId"]!.GetValue<string>();
                var growId = last["jobGrowId"]!.GetValue<string>();
                var key = ClassKey.From(baseClass["jobName"]!.GetValue<string>(), last["jobGrowName"]!.GetValue<string>());

                var list = JsonNode.Parse(await ctx.Client.GetAsync($"/skills/{jobId}", $"jobGrowId={growId}"))!["skills"]!.AsArray();
                var attackSkills = list.Where(s => s!["type"]!.GetValue<string>() == "active" && s["requiredLevel"]!.GetValue<int>() is >= 25 and <= 85).Take(5);

                int percent = 0, flat = 0;
                foreach (var skill in attackSkills)
                {
                    var detail = JsonNode.Parse(await ctx.Client.GetAsync($"/skills/{jobId}/{skill!["skillId"]!.GetValue<string>()}"))!;
                    var text = detail["levelInfo"]?["optionDesc"]?.GetValue<string>() ?? "";
                    var (p, f) = CountAttackLines(text);
                    percent += p;
                    flat += f;
                }
                var guess = percent > flat ? "Percent" : flat > 0 ? "Fixed" : "Unknown";
                results.Add(new { Key = key, Guess = guess, PercentLines = percent, FlatLines = flat });
                Console.WriteLine($"{key}: {guess} (percent lines {percent}, flat lines {flat})");
            }
        }

        Directory.CreateDirectory(ctx.OutDir);
        await File.WriteAllTextAsync(Path.Combine(ctx.OutDir, "damage_types.json"), System.Text.Json.JsonSerializer.Serialize(results, Json.Pretty));
        return 0;
    }
}

class GetCommand : ICommand
{
    public string Name => "get";
    public string Usage => "get <path> <outFile> [query]              ad-hoc API call saved to a file";
    public int MinArgs => 2;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        await ctx.SaveRawAsync(args[1], await ctx.Client.GetAsync(args[0], args.Length > 2 ? args[2] : ""));
        return 0;
    }
}
