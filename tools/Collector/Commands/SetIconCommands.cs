using System.Text.Json;
using System.Text.Json.Nodes;

namespace Collector.Commands;

// Downloads the 12 gear set emblems from dfmax into stats/img/items and records which set family each page set name belongs to.
class FetchSetIconsCommand : ICommand
{
    public string Name => "fetch-set-icons";
    public string Usage => "fetch-set-icons                           download the 12 set emblems (one page, 12 images) and map every set name to its family";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    const int PauseMs = 250;

    // Any class page lists all 12 gear sets with their emblems.
    const string ClassPage = "/jobs/귀검사(여)/眞 다크템플러/summary";

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var dir = Path.Combine(ctx.RawDir, "dfmax");
        Directory.CreateDirectory(dir);
        var iconDir = Path.Combine(RepoRoot.Find(), "stats", "img", "items");
        Directory.CreateDirectory(iconDir);

        // 1. The emblems. On the page a set is a record with its Korean name (setItem) and image path (imagePath).
        using var http = Dfmax.NewClient();
        var html = await Dfmax.CachedTextAsync(http, Dfmax.Host + Dfmax.EscapePath(ClassPage), Path.Combine(dir, "summary_sets.html"), PauseMs);
        var root = Dfmax.Payload(html);
        var saved = 0;
        var found = new HashSet<string>();
        foreach (var o in root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
        {
            var korean = Dfmax.Text(root, o, "setItem");
            var path = Dfmax.Text(root, o, "imagePath");
            if (korean == null || path == null || !SetEmblems.KoreanFamilies.TryGetValue(korean, out var family)) continue;
            found.Add(family);
            var file = Path.Combine(iconDir, SetEmblems.FileName(family));
            if (File.Exists(file)) continue;
            await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(Dfmax.Host + "/_ipx/_/image" + Dfmax.EscapePath(path["/image".Length..])));
            await Task.Delay(PauseMs);
            saved++;
        }
        Console.WriteLine($"{saved} emblems downloaded; {found.Count} of {SetEmblems.KoreanFamilies.Count} sets found on the page.");

        // 2. Page set name -> family. A tier label ("Paradise Found Set") is paired with the family its own pieces report ("Hideout's Endless Gold Set").
        var counts = new Dictionary<string, Dictionary<string, int>>();
        foreach (var classKey in ctx.ScannedClassKeys())
        {
            var charsDir = ctx.CharsDir(classKey);
            if (!Directory.Exists(charsDir)) continue;
            foreach (var file in Directory.EnumerateFiles(charsDir, "*_equipment.json"))
            {
                var e = JsonNode.Parse(await File.ReadAllTextAsync(file));
                var label = e?["setItemInfo"]?.AsArray().FirstOrDefault()?["setItemName"]?.GetValue<string>();
                var piece = e?["equipment"]?.AsArray()
                    .Select(i => i?["setItemName"]?.GetValue<string>())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .GroupBy(n => n)
                    .MaxBy(g => g.Count())?.Key;
                if (label == null || piece == null) continue;
                var name = SetNames.Display(label);
                var family = piece.EndsWith(" Set") ? piece[..^" Set".Length] : piece;
                if (!counts.TryGetValue(name, out var byFamily)) counts[name] = byFamily = new();
                byFamily[family] = byFamily.GetValueOrDefault(family) + 1;
            }
        }
        var families = counts.ToDictionary(c => c.Key, c => c.Value.MaxBy(f => f.Value).Key);
        Directory.CreateDirectory(ctx.ReferenceDir);
        await File.WriteAllTextAsync(Path.Combine(ctx.ReferenceDir, "setfamilies.json"),JsonSerializer.Serialize(families, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote setfamilies.json: {families.Count} set names -> {families.Values.Distinct().Count()} families.");
        return 0;
    }
}
