using System.Text.Json.Nodes;

namespace Collector.Commands;

// Skills whose icon or grid position cannot be found automatically (dfogang's CDN lacks them, dfmax uses Korean ids): reference/skilliconmap.json maps
// skill names to a saved image ("source": "file:<path under reference/>", left out when the skill already has an icon) and the skill's grid x/y.
// A class entry replaces the class's whole grid (Dark Knight and Creator, which no icon source has); with "merge": true its skills are added to the grid
// that fetch-skill-layout found (a handful of skills it could not match). This copies the icons into the site and writes the grid positions.
class ApplySkillIconMapCommand : ICommand
{
    public string Name => "apply-skill-icon-map";
    public string Usage => "apply-skill-icon-map                      copy the hand-mapped skill icons and write their grid positions (run after fetch-skill-layout)";
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

            var merge = entry!["merge"]?.GetValue<bool>() == true && layout[classKey] is JsonArray;
            var placed = merge ? (JsonArray)layout[classKey]! : new JsonArray();
            var copied = 0;
            var added = 0;
            foreach (var (name, skill) in entry["skills"]!.AsObject())
            {
                if (!ids.TryGetValue(name, out var id)) { Console.WriteLine($"  {classKey}: no skill named {name}"); continue; }
                if (skill!["source"] is { } source)
                {
                    File.Copy(Path.Combine(ctx.ReferenceDir, source.GetValue<string>()["file:".Length..]), Path.Combine(target, id + ".png"), overwrite: true);
                    copied++;
                }

                if (skill["x"] is { } x && skill["y"] is { } y)
                {
                    foreach (var old in placed.Where(p => p!["id"]!.GetValue<string>() == id).ToList()) placed.Remove(old);
                    placed.Add(new JsonObject { ["id"] = id, ["x"] = x.GetValue<int>(), ["y"] = y.GetValue<int>(), ["similarity"] = 1.0 });
                    added++;
                }
            }
            layout[classKey] = placed;
            Console.WriteLine($"{classKey}: {copied} icons copied, {added} {(merge ? "added to" : "placed on")} the grid.");
        }

        await File.WriteAllTextAsync(layoutPath, layout.ToJsonString(Json.Pretty));
        return 0;
    }
}
