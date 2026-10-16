using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Collector.Commands;

// Downloads every class's skill grid (column, row) from dfmax and matches it to our skills by icon.
// dfmax uses the Korean server's skill ids, so ids cannot be joined; the icon art is identical, so images are compared instead.
partial class FetchSkillLayoutCommand : ICommand
{
    public string Name => "fetch-skill-layout";
    public string Usage => "fetch-skill-layout                        download the in-game skill grid of every class (about 70 pages and 3,000 icons, resumable) and match it to our skills";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    const int PauseMs = 250;
    const int FeatureSize = 16;
    const double AcceptSimilarity = 0.9;

    record GridSkill(string Id, string Type, int X, int Y, string ImagePath);

    // dfmax's base class names -> ours. Its Imperial Knight (제국기사) page and any other Korean-only class are skipped.
    static readonly Dictionary<string, string> BaseClasses = new()
    {
        ["귀검사(남)"] = "Slayer (M)", ["귀검사(여)"] = "Slayer (F)", ["격투가(남)"] = "Fighter (M)", ["격투가(여)"] = "Fighter (F)",
        ["거너(남)"] = "Gunner (M)", ["거너(여)"] = "Gunner (F)", ["마법사(남)"] = "Mage (M)", ["마법사(여)"] = "Mage (F)",
        ["프리스트(남)"] = "Priest (M)", ["프리스트(여)"] = "Priest (F)", ["도적"] = "Thief", ["나이트"] = "Knight", ["마창사"] = "Demonic Lancer",
        ["총검사"] = "Agent", ["아처"] = "Archer", ["크리에이터"] = "Creator", ["다크나이트"] = "Dark Knight",
    };

