using System.Text.Json;
using System.Text.Json.Nodes;

namespace Collector.Commands;

// Saves the detail of every skill (prerequisites, max level) once per job and skill id, for laying out the skill tree.
class FetchSkillDetailsCommand : ICommand
{
    public string Name => "fetch-skill-details";
    public string Usage => "fetch-skill-details                       save every class skill's detail (prerequisites); resumable";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var dir = Path.Combine(ctx.RawDir, "skilldetails");
        Directory.CreateDirectory(dir);

        var jobs = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.RawDir, "jobs.json")))!["rows"]!.AsArray();
        var saved = 0;
        var failed = 0;

        foreach (var baseClass in jobs)
        {
            var jobId = baseClass!["jobId"]!.GetValue<string>();
            foreach (var chain in baseClass["rows"]!.AsArray())
            {
                var last = chain!;
                while (last["next"] != null) last = last["next"]!;
                var key = ClassKey.From(baseClass["jobName"]!.GetValue<string>(), last["jobGrowName"]!.GetValue<string>());
                var listPath = Path.Combine(ctx.ReferenceDir, "skilllists", $"skilllist_{key}.json");
                if (!File.Exists(listPath)) continue;

                foreach (var skill in JsonNode.Parse(await File.ReadAllTextAsync(listPath))!["skills"]!.AsArray())
                {
                    var skillId = skill!["skillId"]!.GetValue<string>();
                    var file = Path.Combine(dir, $"{jobId}_{skillId}.json");
                    if (File.Exists(file)) continue;

                    try
                    {
                        await File.WriteAllTextAsync(file, await ctx.Client.GetAsync($"/skills/{jobId}/{skillId}"));
                        if (++saved % 100 == 0) Console.WriteLine($"  {saved} saved");
                    }
                    catch (HttpRequestException ex)
                    {
                        failed++;
                        Console.WriteLine($"  failed {skillId[..8]}: {ex.Message}");
                    }
                }
            }
        }
        await SaveCombinedPrerequisitesAsync(ctx, dir);
        Console.WriteLine($"Done. {saved} saved, {failed} failed.");
        return 0;
    }

    // The page only needs each skill's prerequisites, so the saved details are boiled down to one small committed file
    // ("<jobId>_<skillId>" -> ids that must be learned first; skills without prerequisites are left out).
    static async Task SaveCombinedPrerequisitesAsync(CommandContext ctx, string detailDir)
    {
        var combined = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(detailDir, "*.json"))
        {
            var detail = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
            var ids = detail["preRequiredSkill"]?.AsArray().Select(p => p!["skillId"]!.GetValue<string>()).ToArray() ?? [];
            if (ids.Length > 0) combined[Path.GetFileNameWithoutExtension(file)] = ids;
        }
        Directory.CreateDirectory(ctx.ReferenceDir);
        await File.WriteAllTextAsync(Path.Combine(ctx.ReferenceDir, "skillprereqs.json"), JsonSerializer.Serialize(combined, Json.Pretty));
        Console.WriteLine($"Wrote skillprereqs.json: {combined.Count} skills with prerequisites.");
    }
}

// Saves each class skill list and downloads skill icons from dfogang's CDN (<Base>/<Advancement>/<Skill_Name>.png).
class FetchSkillIconsCommand : ICommand
{
    public string Name => "fetch-skill-icons";
    public string Usage => "fetch-skill-icons                         save skill lists and download skill icons (slow, resumable)";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    const string CdnRoot = "https://cdn.dfogang.com/assets/skills";

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var siteSkills = Path.Combine(RepoRoot.Find(), "stats", "img", "skills");
        Directory.CreateDirectory(siteSkills);
        Directory.CreateDirectory(Path.Combine(siteSkills, "badges"));

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("dfoguides-tools/0.1 (personal hobby project)");

        // VP and enhancement badges.
        foreach (var badge in new[] { "Evolve1", "Evolve2", "Enhance1", "Enhance2" })
            await DownloadAsync(http, $"{CdnRoot}/SkillEvolve/{badge}.png", Path.Combine(siteSkills, "badges", $"{badge}.png"));

