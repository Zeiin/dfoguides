using System.Text.Json;
using System.Text.Json.Nodes;

namespace Collector.Commands;

// The scheduled pull: rescans the rosters, then re-pulls only new and changed characters plus the oldest slice, keeping the rest from last time.
class RefreshCommand : ICommand
{
    public string Name => "refresh";
    public string Usage => "refresh [delayMs] [budgetMinutes] [keys]  incremental pull of every class (resumable; stops cleanly when the budget is used up; default delay 250 ms, no budget; keys = comma-separated class keys to limit it)";
    public int MinArgs => 0;
    public bool NeedsApiKey => true;

    const int FameFloor = 91_582;
    static readonly string[] Suffixes = ["equipment", "oath", "skill_style", "avatar", "creature"];

    sealed class ClassRun(string key, List<RosterEntry> roster, Dictionary<string, CharState> state)
    {
        public string Key { get; } = key;
        public List<RosterEntry> Roster { get; } = roster;
        public Dictionary<string, CharState> State { get; } = state;
    }

    sealed class Counters { public int Pulled, Kept, Dropped; }

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        if (args.Length > 0)
        {
            ctx.Client.MinGap = TimeSpan.FromMilliseconds(int.Parse(args[0]));
            Console.WriteLine($"Pause between calls: {args[0]} ms");
        }
        var budget = args.Length > 1 ? int.Parse(args[1]) : 0;
        var only = args.Length > 2 ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet() : null;
        DateTime? deadline = budget > 0 ? DateTime.UtcNow.AddMinutes(budget) : null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var families = SetEmblems.ReadFamilies(Path.Combine(ctx.ReferenceDir, "setfamilies.json"));
        var statePath = Path.Combine(ctx.RawDir, "refresh_state.json");
        var state = RefreshState.Load(statePath);