    [GeneratedRegex("href=\"(/jobs/[^\"]+/summary)\"")]
    private static partial Regex JobLink();

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("This command compares images with System.Drawing, which needs Windows."); return 1; }

        var dir = Path.Combine(ctx.RawDir, "dfmax");
        Directory.CreateDirectory(dir);
        using var http = Dfmax.NewClient();

        // 1. The class pages.
        var jobsHtml = await Dfmax.CachedTextAsync(http, $"{Dfmax.Host}/jobs", Path.Combine(dir, "jobs.html"), PauseMs);
        var links = JobLink().Matches(jobsHtml).Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct().ToList();
        Console.WriteLine($"{links.Count} class pages listed.");

        // 2. Each page's skill grid, and its icons.
        var grids = new List<(string Link, List<GridSkill> Skills)>();
        for (var i = 0; i < links.Count; i++)
        {
            var skills = await LoadGridAsync(http, links[i].Replace("/summary", "/skill-style"), Path.Combine(dir, $"grid_{i}.json"));
            var iconDir = Path.Combine(dir, "icons", i.ToString());
            Directory.CreateDirectory(iconDir);
            foreach (var s in skills)
            {
                var path = Path.Combine(iconDir, s.Id + ".png");
                if (File.Exists(path)) continue;
                try { await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(Dfmax.Host + "/_ipx/_/image" + Dfmax.EscapePath(s.ImagePath["/image".Length..]))); }
                catch (HttpRequestException ex) { Console.WriteLine($"    icon missing on dfmax ({s.Id[..8]}): {ex.Message}"); }
                await Task.Delay(PauseMs);
            }
            grids.Add((links[i], skills));
            Console.WriteLine($"  [{i + 1}/{links.Count}] {skills.Count} skills  {Uri.UnescapeDataString(links[i])}");
        }

        // 3. Our skill icons, one feature vector each.
        var ours = new Dictionary<string, Dictionary<string, double[]>>();
        foreach (var classDir in Directory.GetDirectories(Path.Combine(RepoRoot.Find(), "stats", "img", "skills")))
        {
            var key = Path.GetFileName(classDir);
            var features = Features(Directory.GetFiles(classDir, "*.png"));
            if (key != "badges" && features.Count > 0) ours[key] = features;
        }

        // 4. Per page: the class it belongs to, then a one-to-one match of icons.
        var catalog = ctx.LoadClassCatalog().Values.ToList();
        var result = new Dictionary<string, List<object>>();

        // 4a. Which of our classes each page is. The page's base class (Korean) narrows the candidates to our classes of that base class
        // (male and female versions share icon art). Within it, the class whose icons best match all the page's icons wins; classes of one
        // base class share their common skills, so counting votes per icon would tie. If two pages claim a class the better match keeps it.
        var claim = new Dictionary<string, (double Score, int Page)>();
        for (var i = 0; i < grids.Count; i++)
        {
            var (link, skills) = grids[i];
            var koreanBase = Uri.UnescapeDataString(link).Split('/')[2];
            if (!BaseClasses.TryGetValue(koreanBase, out var baseClass)) { Console.WriteLine($"  skipped (not in Global): {Uri.UnescapeDataString(link)}"); continue; }
            var inBase = catalog.Where(c => c.BaseClass == baseClass && ours.ContainsKey(c.Key)).Select(c => c.Key).ToList();
            var theirs = Features(skills.Select(s => Path.Combine(dir, "icons", i.ToString(), s.Id + ".png")).Where(File.Exists));
            if (inBase.Count == 0 || theirs.Count == 0) { Console.WriteLine($"  no class found for {Uri.UnescapeDataString(link)}"); continue; }

            var (classKey, score) = inBase.Select(k => (Class: k, Score: theirs.Values.Average(v => ours[k].Values.Max(o => Dot(v, o))))).MaxBy(x => x.Score);
            if (claim.TryGetValue(classKey, out var other))
            {
                Console.WriteLine($"  {classKey} claimed by two pages ({other.Score:0.000} and {score:0.000}); keeping the better one");
                if (score <= other.Score) continue;
            }
            claim[classKey] = (score, i);
        }

        // 4b. One-to-one match of the page's icons to the class's icons.
        foreach (var (classKey, (_, i)) in claim)
        {
            var skills = grids[i].Skills;
            var theirs = Features(skills.Select(s => Path.Combine(dir, "icons", i.ToString(), s.Id + ".png")).Where(File.Exists));
            var candidates = theirs.SelectMany(t => ours[classKey].Select(o => (Theirs: t.Key, Ours: o.Key, Sim: Dot(t.Value, o.Value)))).OrderByDescending(p => p.Sim);
            var usedTheirs = new HashSet<string>();
            var usedOurs = new HashSet<string>();
            var placed = new List<object>();
            foreach (var p in candidates)
            {
                if (p.Sim < AcceptSimilarity) break;
                if (!usedTheirs.Add(p.Theirs)) continue;
                if (!usedOurs.Add(p.Ours)) { usedTheirs.Remove(p.Theirs); continue; }
                var g = skills.First(s => s.Id == p.Theirs);
                placed.Add(new { id = p.Ours, x = g.X, y = g.Y, similarity = Math.Round(p.Sim, 3) });
            }
            result[classKey] = placed;
            Console.WriteLine($"  {classKey}: {placed.Count} of {skills.Count} grid skills matched to {ours[classKey].Count} of ours");
        }

        Directory.CreateDirectory(ctx.ReferenceDir);
        await File.WriteAllTextAsync(Path.Combine(ctx.ReferenceDir, "skilllayout.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote skilllayout.json for {result.Count} classes.");
        return 0;
    }

    static async Task<List<GridSkill>> LoadGridAsync(HttpClient http, string linkPath, string cacheFile)
    {
        if (!File.Exists(cacheFile))
        {
            var html = await http.GetStringAsync(Dfmax.Host + Dfmax.EscapePath(System.Net.WebUtility.HtmlDecode(linkPath)));
            await Task.Delay(PauseMs);
            var root = Dfmax.Payload(html);

            var skills = new Dictionary<string, GridSkill>();
            foreach (var o in root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var id = Dfmax.Text(root, o, "skillId");
                var x = Dfmax.Number(root, o, "x");
                var y = Dfmax.Number(root, o, "y");
                var path = Dfmax.Text(root, o, "imagePath");
                if (id != null && x != null && y != null && path != null && !skills.ContainsKey(id))
                    skills[id] = new GridSkill(id, Dfmax.Text(root, o, "type") ?? "", x.Value, y.Value, path);
            }
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(skills.Values));
        }
        return JsonSerializer.Deserialize<List<GridSkill>>(await File.ReadAllTextAsync(cacheFile))!;
    }

    // 16x16 colour thumbnail over black, centred and normalised, so two copies of the same art compare as 1.0 whatever their size.
    // File name (the skill id) -> feature vector.
    [SupportedOSPlatform("windows")]
    static Dictionary<string, double[]> Features(IEnumerable<string> files) =>
        files.ToDictionary(f => Path.GetFileNameWithoutExtension(f), Feature);

    [SupportedOSPlatform("windows")]
    static double[] Feature(string path)
    {
        using var source = new Bitmap(path);
        using var small = new Bitmap(FeatureSize, FeatureSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, FeatureSize, FeatureSize));
        }
        var v = new double[FeatureSize * FeatureSize * 3];
        for (var y = 0; y < FeatureSize; y++)
            for (var x = 0; x < FeatureSize; x++)
            {
                var c = small.GetPixel(x, y);
                var i = (y * FeatureSize + x) * 3;
                v[i] = c.R; v[i + 1] = c.G; v[i + 2] = c.B;
            }
        var mean = v.Average();
        for (var i = 0; i < v.Length; i++) v[i] -= mean;
        var norm = Math.Sqrt(v.Sum(d => d * d)) + 1e-9;
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }

    static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}
