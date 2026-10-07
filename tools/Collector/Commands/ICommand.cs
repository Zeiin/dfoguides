using System.Text.Json;
using System.Text.Json.Nodes;

namespace Collector.Commands;

// One command-line command. Program.cs finds the right one by Name and runs it.
interface ICommand
{
    string Name { get; }
    string Usage { get; }       // "name <required> [optional]   short description"
    int MinArgs { get; }        // required arguments after the command name
    bool NeedsApiKey { get; }
    Task<int> RunAsync(CommandContext ctx, string[] args);   // args excludes the command name
}

// Shared by every command: client, folders, file helpers.
record CommandContext(NeopleClient Client, string RawDir, string OutDir)
{
    // Saves a raw response, indented for reading.
    public async Task SaveRawAsync(string name, string json)
    {
        var formatted = JsonSerializer.Serialize(JsonNode.Parse(json), Json.Pretty);
        await File.WriteAllTextAsync(Path.Combine(RawDir, name), formatted);
        Console.WriteLine($"  saved {name}");
    }

    // Display names by class key.
    public Dictionary<string, ClassInfo> LoadClassCatalog() =>
        ClassCatalog.FromJobs(File.ReadAllText(Path.Combine(RawDir, "jobs.json"))).ToDictionary(c => c.Key);

    public string PortraitDir => Path.Combine(OutDir, "..", "art", "classes");

    // Hand-made art wins: a ready 240x300 PNG, then a transparent PNG, then the downloaded JPEG.
    public string? FindPortrait(string classKey) =>
        new[] { $"{classKey}_240x300.png", $"{classKey}.png", $"{classKey}.jpeg" }
            .Select(f => Path.Combine(PortraitDir, f)).FirstOrDefault(File.Exists);

    // Website file name for a portrait: PNG keeps transparency, JPEG art is saved as .jpg.
    public string? PortraitSiteName(string classKey) =>
        FindPortrait(classKey) is { } file ? $"{classKey}{(file.EndsWith(".jpeg") ? ".jpg" : ".png")}" : null;

    public string RosterPath(string classKey) => Path.Combine(RawDir, $"roster_{classKey}.json");
    public string CharsDir(string classKey) => Path.Combine(RawDir, $"chars_{classKey}");

    public async Task<List<RosterEntry>> LoadRosterAsync(string classKey) =>
        JsonSerializer.Deserialize<List<RosterEntry>>(await File.ReadAllTextAsync(RosterPath(classKey)))!;

    // Every class with a saved roster (the *_full backup is not a class).
    public IEnumerable<string> ScannedClassKeys() =>
        Directory.GetFiles(RawDir, "roster_*.json")
            .Where(f => !f.EndsWith("_full.json"))
            .Select(f => Path.GetFileNameWithoutExtension(f)["roster_".Length..])
            .Order();
}

static class Commands
{
    public static readonly ICommand[] All =
    [
        new JobsCommand(), new ProbeCommand(), new CharacterCommand(), new GetCommand(), new DamageTypesCommand(),
        new ScanCommand(), new RosterCommand(), new PullCommand(), new SampleCommand(), new SampleAllCommand(), new PullAllCommand(),
        new AnalyzeCommand(), new AnalyzeAllCommand(), new AnalyzeEveryCommand(),
        new RateTestCommand(), new FetchArtCommand(), new FetchSkillIconsCommand(), new ExportSiteCommand(),
    ];
}
