using System.Text.Json.Nodes;

namespace Collector;

record RosterEntry(string ServerId, string CharacterId, int Fame);

static class Sampling
{
    // Same rule for pull and analysis so samples line up.
    public static List<RosterEntry> EvenlySpaced(IReadOnlyList<RosterEntry> roster, double fraction)
    {
        var take = Math.Max(1, (int)Math.Round(roster.Count * fraction));
        return Enumerable.Range(0, take).Select(i => roster[i * roster.Count / take]).ToList();
    }
}

static class ClassKey
{
    // "Archer" + "Neo: Chimera" -> "Archer_Neo_Chimera". Safe for file names and unique per class.
    public static string From(string baseName, string growName) =>
        System.Text.RegularExpressions.Regex.Replace($"{baseName}_{growName}", @"[^A-Za-z0-9]+", "_").Trim('_');
}

static class Roster
{
    // The fame search sees ~10,000 fame below maxFame and returns 200 rows, so slide a window down and dedupe by id.
    const int WindowWidth = 10_000;
    const int PageLimit = 200;

    // floorFame skips everything below a cutoff.
    public static async Task<List<RosterEntry>> BuildAsync(NeopleClient client, string jobId, string jobGrowId, int startFame = 170_000, int floorFame = 0)
    {
        var seen = new Dictionary<string, RosterEntry>();
        var maxFame = startFame;
        var emptyStreak = 0;

        // Stop after 3 empty windows once characters are found (8 before the first).
        while (maxFame >= floorFame && maxFame > 0 && emptyStreak < (seen.Count == 0 ? 8 : 3))
        {
            var minFame = Math.Max(floorFame, maxFame - WindowWidth);
            var json = await client.GetAsync("/servers/all/characters-fame",
                $"jobId={jobId}&jobGrowId={jobGrowId}&isAllJobGrow=false&limit={PageLimit}&minFame={minFame}&maxFame={maxFame}");
            var rows = JsonNode.Parse(json)?["rows"]?.AsArray() ?? [];

            var lowest = int.MaxValue;
            foreach (var row in rows)
            {
                var fame = row!["fame"]!.GetValue<int>();
                lowest = Math.Min(lowest, fame);
                var id = row["characterId"]!.GetValue<string>();
                seen.TryAdd(id, new RosterEntry(row["serverId"]!.GetValue<string>(), id, fame));
            }

            emptyStreak = rows.Count == 0 ? emptyStreak + 1 : 0;
            // Full page: continue from the lowest fame seen. Otherwise jump past the window.
            var previousMax = maxFame;
            maxFame = rows.Count >= PageLimit ? lowest : minFame - 1;
            if (maxFame >= previousMax) maxFame = previousMax - 1; // no progress guard
            Console.WriteLine($"  next window ends at {maxFame}: {seen.Count} characters so far");
        }

        return seen.Values.Where(e => e.Fame >= floorFame).OrderByDescending(e => e.Fame).ToList();
    }
}
