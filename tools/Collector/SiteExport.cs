using System.Text.Json;
using System.Text.Json.Serialization;
using Collector.Commands;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Collector;

record SiteClass(string Key, string Display, string BaseClass, string Advancement, string Role, string? Portrait, int Sample, double Share, int FameMin, int FameMax);
// One skill of a class tree. Pre lists the skill ids that must be learned first; X and Y are its column and row in the game's skill window
// (null when the skill could not be placed).
record SiteSkill(string Id, string Name, int Level, string Type, string? Icon, string[] Pre, int? X, int? Y);

record SiteData(string Generated, int CutoffFame, int TotalCharacters, List<SiteClass> Classes, Dictionary<string, object> Stats,
    Dictionary<string, Dictionary<string, string>> Icons, Dictionary<string, List<SiteSkill>> SkillTree,
    Dictionary<string, string> SetFamilies, Dictionary<string, string> FamilyIcons, Dictionary<string, string> WeaponRarity);

// Writes the website data file and web-sized portraits into <repo>/stats.
static class SiteExport
{
    const int TopRows = 20;
    // Left out of the skill tree: Skill Chain and the three Neo awakening passives are not skills anyone chooses (they sit at level 0 in every class).
    internal static readonly HashSet<string> HiddenSkills = ["Skill Chain", "Subitism", "One Step Closer", "Clues to Awakening"];
    const int PortraitWidth = 240;
    const int PortraitHeight = 300;

