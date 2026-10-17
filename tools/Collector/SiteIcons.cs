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

        public string? Best(string category, string name) => Ranked(category, name).FirstOrDefault();

        // Every id seen for a name, most common first (one title name can come with several item ids). A name with no ids of its own (the
        // level-free "Voyage Captain Celena Platinum" of the group pages) uses the ids of its levelled forms ("... Platinum [80]").
        public IEnumerable<string> Ranked(string category, string name)
        {
            if (!_byCategory.TryGetValue(category, out var names)) return [];
            if (names.TryGetValue(name, out var own)) return own.OrderByDescending(kv => kv.Value).Select(kv => kv.Key);

            var pooled = new Dictionary<string, int>();
            foreach (var (other, ids) in names.Where(n => n.Key.StartsWith(name + " [")))
                foreach (var (id, count) in ids) pooled[id] = pooled.GetValueOrDefault(id) + count;
            return pooled.OrderByDescending(kv => kv.Value).Select(kv => kv.Key);
        }

        // "<category>|<displayed name>" -> the item name the API reported (dfogang's icon services want it). A level-free name uses a levelled form's.
        public Dictionary<string, string> ItemNames { get; } = new();

        public string? ItemName(string category, string name) =>
            ItemNames.TryGetValue($"{category}|{name}", out var exact)
                ? exact
                : ItemNames.FirstOrDefault(kv => kv.Key.StartsWith($"{category}|{name} [")).Value;
    }

    // The file name of a title's hand-made icon: the title without its Platinum level, with characters a file name cannot hold replaced by "-".
    internal static string ManualTitleFileName(string shownName)
    {
        var withoutLevel = System.Text.RegularExpressions.Regex.Replace(shownName, @" \[\d+\]$", "");
        return string.Concat(withoutLevel.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) + ".png";
    }

    // Copies the hand-made icon of a title into the site's icon folder and returns its file name; null when there is none.
    static string? ManualTitleIcon(CommandContext ctx, string shownName, string iconDir)
    {
        var fileName = ManualTitleFileName(shownName);
        var source = Path.Combine(ctx.ReferenceDir, "titleicons", fileName);
        if (!File.Exists(source)) return null;
        var target = "title-" + fileName.Replace(' ', '-').ToLowerInvariant();
        File.Copy(source, Path.Combine(iconDir, target), overwrite: true);
        return target;
    }

    // Item ids that no image source has; remembered so the daily run does not ask again.
    const string UnavailableFile = "unavailable.txt";

    static async Task<bool> TryDownloadAsync(HttpClient http, string url, string path, int pauseMs)
    {
        using var response = await http.GetAsync(url);
        await Task.Delay(pauseMs);
        if (!response.IsSuccessStatusCode) return false;
        await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync());
        return true;
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
        var unavailablePath = Path.Combine(iconDir, UnavailableFile);
        var failedIds = File.Exists(unavailablePath) ? File.ReadAllLines(unavailablePath).ToHashSet() : new HashSet<string>();

        // Titles and creatures: Neople's image server lacks many of them (and every Platinum level), so dfogang's icon service (which serves the
        // same Neople artwork) is tried next. Each item id seen for the name is tried in turn.
        async Task<string?> ServiceIconAsync(string category, string name)
        {
            // A hand-made icon (reference/titleicons/<title>.png) wins: it is for titles no image source has.
            if (category == "titles" && ManualTitleIcon(ctx, name, iconDir) is { } manual) return manual;

            var ids = votes.Ranked(category, name).ToList();
            foreach (var id in ids)
                if (File.Exists(Path.Combine(iconDir, $"{id}.png"))) return $"{id}.png";

            foreach (var id in ids.Where(i => !failedIds.Contains(i)))
                if (await TryDownloadAsync(http, $"https://img-api.dfoneople.com/df/items/{id}", Path.Combine(iconDir, $"{id}.png"), PauseMs)) { downloaded++; return $"{id}.png"; }

            if (votes.ItemName(category, name) is { } itemName)
                foreach (var id in ids.Where(i => !failedIds.Contains(i)))
                    if (await TryDownloadAsync(http, $"https://api.dfogang.com/api/{(category == "titles" ? "title-icon" : "creature-icon")}/{id}/{Uri.EscapeDataString(itemName)}", Path.Combine(iconDir, $"{id}.png"), 250)) { downloaded++; return $"{id}.png"; }

            foreach (var id in ids) failedIds.Add(id);
            return null;
        }

        foreach (var (category, wanted) in names)
        {
            iconByName[category] = new();
            if (category is "titles" or "creatures")
            {
                foreach (var name in wanted)
                {
                    var file = await ServiceIconAsync(category, name);
                    if (file != null) iconByName[category][name] = file;
                    else missing.Add($"{category}: {name}");
                }
                // A Platinum level with no image of its own borrows another level of the same title or creature (the artwork is the same).
                foreach (var name in wanted.Where(n => !iconByName[category].ContainsKey(n) && n.Contains(" Platinum [")))
                {
                    var prefix = name[..name.IndexOf(" [", StringComparison.Ordinal)];
                    var sibling = iconByName[category].FirstOrDefault(kv => kv.Key.StartsWith(prefix + " ["));
                    if (sibling.Value != null) iconByName[category][name] = sibling.Value;
                }
                continue;
            }

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

        await File.WriteAllLinesAsync(unavailablePath, failedIds.Order());
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
                        votes.Add("exalted", item.ItemName, item.ItemId);
                    if (item.ItemName.Contains("Distinct"))
                        votes.Add("distinct", CharacterReader.DistinctName(item.ItemName), item.ItemId);
                }

                // A set is shown by its normal top piece.
                var setName = SetNames.Of(equipment);
                var top = items.FirstOrDefault(i => i.SlotId == "JACKET" && !i.ItemName.Contains("Distinct") && !i.ItemName.Contains("Starter"));
                if (setName != "(none)") votes.Add("sets", setName, top?.ItemId);

                // An oath set is shown by its crystal.
                votes.Add("oath", oath?.Oath?.SetInfo?.SetName, oath?.Oath?.Crystal?.FirstOrDefault()?.ItemId);

                if (items.FirstOrDefault(i => i.SlotId == "TITLE") is { } titleItem)
                {
                    var (titleName, titleLevel) = CharacterReader.ParseTitle(titleItem.ItemName);
                    var shownTitle = titleLevel is { } tl ? $"{titleName} [{tl}]" : titleName;
                    votes.Add("titles", shownTitle, titleItem.ItemId);
                    votes.ItemNames[$"titles|{shownTitle}"] = titleItem.ItemName;
                }

                if (creature?.Creature?.ItemName is { } creatureName)
                {
                    var (baseName, level) = CharacterReader.ParseTitle(creatureName);
                    var shownCreature = level is { } l ? $"{baseName} [{l}]" : baseName;
                    votes.Add("creatures", shownCreature, creature.Creature.ItemId);
                    votes.ItemNames[$"creatures|{shownCreature}"] = creatureName;
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
