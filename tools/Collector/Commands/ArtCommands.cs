using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Collector.Commands;

// One-time portrait download into data/art/classes; not part of the daily pipeline.
class FetchArtCommand : ICommand
{
    public string Name => "fetch-art";
    public string Usage => "fetch-art                                 download class portraits from dfo.gg (once; skips files already saved)";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    const string PortraitUrl = "https://dfo.gg/assets/characters/{0}.jpeg";

    // Classes whose dfo.gg name differs from the generated one.
    static readonly Dictionary<string, string> SlugOverrides = new()
    {
    };

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var artDir = Path.Combine(ctx.OutDir, "..", "art", "classes");
        Directory.CreateDirectory(artDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("dfoguides-tools/0.1 (personal hobby project)");

        var jobsRoot = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.RawDir, "jobs.json")))!["rows"]!.AsArray();
        var saved = 0;
        var missing = new List<string>();

        foreach (var baseClass in jobsRoot)
        {
            foreach (var chain in baseClass!["rows"]!.AsArray())
            {
                var last = chain!;
                while (last["next"] != null) last = last["next"]!;
                var baseName = baseClass["jobName"]!.GetValue<string>();
                var growName = last["jobGrowName"]!.GetValue<string>();

                var classKey = ClassKey.From(baseName, growName);
                var file = Path.Combine(artDir, $"{classKey}.jpeg");
                if (File.Exists(file)) continue;

                // "Slayer (M)" + "Neo: Blade Master" -> "slayer-m-blade-master"
                var slug = SlugOverrides.GetValueOrDefault(classKey, $"{Slug(baseName)}-{Slug(growName.Replace("Neo: ", ""))}");
                using var response = await http.GetAsync(string.Format(PortraitUrl, slug));
                if (response.IsSuccessStatusCode)
                {
                    await File.WriteAllBytesAsync(file, await response.Content.ReadAsByteArrayAsync());
                    saved++;
                }
                else
                {
                    missing.Add($"{classKey} (tried {slug}, status {(int)response.StatusCode})");
                }
                await Task.Delay(700);
            }
        }

        Console.WriteLine($"Saved {saved} portraits to {Path.GetFullPath(artDir)}");
        foreach (var m in missing) Console.WriteLine($"  missing: {m}");
        return 0;
    }

    static string Slug(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
}
