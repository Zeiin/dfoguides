using System.Text.Json;
using System.Text.Json.Serialization;
using Collector.Commands;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Collector;

record SiteClass(string Key, string Display, string BaseClass, string Advancement, string Role, string? Portrait, int Sample, double Share);
// One skill of a class tree; its row is the required level.
record SiteSkill(string Id, string Name, int Level, string Type, bool Icon);

record SiteData(string Generated, int CutoffFame, int TotalCharacters, List<SiteClass> Classes, Dictionary<string, object> Stats,
    Dictionary<string, Dictionary<string, string>> Icons, Dictionary<string, List<SiteSkill>> SkillTree);

// Writes the website data file and web-sized portraits into <repo>/stats.
static class SiteExport
{
    const int TopRows = 20;
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
            classes.Add(new SiteClass(c.Key, c.Display, c.BaseClass, c.Advancement, c.Role, portrait, s.Sample, 0));
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
                Want("platinum", c.PlatinumAura); Want("emblems", c.EmblemEffects); Want("exalted", c.Exalted);
            }
            else if (value is AgnosticStats a)
            {
                Want("sets", a.Sets); Want("oath", a.OathSets); Want("exalted", a.Exalted);
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

        // Skill trees from the skill lists fetch-skill-icons saved.
        var skillTree = new Dictionary<string, List<SiteSkill>>();
        foreach (var c in classes)
        {
            var listPath = Path.Combine(ctx.RawDir, $"skilllist_{c.Key}.json");
            if (!File.Exists(listPath)) continue;
            var list = JsonDocument.Parse(await File.ReadAllTextAsync(listPath)).RootElement.GetProperty("skills");
            skillTree[c.Key] = list.EnumerateArray()
                .Select(s => new SiteSkill(
                    s.GetProperty("skillId").GetString()!,
                    s.GetProperty("name").GetString()!,
                    s.GetProperty("requiredLevel").GetInt32(),
                    s.GetProperty("type").GetString()!,
                    File.Exists(Path.Combine(siteDir, "img", "skills", $"{s.GetProperty("skillId").GetString()}.png"))))
                .ToList();
        }

        var data = new SiteData(DateTime.UtcNow.ToString("yyyy-MM-dd"), cutoffFame, total, classes, stats, icons, skillTree);
        // A script file, not fetched JSON, so the page also works opened from disk.
        await File.WriteAllTextAsync(Path.Combine(siteDir, "data", "data.js"), "window.DFO_DATA = " + JsonSerializer.Serialize(data, WebJson) + ";");
        Console.WriteLine($"Exported {classes.Count} classes to {siteDir}");
    }

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
        Sets = T(s.Sets), OathSets = T(s.OathSets), OathSetOptions = T(s.OathSetOptions), Weapons = T(s.Weapons), Titles = T(s.Titles),
        DistinctBySlot = T(s.DistinctBySlot), Exalted = T(s.Exalted), BlackFangCounts = T(s.BlackFangCounts),
        // These tables need every row (each skill appears once per option).
        VpSkills = T(s.VpSkills, 80), VpSets = T(s.VpSets), EnhancementPicks = T(s.EnhancementPicks, 80), EnhancementSets = T(s.EnhancementSets),
        WeaponAvatarLevels = T(s.WeaponAvatarLevels), Creatures = T(s.Creatures),
        PlatinumTop = T(s.PlatinumTop), PlatinumBottom = T(s.PlatinumBottom), PlatinumAura = T(s.PlatinumAura),
        EmblemEffects = T(s.EmblemEffects, 80),
    };

    static AgnosticStats Trim(AgnosticStats s) => s with
    {
        Sets = T(s.Sets), OathSets = T(s.OathSets), OathSetOptions = T(s.OathSetOptions), Titles = T(s.Titles),
        DistinctBySlot = T(s.DistinctBySlot), Exalted = T(s.Exalted), BlackFangCounts = T(s.BlackFangCounts),
    };
}
