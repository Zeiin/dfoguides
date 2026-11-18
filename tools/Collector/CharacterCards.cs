using System.Text.Json;
using System.Text.Json.Serialization;
using Collector.Commands;

namespace Collector;

// One item of a character page (gear, oath piece, creature, avatar...). Icon is a file in stats/img/items, null when none exists.
// Reinforce holds the amplification level when Amp is set; Tune is an oath crystal's tune level; Level is the level a Platinum title,
// Platinum creature or Rare Clone weapon avatar stands for (null for any other item).
record CardItem(string Name, string? Icon, string? Rarity = null, string? Slot = null, int? Reinforce = null, string? Amp = null,
    int? Refine = null, string? Set = null, string[]? Enchant = null, int? Tune = null, int? Level = null);
record CardEmblem(string Color, string Name, string? Icon);
record CardAvatar(string Slot, CardItem Item, CardEmblem[] Emblems);
record CardOath(string? Set, string? Option, string? SetRarity, CardItem? Item, CardItem[] Crystals);
record CardCreature(CardItem Item, CardItem? Twin, CardItem[] Artifacts);   // Twin = the creature's clone
// Skill id -> level / VP type / enhancement type; ids match the class skill tree in data.js.
record CardSkills(Dictionary<string, int> Levels, Dictionary<string, int> Vp, Dictionary<string, int> Enhancement);
record CharacterCard(string Id, string Class, string Name, string Server, int Fame, int? Level, CardItem[] Gear, CardOath? Oath,
    CardCreature? Creature, CardAvatar[] Avatar, CardSkills Skills);

// A character as read from the saved raw files, before the icons are known.
sealed record LoadedCharacter(RosterEntry Entry, string Class, EquipmentResponse Equipment, OathResponse? Oath, AvatarResponse? Avatar, CreatureResponse? Creature, SkillResponse Skills);

// The top characters by fame of each class (and of all classes): the data behind the leaderboard and character pages.
static class CharacterCards
{
    internal const int Size = 20;

    static readonly JsonSerializerOptions WebJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Highest fame first; the id breaks ties so the same data always gives the same order.
    internal static List<RosterEntry> Ranked(IEnumerable<RosterEntry> roster) =>
        roster.OrderByDescending(e => e.Fame).ThenBy(e => e.CharacterId, StringComparer.Ordinal).ToList();