        var jobs = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.RawDir, "jobs.json")))!["rows"]!.AsArray();
        var total = 0;
        var missing = new List<string>();

        foreach (var baseClass in jobs)
        {
            foreach (var chain in baseClass!["rows"]!.AsArray())
            {
                var last = chain!;
                while (last["next"] != null) last = last["next"]!;
                var baseName = baseClass["jobName"]!.GetValue<string>();
                var advancement = ClassCatalog.ShortName(last["jobGrowName"]!.GetValue<string>());
                var key = ClassKey.From(baseName, last["jobGrowName"]!.GetValue<string>());

                // The skill list also feeds the skill tree on the page.
                var listPath = Path.Combine(ctx.ReferenceDir, "skilllists", $"skilllist_{key}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(listPath)!);
                if (!File.Exists(listPath))
                    await File.WriteAllTextAsync(listPath,await ctx.Client.GetAsync($"/skills/{baseClass["jobId"]!.GetValue<string>()}", $"jobGrowId={last["jobGrowId"]!.GetValue<string>()}"));

                var skills = JsonNode.Parse(await File.ReadAllTextAsync(listPath))!["skills"]!.AsArray();
                var classHits = 0;
                var classDir = Path.Combine(siteSkills, key);
                Directory.CreateDirectory(classDir);
                foreach (var skill in skills)
                {
                    var id = skill!["skillId"]!.GetValue<string>();
                    var name = skill["name"]!.GetValue<string>();
                    var target = Path.Combine(classDir, $"{id}.png");
                    total++;
                    if (File.Exists(target)) { classHits++; continue; }

                    var ok = false;
                    // The general skills have no icon on dfogang; apply-skill-icon-map supplies them.
                    if (ApplySkillIconMapCommand.GeneralSkills.Contains(name) || SiteExport.HiddenSkills.Contains(name)) { missing.Add($"{key}: {name}"); continue; }
                    foreach (var file in FileNames(name))
                    {
                        var url = $"{CdnRoot}/{Uri.EscapeDataString(baseName)}/{Uri.EscapeDataString(advancement)}/{Uri.EscapeDataString(file)}";
                        if (await DownloadAsync(http, url, target)) { ok = true; break; }
                    }
                    if (ok) classHits++; else missing.Add($"{key}: {name}");
                }
                Console.WriteLine($"{key}: {classHits}/{skills.Count} icons");
            }
        }

        await File.WriteAllLinesAsync(Path.Combine(ctx.OutDir, "skill_icons_missing.txt"), missing);
        Console.WriteLine($"Done. {total - missing.Count}/{total} skills have icons; {missing.Count} missing (see data/out/skill_icons_missing.txt).");
        return 0;
    }

    // dfogang file names: spaces to underscores, apostrophes dropped, colon to hyphen, "!" and "?" dropped; other spellings are fallbacks.
    internal static IEnumerable<string> FileNames(string skillName)
    {
        yield return skillName.Replace(":", "-").Replace("'", "").Replace(' ', '_') + ".png";
        yield return skillName.Replace(":", "").Replace("'", "").Replace(' ', '_') + ".png";
        yield return skillName.Replace(":", "-").Replace(' ', '_') + ".png";
        yield return skillName.Replace("'", "").Replace(' ', '_') + ".png";

        var plain = skillName.Replace("!", "").Replace("?", "");
        if (plain == skillName) yield break;
        yield return plain.Replace(":", "-").Replace("'", "").Replace(' ', '_') + ".png";
        yield return plain.Replace(":", "").Replace("'", "").Replace(' ', '_') + ".png";
    }

    static async Task<bool> DownloadAsync(HttpClient http, string url, string target)
    {
        await Task.Delay(250);
        using var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return false;
        await File.WriteAllBytesAsync(target, await response.Content.ReadAsByteArrayAsync());
        return true;
    }
}
