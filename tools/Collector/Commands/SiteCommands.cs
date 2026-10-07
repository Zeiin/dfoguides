namespace Collector.Commands;

class ExportSiteCommand : ICommand
{
    public string Name => "export-site";
    public string Usage => "export-site [skip-icons]                  write the website data, portraits and icons into /stats (run analyze-every first)";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        await SiteExport.ExportAsync(ctx, cutoffFame: 91_582, reuseIcons: args.Contains("skip-icons"));
        return 0;
    }
}
