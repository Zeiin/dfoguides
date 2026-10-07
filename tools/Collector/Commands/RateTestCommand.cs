using System.Text;

namespace Collector.Commands;

// Steps the request rate up until the API pushes back. Never run during a pull (shared rate budget).
class RateTestCommand : ICommand
{
    public string Name => "ratetest";
    public string Usage => "ratetest <classKey> <seq|par> confirm      step up the request rate until the API pushes back (not during a pull)";
    public int MinArgs => 3;
    public bool NeedsApiKey => true;

    static readonly int[] DelaysMs = [250, 150, 100, 50, 25, 0];
    static readonly int[] Concurrency = [2, 4, 8, 16];
    const int RequestsPerStep = 100;
    static readonly TimeSpan CoolDown = TimeSpan.FromSeconds(30);

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        var mode = args[1];
        if (args[2] != "confirm" || mode is not ("seq" or "par"))
        {
            Console.WriteLine("Run as: ratetest <classKey> <seq|par> confirm   (and make sure no pull is running)");
            return 1;
        }

        var roster = await ctx.LoadRosterAsync(args[0]);
        var entry = roster[0];
        var path = $"/servers/{entry.ServerId}/characters/{entry.CharacterId}/equip/equipment";

        Directory.CreateDirectory(ctx.OutDir);
        var logPath = Path.Combine(ctx.OutDir, "ratetest.log");
        async Task Log(string line)
        {
            Console.WriteLine(line);
            await File.AppendAllTextAsync(logPath, $"{DateTime.Now:s} {line}{Environment.NewLine}");
        }

        await Log($"--- ratetest {mode}, {RequestsPerStep} requests per step ---");
        var steps = mode == "seq" ? DelaysMs : Concurrency;
        foreach (var step in steps)
        {
            var results = mode == "seq" ? await RunSequentialAsync(ctx, path, step) : await RunParallelAsync(ctx, path, step);
            var ok = results.Count(r => r.Status == 200);
            var label = mode == "seq" ? $"delay {step} ms" : $"{step} at once";
            var summary = new StringBuilder($"{label}: {ok}/{results.Count} ok, avg latency {results.Average(r => r.ElapsedMs):F0} ms");

            var failure = results.FirstOrDefault(r => r.Status != 200);
            if (failure != null)
            {
                summary.Append($", STOPPED: status {failure.Status}");
                if (failure.RateHeaders.Length > 0) summary.Append($", headers [{failure.RateHeaders}]");
                await Log(summary.ToString());
                await Log("First sign of pushback. Do not go faster than the previous step; pick a margin below it.");
                return 2;
            }

            if (results.Any(r => r.RateHeaders.Length > 0)) summary.Append($", headers [{results.First(r => r.RateHeaders.Length > 0).RateHeaders}]");
            await Log(summary.ToString());
            if (step != steps[^1]) await Task.Delay(CoolDown);
        }

        await Log("No pushback at the fastest step tested.");
        return 0;
    }

    // Stops at the first non-200 response.
    static async Task<List<RawResult>> RunSequentialAsync(CommandContext ctx, string path, int delayMs)
    {
        var results = new List<RawResult>();
        for (var i = 0; i < RequestsPerStep; i++)
        {
            var result = await ctx.Client.SendOnceAsync(path);
            results.Add(result);
            if (result.Status != 200) break;
            if (delayMs > 0) await Task.Delay(delayMs);
        }
        return results;
    }

    static async Task<List<RawResult>> RunParallelAsync(CommandContext ctx, string path, int concurrency)
    {
        var results = new List<RawResult>();
        while (results.Count < RequestsPerStep)
        {
            // Starts all requests together and waits for them.
            var batch = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => ctx.Client.SendOnceAsync(path)));
            results.AddRange(batch);
            if (batch.Any(r => r.Status != 200)) break;
        }
        return results;
    }
}
