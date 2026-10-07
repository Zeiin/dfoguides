using System.Text.Json;
using Collector.Commands;

namespace Collector;

// Finds an item id for each name shown on the page and downloads its icon once.
static class SiteIcons
{
    const int PauseMs = 120;

    // category -> name -> (itemId -> count); the most common id wins.
    sealed class Votes
    {
        readonly Dictionary<string, Dictionary<string, Dictionary<string, int>>> _byCategory = new();

        public void Add(string category, string? name, string? id)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) return;
            if (!_byCategory.TryGetValue(category, out var names)) _byCategory[category] = names = new();
            if (!names.TryGetValue(name, out var ids)) names[name] = ids = new();
            ids[id] = ids.GetValueOrDefault(id) + 1;
        }

        public string? Best(string category, string name) =>
            _byCategory.TryGetValue(category, out var names) && names.TryGetValue(name, out var ids)
                ? ids.MaxBy(kv => kv.Value).Key
                : null;
    }

    // Returns category -> name -> icon file name.
    public static async Task<Dictionary<string, Dictionary<string, string>>> BuildAsync(
        CommandContext ctx, string iconDir, int cutoffFame, Dictionary<string, HashSet<string>> names)
    {
        var votes = await CollectAsync(ctx, cutoffFame);
        Directory.CreateDirectory(iconDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("dfoguides-tools/0.1 (personal hobby project)");

        var iconByName = new Dictionary<string, Dictionary<string, string>>();
        var missing = new List<string>();
        var downloaded = 0;
        var failedIds = new HashSet<string>();

        foreach (var (category, wanted) in names)
        {
            iconByName[category] = new();
            foreach (var name in wanted)
            {
                var id = votes.Best(category, name);
                if (id == null || failedIds.Contains(id)) { missing.Add($"{category}: {name}"); continue; }

                var file = $"{id}.png";
                var path = Path.Combine(iconDir, file);
                if (!File.Exists(path))
                {
                    using var response = await http.GetAsync($"https://img-api.dfoneople.com/df/items/{id}");
                    await Task.Delay(PauseMs);
                    if (!response.IsSuccessStatusCode) { failedIds.Add(id); missing.Add($"{category}: {name}"); continue; }
                    await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync());
                    downloaded++;
                }
                iconByName[category][name] = file;
            }
        }

        Console.WriteLine($"Icons: {downloaded} downloaded, {iconByName.Sum(c => c.Value.Count)} names matched, {missing.Count} without an icon.");
        await File.WriteAllLinesAsync(Path.Combine(ctx.OutDir, "icons_missing.txt"), missing);
        return iconByName;
    }

    // Records which item id carried each displayed name.
    static async Task<Votes> CollectAsync(CommandContext ctx, int cutoffFame)
    {
        var votes = new Votes();
        foreach (var classKey in ctx.ScannedClassKeys())
        {
            var dir = ctx.CharsDir(classKey);
            if (!Directory.Exists(dir)) continue;

            foreach (var entry in (await ctx.LoadRosterAsync(classKey)).Where(e => e.Fame >= cutoffFame))
            {
                async Task<T?> Load<T>(string suffix)
                {
                    var path = Path.Combine(dir, $"{entry.CharacterId}_{suffix}.json");
                    return File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Json.Options) : default;
                }

                var equipment = await Load<EquipmentResponse>("equipment");
                var avatar = await Load<AvatarResponse>("avatar");
                var creature = await Load<CreatureResponse>("creature");
                var oath = await Load<OathResponse>("oath");

                var items = equipment?.Equipment ?? [];
                foreach (var item in items)
                {
                    if (item.SlotId == "WEAPON") votes.Add("weapons", item.ItemName, item.ItemId);
                    if (CharacterReader.ExaltedItems.TryGetValue(item.SlotId, out var exalted) && exalted == item.ItemName)
                        votes.Add("exalted", SlotNames.Display(item.SlotId), item.ItemId);
                }

                // A set is shown by its normal top piece.
                var tierLabel = equipment?.SetItemInfo?.FirstOrDefault()?.SetItemName;
                var top = items.FirstOrDefault(i => i.SlotId == "JACKET" && !i.ItemName.Contains("Distinct") && !i.ItemName.Contains("Starter"));
                if (tierLabel != null) votes.Add("sets", SetNames.Display(tierLabel), top?.ItemId);

                // An oath set is shown by its crystal.
                votes.Add("oath", oath?.Oath?.SetInfo?.SetName, oath?.Oath?.Crystal?.FirstOrDefault()?.ItemId);

                if (creature?.Creature?.ItemName is { } creatureName)
                {
                    var (baseName, level) = CharacterReader.ParseTitle(creatureName);
                    votes.Add("creatures", level is { } l ? $"{baseName} [{l}]" : baseName, creature.Creature.ItemId);
                }

                var slots = avatar?.Avatar ?? [];
                var weaponAvatar = slots.FirstOrDefault(s => s.SlotId == "WEAPON");
                if (weaponAvatar?.ItemName is { } avatarName && CharacterReader.RareCloneLevel(avatarName) is { } cloneLevel)
                    votes.Add("weaponAvatars", $"Rare Clone [{cloneLevel}Lv]", weaponAvatar.ItemId);

                foreach (var emblem in slots.SelectMany(s => s.Emblems ?? []).Where(e => !string.IsNullOrEmpty(e.ItemName)))
                {
                    if (emblem.SlotColor == "Platinum") votes.Add("platinum", Emblems.PlatinumSkill(emblem.ItemName!), emblem.ItemId);
                    else if (!string.IsNullOrEmpty(emblem.SlotColor) && Emblems.Effect(emblem.ItemName!) is { } effect)
                        votes.Add("emblems", $"{emblem.SlotColor}: {effect}", emblem.ItemId);
                }
            }
        }
        return votes;
    }
}