        // 1. Today's rosters. A first run adopts characters already on disk (the old roster file and the files' dates).
        var runs = new List<ClassRun>();
        var jobs = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ctx.RawDir, "jobs.json")))!["rows"]!.AsArray();
        foreach (var baseClass in jobs)
        {
            foreach (var chain in baseClass!["rows"]!.AsArray())
            {
                var last = chain!;
                while (last["next"] != null) last = last["next"]!;
                var key = ClassKey.From(baseClass["jobName"]!.GetValue<string>(), last["jobGrowName"]!.GetValue<string>());
                if (only != null && !only.Contains(key)) continue;

                if (!state.TryGetValue(key, out var known)) state[key] = known = new();
                if (known.Count == 0 && File.Exists(ctx.RosterPath(key))) Adopt(ctx, key, await ctx.LoadRosterAsync(key), known);

                var roster = await Roster.BuildAsync(ctx.Client, baseClass["jobId"]!.GetValue<string>(), last["jobGrowId"]!.GetValue<string>(), floorFame: FameFloor);
                runs.Add(new ClassRun(key, roster, known));
                Console.WriteLine($"{key}: {roster.Count} in the roster, {known.Count} on disk");
            }
        }
        RefreshState.Save(statePath, state);

        // 2. Smallest classes first, starting one place further along each day so a cut-off run does not always skip the same classes.
        var order = runs.Where(r => r.Roster.Count > 0).OrderBy(r => r.Roster.Count).ToList();
        if (order.Count > 0) order = order.Skip(today.DayNumber % order.Count).Concat(order.Take(today.DayNumber % order.Count)).ToList();

        var counters = new Counters();
        var requestsAtStart = ctx.Client.RequestCount;
        var stoppedEarly = false;
        foreach (var run in order)
        {
            try
            {
                stoppedEarly = !await RefreshClassAsync(ctx, run, today, deadline, families, counters);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"STOPPED EARLY: {ex.Message}");
                stoppedEarly = true;
            }
            RefreshState.Save(statePath, state);
            if (stoppedEarly) break;
        }

        // 3. The rosters analysis reads: everything on disk, so characters not reached today keep yesterday's snapshot.
        var total = 0;
        var covered = 0;
        foreach (var run in runs)
        {
            total += run.Roster.Count;
            covered += run.Roster.Count(e => run.State.ContainsKey(e.CharacterId));
            var final = run.State.Select(kv => new RosterEntry(kv.Value.ServerId, kv.Key, kv.Value.Fame)).OrderByDescending(e => e.Fame).ToList();
            await File.WriteAllTextAsync(ctx.RosterPath(run.Key), JsonSerializer.Serialize(final, Json.Pretty));
        }

        var coverage = total == 0 ? 0 : (double)covered / total;
        Directory.CreateDirectory(ctx.OutDir);
        // Also kept in raw/ (which the workflow caches) so a later re-export knows whether the data behind it is complete.
        string[] summary =
        [
            $"coverage={coverage.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}",
            $"limited={(only != null ? "true" : "false")}",
            $"stoppedEarly={(stoppedEarly ? "true" : "false")}",
            $"pulled={counters.Pulled}", $"keptOriginal={counters.Kept}", $"dropped={counters.Dropped}",
            $"requests={ctx.Client.RequestCount - requestsAtStart}",
        ];
        await File.WriteAllLinesAsync(Path.Combine(ctx.OutDir, "refresh_summary.txt"), summary);
        await File.WriteAllLinesAsync(Path.Combine(ctx.RawDir, "refresh_summary.txt"), summary);
        Console.WriteLine($"Refresh done: {counters.Pulled} pulled, {counters.Kept} kept as original, {counters.Dropped} dropped, coverage {coverage:P1}, {ctx.Client.RequestCount - requestsAtStart} requests{(stoppedEarly ? " (stopped early)" : "")}.");
        return 0;
    }

    // Returns false when the pull was cut short.
    static async Task<bool> RefreshClassAsync(CommandContext ctx, ClassRun run, DateOnly today, DateTime? deadline, IReadOnlyDictionary<string, string> families, Counters counters)
    {
        var chars = ctx.CharsDir(run.Key);
        var incoming = Path.Combine(chars, ".incoming");
        Directory.CreateDirectory(incoming);

        var todo = Refresh.Select(run.Roster, run.State, today);
        Console.WriteLine($"== {run.Key}: {todo.Count} to pull of {run.Roster.Count}");
        var complete = await CharacterPuller.PullAsync(ctx.Client, todo, incoming, deadline: deadline);
        foreach (var entry in todo) Apply(chars, incoming, entry, run.State, today, families, counters);
        if (!complete) return false;

        // Characters that left the roster (dropped below the cutoff, changed class). Kept only if they fell by switching into Magic Domain.
        var inRoster = run.Roster.Select(e => e.CharacterId).ToHashSet();
        var missing = run.State.Where(kv => !inRoster.Contains(kv.Key) && (!kv.Value.KeptOriginal || Refresh.AgeDays(kv.Value.Checked, today) >= Refresh.StaleDays))
            .Select(kv => new RosterEntry(kv.Value.ServerId, kv.Key, kv.Value.Fame)).ToList();
        if (missing.Count == 0) return true;

        Console.WriteLine($"   checking {missing.Count} characters that left the roster");
        complete = await CharacterPuller.PullAsync(ctx.Client, missing, incoming, only: ["equipment", "oath"], deadline: deadline);
        foreach (var entry in missing) Probe(chars, incoming, entry, run, today, families, counters);
        return complete;
    }

    // Replaces the saved snapshot with the freshly pulled one, unless the Magic Domain rule says to keep the original.
    static void Apply(string chars, string incoming, RosterEntry entry, Dictionary<string, CharState> state, DateOnly today, IReadOnlyDictionary<string, string> families, Counters counters)
    {
        var id = entry.CharacterId;
        if (!Suffixes.All(s => File.Exists(Path.Combine(incoming, $"{id}_{s}.json")))) return; // not finished: stays in incoming, retried next run

        if (state.TryGetValue(id, out var old) && File.Exists(Path.Combine(chars, $"{id}_equipment.json")))
        {
            var before = Refresh.ReadWear(chars, id);
            var after = Refresh.ReadWear(incoming, id);
            if (Refresh.KeepOriginal(old.Fame, entry.Fame, Refresh.WearsMagicDomain(before.Set, before.Oath, families), Refresh.WearsMagicDomain(after.Set, after.Oath, families)))
            {
                DeleteFiles(incoming, id);
                state[id] = old with { KeptOriginal = true, SeenFame = entry.Fame, Checked = Refresh.Day(today) };
                counters.Kept++;
                return;
            }
        }

        foreach (var suffix in Suffixes) File.Move(Path.Combine(incoming, $"{id}_{suffix}.json"), Path.Combine(chars, $"{id}_{suffix}.json"), overwrite: true);
        state[id] = new CharState(entry.ServerId, entry.Fame, Refresh.Day(today));
        counters.Pulled++;
    }

    // A character that left the roster: its current gear decides whether the original snapshot stays.
    static void Probe(string chars, string incoming, RosterEntry entry, ClassRun run, DateOnly today, IReadOnlyDictionary<string, string> families, Counters counters)
    {
        var id = entry.CharacterId;
        var path = Path.Combine(incoming, $"{id}_equipment.json");
        if (!File.Exists(path) || !File.Exists(Path.Combine(incoming, $"{id}_oath.json"))) return; // try again next run

        var current = JsonSerializer.Deserialize<EquipmentResponse>(File.ReadAllText(path), Json.Options);
        var sameClass = current?.JobName != null && current.JobGrowName != null && ClassKey.From(current.JobName, current.JobGrowName) == run.Key;
        var old = run.State[id];
        var before = Refresh.ReadWear(chars, id);
        var after = Refresh.ReadWear(incoming, id);
        var keep = sameClass && current?.Fame is { } fame
            && Refresh.KeepOriginal(old.Fame, fame, Refresh.WearsMagicDomain(before.Set, before.Oath, families), Refresh.WearsMagicDomain(after.Set, after.Oath, families));

        DeleteFiles(incoming, id);
        if (keep)
        {
            run.State[id] = old with { KeptOriginal = true, SeenFame = current!.Fame!.Value, Checked = Refresh.Day(today) };
            counters.Kept++;
            return;
        }
        DeleteFiles(chars, id);
        run.State.Remove(id);
        counters.Dropped++;
    }

    static void DeleteFiles(string dir, string id)
    {
        foreach (var file in Directory.GetFiles(dir, $"{id}_*.json")) File.Delete(file);
    }

    // First run: characters already on disk count as pulled on the day their files were written.
    static void Adopt(CommandContext ctx, string key, List<RosterEntry> oldRoster, Dictionary<string, CharState> state)
    {
        var chars = ctx.CharsDir(key);
        foreach (var entry in oldRoster)
        {
            var files = Suffixes.Select(s => Path.Combine(chars, $"{entry.CharacterId}_{s}.json")).ToList();
            if (!files.All(File.Exists)) continue;
            var written = DateOnly.FromDateTime(files.Max(File.GetLastWriteTimeUtc));
            state[entry.CharacterId] = new CharState(entry.ServerId, entry.Fame, Refresh.Day(written));
        }
        Console.WriteLine($"   adopted {state.Count} characters already on disk");
    }
}