    // The Size highest-fame characters that still belong to the class and have gear and skills on disk.
    static async Task<List<LoadedCharacter>> LoadTopAsync(CommandContext ctx, string classKey)
    {
        var dir = ctx.CharsDir(classKey);
        var top = new List<LoadedCharacter>();
        foreach (var entry in Ranked(await ctx.LoadRosterAsync(classKey)))
        {
            if (top.Count >= Size) break;
            async Task<T?> Load<T>(string suffix)
            {
                var path = Path.Combine(dir, $"{entry.CharacterId}_{suffix}.json");
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Json.Options) : default;
            }

            var equipment = await Load<EquipmentResponse>("equipment");
            var skills = await Load<SkillResponse>("skill_style");
            if (equipment?.Equipment == null || skills?.Skill?.Style == null || !CharacterReader.BelongsTo(equipment, classKey)) continue;
            top.Add(new LoadedCharacter(entry, classKey, equipment, await Load<OathResponse>("oath"), await Load<AvatarResponse>("avatar"), await Load<CreatureResponse>("creature"), skills));
        }
        return top;
    }

    // Every item id a character page draws an icon for.
    internal static IEnumerable<string> IconIds(LoadedCharacter c)
    {
        var ids = c.Equipment.Equipment!.Select(i => i.ItemId)
            .Concat([c.Oath?.Oath?.Info?.ItemId])
            .Concat((c.Oath?.Oath?.Crystal ?? []).Select(x => x.ItemId))
            .Concat([c.Creature?.Creature?.ItemId, c.Creature?.Creature?.CloneInfo?.ItemId])
            .Concat((c.Creature?.Creature?.Artifact ?? []).Select(a => a.ItemId))
            .Concat((c.Avatar?.Avatar ?? []).SelectMany(s => (s.Emblems ?? []).Select(e => e.ItemId).Prepend(s.ItemId)));
        return ids.Where(id => !string.IsNullOrEmpty(id))!;
    }

    // have = item ids with an icon file; icons = the named icons of the statistics (a title or creature the image server lacks still has one).
    internal static CharacterCard Build(LoadedCharacter c, IReadOnlySet<string> have, IReadOnlyDictionary<string, Dictionary<string, string>> icons)
    {
        string? Icon(string? id) => id != null && have.Contains(id) ? $"{id}.png" : null;
        string? Named(string category, string name) => icons.GetValueOrDefault(category)?.GetValueOrDefault(name);

        CardItem Gear(EquippedItem i)
        {
            var icon = Icon(i.ItemId);
            if (icon == null && i.SlotId == "TITLE") icon = Named("titles", CharacterReader.ShownName(i.ItemName));
            return new CardItem(i.ItemName, icon, i.ItemRarity, i.SlotId,
                i.Reinforce > 0 ? i.Reinforce : null, i.AmplificationName, i.Refine > 0 ? i.Refine : null, i.SetItemName,
                i.Enchant?.Status?.Select(s => $"{s.Name} +{s.Value}").ToArray(),
                Level: i.SlotId == "TITLE" ? CharacterReader.ParseTitle(i.ItemName).Level : null);
        }

        var oath = c.Oath?.Oath;
        var creature = c.Creature?.Creature;
        var style = c.Skills.Skill!.Style!;
        return new CharacterCard(
            c.Entry.CharacterId,
            c.Class,
            c.Equipment.CharacterName ?? "",
            c.Equipment.ServerId ?? c.Entry.ServerId,
            c.Entry.Fame,
            c.Equipment.Level,
            c.Equipment.Equipment!.Select(Gear).ToArray(),
            oath?.SetInfo == null && oath?.Info == null ? null : new CardOath(
                oath.SetInfo?.SetName, oath.SetInfo?.SetOptionName, oath.SetInfo?.SetRarityName,
                // The image server has no icon for the oath item itself; the statistics show the set by its crystal, so does this.
                oath.Info?.ItemName is { } oathName ? new CardItem(oathName, Icon(oath.Info.ItemId) ?? Named("oath", oath.SetInfo?.SetName ?? ""), oath.Info.ItemRarity) : null,
                (oath.Crystal ?? []).Where(x => x.ItemName != null).Select(x => new CardItem(x.ItemName!, Icon(x.ItemId), x.ItemRarity, Tune: x.Tune?.Level)).ToArray()),
            creature?.ItemName == null ? null : new CardCreature(
                new CardItem(creature.ItemName, Icon(creature.ItemId) ?? Named("creatures", CharacterReader.ShownName(creature.ItemName)), creature.ItemRarity,
                    Level: CharacterReader.ParseTitle(creature.ItemName).Level),
                creature.CloneInfo?.ItemName is { } cloneName ? new CardItem(cloneName, Icon(creature.CloneInfo.ItemId)) : null,
                (creature.Artifact ?? []).Where(a => a.ItemName != null).Select(a => new CardItem(a.ItemName!, Icon(a.ItemId), a.ItemRarity)).ToArray()),
            (c.Avatar?.Avatar ?? []).Where(s => !string.IsNullOrEmpty(s.ItemName)).Select(s => new CardAvatar(
                s.SlotId,
                new CardItem(s.ItemName!, Icon(s.ItemId), Level: s.SlotId == "WEAPON" ? CharacterReader.RareCloneLevel(s.ItemName!) : null),
                (s.Emblems ?? []).Where(e => !string.IsNullOrEmpty(e.ItemName)).Select(e => new CardEmblem(e.SlotColor ?? "", e.ItemName!, Icon(e.ItemId))).ToArray())).ToArray(),
            new CardSkills(
                (style.Active ?? []).Concat(style.Passive ?? []).Where(s => s.Level > 0).ToDictionary(s => s.SkillId, s => s.Level),
                (style.Evolution ?? []).ToDictionary(e => e.SkillId, e => e.Type),
                (style.Enhancement ?? []).ToDictionary(e => e.SkillId, e => e.Type)));
    }

    // The highest-fame characters across the given lists (for the all-classes leaderboard).
    internal static List<CharacterCard> TopOverall(IEnumerable<CharacterCard> cards) =>
        cards.OrderByDescending(c => c.Fame).ThenBy(c => c.Id, StringComparer.Ordinal).Take(Size).ToList();

    // Writes stats/data/characters/<key>.js for every class, and all.js, dps.js and support.js for the overall tops.
    // roleByClass: class key -> "DPS" or "Support".
    public static async Task ExportAsync(CommandContext ctx, string siteDir, IReadOnlyDictionary<string, string> roleByClass, IReadOnlyDictionary<string, Dictionary<string, string>> icons)
    {
        var loaded = new Dictionary<string, List<LoadedCharacter>>();
        foreach (var key in roleByClass.Keys)
            if (File.Exists(ctx.RosterPath(key)) && Directory.Exists(ctx.CharsDir(key))) loaded[key] = await LoadTopAsync(ctx, key);

        var ids = loaded.Values.SelectMany(l => l).SelectMany(IconIds).ToHashSet();
        var have = await SiteIcons.EnsureItemIconsAsync(Path.Combine(siteDir, "img", "items"), ids);

        var dir = Path.Combine(siteDir, "data", "characters");
        Directory.CreateDirectory(dir);
        var all = new List<CharacterCard>();
        foreach (var (key, list) in loaded)
        {
            var cards = list.Select(c => Build(c, have, icons)).ToList();
            all.AddRange(cards);
            await WriteAsync(dir, key, cards);
        }
        await WriteAsync(dir, "all", TopOverall(all));
        await WriteAsync(dir, "dps", TopOverall(all.Where(c => roleByClass[c.Class] == "DPS")));
        await WriteAsync(dir, "support", TopOverall(all.Where(c => roleByClass[c.Class] == "Support")));
        Console.WriteLine($"Character pages: top {Size} of {loaded.Count} classes written to {dir}");
    }

    static Task WriteAsync(string dir, string key, IReadOnlyList<CharacterCard> cards) =>
        File.WriteAllTextAsync(Path.Combine(dir, $"{key}.js"),
            $"window.DFO_CHARS=window.DFO_CHARS||{{}};window.DFO_CHARS[{JsonSerializer.Serialize(key)}]={JsonSerializer.Serialize(cards, WebJson)};");
}
