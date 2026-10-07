namespace Collector;

static class CharacterPuller
{
    static readonly (string Suffix, string Path)[] Endpoints =
    [
        ("equipment", "equip/equipment"),
        ("oath", "equip/oath"),
        ("skill_style", "skill/style"),
        ("avatar", "equip/avatar"),
        ("creature", "equip/creature"),
    ];

    // Stop after this many failures in a row (key blocked, API down).
    const int MaxConsecutiveFailures = 20;

    // 429 responses tolerated per run.
    const int MaxRateLimitHits = 5;

    // Existing files are skipped so an interrupted run resumes.
    // `only` limits which endpoints are fetched.
    public static async Task PullAsync(NeopleClient client, IReadOnlyList<RosterEntry> roster, string outDir, IReadOnlyCollection<string>? only = null)
    {
        Directory.CreateDirectory(outDir);
        var done = 0;
        var failed = 0;
        var consecutiveFailures = 0;
        var started = DateTime.UtcNow;

        foreach (var entry in roster)
        {
            foreach (var (suffix, path) in Endpoints.Where(e => only == null || only.Contains(e.Suffix)))
            {
                var file = Path.Combine(outDir, $"{entry.CharacterId}_{suffix}.json");
                if (File.Exists(file)) continue;

                try
                {
                    var body = await client.GetAsync($"/servers/{entry.ServerId}/characters/{entry.CharacterId}/{path}");
                    // Write then rename so an interrupted write cannot leave a half file.
                    var temp = file + ".tmp";
                    await File.WriteAllTextAsync(temp, body);
                    File.Move(temp, file, overwrite: true);
                    consecutiveFailures = 0;
                }
                catch (HttpRequestException ex)
                {
                    failed++;
                    Console.WriteLine($"  failed {entry.CharacterId[..8]} {suffix}: {ex.Message}");
                    if (++consecutiveFailures >= MaxConsecutiveFailures)
                        throw new InvalidOperationException($"{MaxConsecutiveFailures} failures in a row, stopping.", ex);
                }
            }

            // The API is pushing back: stop.
            if (client.RateLimitHits >= MaxRateLimitHits)
                throw new InvalidOperationException($"Hit the rate limit {client.RateLimitHits} times; stopping. Raise the delay and rerun (the pull resumes).");

            if (++done % 50 == 0)
                Console.WriteLine($"  {done}/{roster.Count} characters, {failed} failed, elapsed {DateTime.UtcNow - started:hh\\:mm\\:ss}");
        }
        Console.WriteLine($"  finished {done}/{roster.Count}, {failed} failed");
    }
}