    // Percentages are shown to one decimal.
    sealed class RoundedDouble : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(Math.Round(value, 1));
    }

    static readonly JsonSerializerOptions WebJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new RoundedDouble() },
    };

    // reuseIcons skips the slow rescan for item icons.
    public static async Task ExportAsync(CommandContext ctx, int cutoffFame, bool reuseIcons = false)
    {
        var siteDir = Path.Combine(RepoRoot.Find(), "stats");
        var portraitDir = Path.Combine(siteDir, "img", "classes");
        Directory.CreateDirectory(Path.Combine(siteDir, "data"));
        Directory.CreateDirectory(portraitDir);

        var catalog = ctx.LoadClassCatalog().Values.OrderBy(c => c.Display).ToList();
        var stats = new Dictionary<string, object>();
        var classes = new List<SiteClass>();
        var records = new Dictionary<string, List<CharacterRecord>>();

        foreach (var c in catalog)
        {
            var path = Path.Combine(ctx.OutDir, $"stats_{c.Key}.json");
            if (!File.Exists(path)) continue;
            var s = JsonSerializer.Deserialize<ClassStats>(await File.ReadAllTextAsync(path))!;
            stats[c.Key] = Trim(s);

            var portraitFile = ctx.FindPortrait(c.Key);
            var portrait = ctx.PortraitSiteName(c.Key);
            if (portraitFile != null && portrait != null)
            {
                if (OperatingSystem.IsWindows()) SavePortrait(portraitFile, Path.Combine(portraitDir, portrait));
                else Console.WriteLine("  portraits are resized on Windows only; keeping existing files");
            }
            // Without the source art (a fresh checkout) the committed web portrait is used.
            portrait ??= new[] { $"{c.Key}.png", $"{c.Key}.jpg" }.FirstOrDefault(f => File.Exists(Path.Combine(portraitDir, f)));
            var classRecords = JsonSerializer.Deserialize<List<CharacterRecord>>(await File.ReadAllTextAsync(Path.Combine(ctx.OutDir, $"records_{c.Key}.json")), Json.Options)!;
            records[c.Key] = classRecords;
            classes.Add(new SiteClass(c.Key, c.Display, c.BaseClass, c.Advancement, c.Role, portrait, s.Sample, 0, classRecords.Min(r => r.Fame), classRecords.Max(r => r.Fame)));
        }

        var total = classes.Sum(c => c.Sample);
        classes = classes.Select(c => c with { Share = 100.0 * c.Sample / total }).ToList();

        var agnostic = JsonSerializer.Deserialize<AgnosticReport>(await File.ReadAllTextAsync(Path.Combine(ctx.OutDir, "stats_all_classes.json")))!;
        stats["all"] = Trim(agnostic.All);
        stats["dps"] = Trim(agnostic.Dps);
        stats["support"] = Trim(agnostic.Support);

        // Every name drawn on the page, so only used icons are downloaded.
        var names = new Dictionary<string, HashSet<string>>();
        void Want(string category, IEnumerable<RateRow> rows)
        {
            if (!names.TryGetValue(category, out var set)) names[category] = set = new();
            foreach (var row in rows) set.Add(row.Name);
        }
        foreach (var value in stats.Values)
        {
            if (value is ClassStats c)
            {
                Want("weapons", c.Weapons); Want("sets", c.Sets); Want("oath", c.OathSets); Want("creatures", c.Creatures);
                Want("weaponAvatars", c.WeaponAvatarLevels); Want("platinum", c.PlatinumTop); Want("platinum", c.PlatinumBottom);
                Want("platinum", c.PlatinumAura); Want("emblems", c.EmblemEffects); Want("exalted", c.Exalted); Want("distinct", c.DistinctItems); Want("titles", c.Titles);
            }
            else if (value is AgnosticStats a)
            {
                Want("sets", a.Sets); Want("oath", a.OathSets); Want("exalted", a.Exalted); Want("distinct", a.DistinctItems); Want("titles", a.Titles);
            }
        }
        var previous = Path.Combine(siteDir, "data", "data.js");
        Dictionary<string, Dictionary<string, string>> icons;
        if (reuseIcons && File.Exists(previous))
        {
            var script = await File.ReadAllTextAsync(previous);
            var json = script["window.DFO_DATA = ".Length..].TrimEnd().TrimEnd(';');
            icons = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                JsonDocument.Parse(json).RootElement.GetProperty("icons").GetRawText())!;
            Console.WriteLine("Reusing item icons from the previous export.");
        }
        else
        {
            icons = await SiteIcons.BuildAsync(ctx, Path.Combine(siteDir, "img", "items"), cutoffFame, names);
        }

        // Sets and oath sets are drawn with the set's emblem (fetch-set-icons), not a piece of its gear.
        SetEmblems.Apply(icons, names, Path.Combine(siteDir, "img", "items"), SetEmblems.ReadFamilies(Path.Combine(ctx.ReferenceDir, "setfamilies.json")));

        // Skill trees from the committed skill lists (reference/).
        var skillTree = new Dictionary<string, List<SiteSkill>>();
        var jobIds = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.RawDir, "jobs.json"))).RootElement.GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("jobName").GetString()!, r => r.GetProperty("jobId").GetString()!);
        var layout = ReadSkillLayout(Path.Combine(ctx.ReferenceDir, "skilllayout.json"));
        var prerequisites = ReadPrerequisites(Path.Combine(ctx.ReferenceDir, "skillprereqs.json"));
        foreach (var c in classes)
        {
            var listPath = Path.Combine(ctx.ReferenceDir, "skilllists", $"skilllist_{c.Key}.json");
            if (!File.Exists(listPath)) continue;
            jobIds.TryGetValue(c.BaseClass, out var jobId);
            var placed = layout.GetValueOrDefault(c.Key) ?? [];
            var list = JsonDocument.Parse(await File.ReadAllTextAsync(listPath)).RootElement.GetProperty("skills");
            // Skill ids repeat across classes with different skills, so icons live in one folder per class.
            skillTree[c.Key] = list.EnumerateArray()
                .Where(s => !HiddenSkills.Contains(s.GetProperty("name").GetString()!))
                .Select(s =>
                {
                    var id = s.GetProperty("skillId").GetString()!;
                    var icon = $"{c.Key}/{id}.png";
                    return new SiteSkill(
                        id,
                        s.GetProperty("name").GetString()!,
                        s.GetProperty("requiredLevel").GetInt32(),
                        s.GetProperty("type").GetString()!,
                        File.Exists(Path.Combine(siteDir, "img", "skills", icon)) ? icon : null,
                        prerequisites.GetValueOrDefault($"{jobId}_{id}") ?? [],
                        placed.TryGetValue(id, out var spot) ? spot.X : null,
                        placed.TryGetValue(id, out spot) ? spot.Y : null);
                })
                .ToList();
        }

        // The filters: every class's characters as compact rows, and which family each set name belongs to (a family is one emblem set).
        var recordsDir = Path.Combine(siteDir, "data", "records");
        Directory.CreateDirectory(recordsDir);
        foreach (var (key, list) in records) await SiteRecords.WriteAsync(recordsDir, key, list);
        var setFamilies = SetEmblems.ReadFamilies(Path.Combine(ctx.ReferenceDir, "setfamilies.json"));
        var familyIcons = setFamilies.Values.Distinct()
            .Where(f => File.Exists(Path.Combine(siteDir, "img", "items", SetEmblems.FileName(f))))
            .ToDictionary(f => f, SetEmblems.FileName);

        // Weapon name -> the rarity most of its wearers have (the page draws a coloured frame from it).
        var weaponRarity = records.Values.SelectMany(r => r).Where(c => c.WeaponRarity != null)
            .GroupBy(c => c.Weapon)
            .ToDictionary(g => g.Key, g => g.GroupBy(c => c.WeaponRarity!).MaxBy(x => x.Count())!.Key);

        var data = new SiteData(DateTime.UtcNow.ToString("yyyy-MM-dd"), cutoffFame, total, classes, stats, icons, skillTree, setFamilies, familyIcons, weaponRarity);
        // A script file, not fetched JSON, so the page also works opened from disk.
        await File.WriteAllTextAsync(Path.Combine(siteDir, "data", "data.js"), "window.DFO_DATA = " + JsonSerializer.Serialize(data, WebJson) + ";");
        Console.WriteLine($"Exported {classes.Count} classes to {siteDir}");
    }

    // class key -> skill id -> (column, row) in the game's skill window, from fetch-skill-layout; empty when it has not been run.
    internal static Dictionary<string, Dictionary<string, (int X, int Y)>> ReadSkillLayout(string path)
    {
        var result = new Dictionary<string, Dictionary<string, (int X, int Y)>>();
        if (!File.Exists(path)) return result;
        foreach (var c in JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject())
            result[c.Name] = c.Value.EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!, s => (s.GetProperty("x").GetInt32(), s.GetProperty("y").GetInt32()));
        return result;
    }

    // "<jobId>_<skillId>" -> ids of the skills that must be learned first, from the file fetch-skill-details writes (empty when it is missing).
    internal static Dictionary<string, string[]> ReadPrerequisites(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))! : [];

    // Scales to cover a 240x300 card, cropped from the top; a ready-made 240x300 PNG is copied as is.
    // PNG targets keep transparency. Windows-only (System.Drawing).
    [SupportedOSPlatform("windows")]
    static void SavePortrait(string source, string target)
    {
        if (source.EndsWith("_240x300.png"))
        {
            File.Copy(source, target, overwrite: true);
            return;
        }

        using var original = Image.FromFile(source);
        var scale = Math.Max((double)PortraitWidth / original.Width, (double)PortraitHeight / original.Height);
        var width = (int)Math.Ceiling(original.Width * scale);
        var height = (int)Math.Ceiling(original.Height * scale);

        using var card = new Bitmap(PortraitWidth, PortraitHeight);
        using (var graphics = Graphics.FromImage(card))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle((PortraitWidth - width) / 2, 0, width, height));
        }

        if (target.EndsWith(".png"))
        {
            card.Save(target, ImageFormat.Png);
            return;
        }

        var jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        using var options = new EncoderParameters(1);
        options.Param[0] = new EncoderParameter(Encoder.Quality, 80L);
        card.Save(target, jpeg, options);
    }

    static List<RateRow> T(List<RateRow> rows, int take = TopRows) => rows.Take(take).ToList();

    static ClassStats Trim(ClassStats s) => s with
    {
        Sets = T(s.Sets), OathSets = T(s.OathSets), Weapons = T(s.Weapons), Titles = T(s.Titles),
        DistinctItems = T(s.DistinctItems), Exalted = T(s.Exalted), BlackFangCounts = T(s.BlackFangCounts), BlackFangSets = T(s.BlackFangSets),
        // These tables need every row (each skill appears once per option).
        VpSkills = T(s.VpSkills, 80), VpSets = T(s.VpSets), EnhancementPicks = T(s.EnhancementPicks, 80), EnhancementSets = T(s.EnhancementSets),
        WeaponAvatarLevels = T(s.WeaponAvatarLevels), Creatures = T(s.Creatures),
        PlatinumTop = T(s.PlatinumTop), PlatinumBottom = T(s.PlatinumBottom), PlatinumAura = T(s.PlatinumAura),
        EmblemEffects = T(s.EmblemEffects, 80),
    };

    static AgnosticStats Trim(AgnosticStats s) => s with
    {
        Sets = T(s.Sets, 80), OathSets = T(s.OathSets), Titles = T(s.Titles),
        DistinctItems = T(s.DistinctItems), Exalted = T(s.Exalted), BlackFangCounts = T(s.BlackFangCounts), BlackFangSets = T(s.BlackFangSets),
    };
}
