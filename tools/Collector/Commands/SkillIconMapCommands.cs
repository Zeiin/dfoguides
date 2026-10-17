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
        return 0;
    }
}
