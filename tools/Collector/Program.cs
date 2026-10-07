using Collector;
using Collector.Commands;

// Runs the command named by the first argument.
var command = Commands.All.FirstOrDefault(c => c.Name == args.FirstOrDefault());
if (command == null || args.Length - 1 < command.MinArgs)
{
    Console.WriteLine("Usage:");
    foreach (var c in Commands.All) Console.WriteLine($"  {c.Usage}");
    return 1;
}

var key = EnvFile.Get("NEOPLE_API_KEY");
if (command.NeedsApiKey && string.IsNullOrWhiteSpace(key))
{
    Console.WriteLine("NEOPLE_API_KEY not found (env var or .env).");
    return 1;
}

// Raw dumps and generated output live under <repo>/data (gitignored).
var dataRoot = Path.Combine(RepoRoot.Find(), "data");
var rawDir = Path.Combine(dataRoot, "raw");
var outDir = Path.Combine(dataRoot, "out");
Directory.CreateDirectory(rawDir);

var context = new CommandContext(new NeopleClient(key ?? ""), rawDir, outDir);
return await command.RunAsync(context, args[1..]);
