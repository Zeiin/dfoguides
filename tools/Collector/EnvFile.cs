namespace Collector;

static class RepoRoot
{
    // The folder containing .git.
    public static string Find()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }
}

static class EnvFile
{
    // Looks for .env upward from the working directory; real environment variables win.
    public static string? Get(string name)
    {
        var fromEnv = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) continue;

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('#')) continue;
                var eq = trimmed.IndexOf('=');
                if (eq <= 0 || trimmed[..eq].Trim() != name) continue;
                return trimmed[(eq + 1)..].Trim().Trim('"');
            }
        }
        return null;
    }
}
