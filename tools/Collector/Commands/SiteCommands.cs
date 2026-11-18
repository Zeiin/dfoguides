namespace Collector.Commands;

class ExportSiteCommand : ICommand
{
    public string Name => "export-site";
    public string Usage => "export-site [skip-icons] [icon-budget=<minutes>]  write the website data, portraits and icons into /stats (run analyze-every first); the budget (default 60) limits the downloading of character icons per run";
    public int MinArgs => 0;
    public bool NeedsApiKey => false;

    public async Task<int> RunAsync(CommandContext ctx, string[] args)
    {
        const string BudgetArg = "icon-budget=";
        var budget = args.FirstOrDefault(a => a.StartsWith(BudgetArg)) is { } given ? int.Parse(given[BudgetArg.Length..]) : 60;
        await SiteExport.ExportAsync(ctx, cutoffFame: 91_582, reuseIcons: args.Contains("skip-icons"), iconBudget: TimeSpan.FromMinutes(budget));
        return 0;
    }
}
