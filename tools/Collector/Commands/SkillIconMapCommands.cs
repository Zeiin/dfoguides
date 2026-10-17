using System.Text.Json.Nodes;

namespace Collector.Commands;

// Dark Knight and Creator have no icons on dfogang, and dfmax uses Korean ids. reference/skilliconmap.json maps their skill names to a saved image
// ("file:<path under reference/>", with the skill's grid x/y when it has one); this copies the icons into the site and writes the grid positions.
class ApplySkillIconMapCommand : ICommand
{
    public string Name => "apply-skill-icon-map";
    public string Usage => "apply-skill-icon-map                      copy the hand-mapped skill icons (Dark Knight, Creator) and their grid positions";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var map = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.ReferenceDir, "skilliconmap.json")))!.AsObject();
        var layoutPath = Path.Combine(ctx.ReferenceDir, "skilllayout.json");
        var layout = JsonNode.Parse(await File.ReadAllTextAsync(layoutPath))!.AsObject();

        foreach (var (classKey, entry) in map)
        {
            var ids = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.ReferenceDir, "skilllists", $"skilllist_{classKey}.json")))!["skills"]!.AsArray()
                .ToDictionary(s => s!["name"]!.GetValue<string>(), s => s!["skillId"]!.GetValue<string>());
            var target = Path.Combine(RepoRoot.Find(), "stats", "img", "skills", classKey);
            Directory.CreateDirectory(target);

            var placed = new JsonArray();
            var copied = 0;
            foreach (var (name, skill) in entry!["skills"]!.AsObject())
            {
                if (!ids.TryGetValue(name, out var id)) { Console.WriteLine($"  {classKey}: no skill named {name}"); continue; }
                var source = skill!["source"]!.GetValue<string>()["file:".Length..];
                File.Copy(Path.Combine(ctx.ReferenceDir, source), Path.Combine(target, id + ".png"), overwrite: true);
                copied++;

                if (skill["x"] is { } x && skill["y"] is { } y)
                    placed.Add(new JsonObject { ["id"] = id, ["x"] = x.GetValue<int>(), ["y"] = y.GetValue<int>(), ["similarity"] = 1.0 });
            }
            layout[classKey] = placed;
            Console.WriteLine($"{classKey}: {copied} icons copied, {placed.Count} placed on the grid.");
        }

        await File.WriteAllTextAsync(layoutPath, layout.ToJsonString(Json.Pretty));
        await ApplyGeneralSkillsAsync(ctx);
        return 0;
    }

    // The Neo awakening passives every class has have no icon on dfogang. One image per skill name lives in
    // reference/skillicon-sources/general and is copied to every class that has the skill and no icon yet.
    internal static readonly HashSet<string> GeneralSkills = ["Subitism", "One Step Closer", "Clues to Awakening"];

    static async Task ApplyGeneralSkillsAsync(CommandContext ctx)
    {
        var sourceDir = Path.Combine(ctx.ReferenceDir, "skillicon-sources", "general");
        var copied = 0;
        foreach (var listFile in Directory.GetFiles(Path.Combine(ctx.ReferenceDir, "skilllists"), "skilllist_*.json"))
        {
            var classKey = Path.GetFileNameWithoutExtension(listFile)["skilllist_".Length..];
            var target = Path.Combine(RepoRoot.Find(), "stats", "img", "skills", classKey);
            Directory.CreateDirectory(target);
            foreach (var skill in JsonNode.Parse(await File.ReadAllTextAsync(listFile))!["skills"]!.AsArray())
            {
                var source = Path.Combine(sourceDir, skill!["name"]!.GetValue<string>() + ".png");
                var destination = Path.Combine(target, skill["skillId"]!.GetValue<string>() + ".png");
                if (!File.Exists(source) || File.Exists(destination)) continue;
                File.Copy(source, destination);
                copied++;
            }
        }
        Console.WriteLine($"General skills: {copied} icons copied.");
    }
}
