using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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

// One leaderboard row: the character's identity plus the icons the row draws. Cells are positional (see CharacterCards.Cells);
// a cell is [name, icon, rarity, reinforce, amp, tune, level] with trailing nulls cut off, or null for an empty slot.
record CardRow(string Id, string Class, string Name, string Server, int? Level, int Fame, object?[]?[] Cells);

// A character as read from the saved raw files, before the icons are known.
sealed record LoadedCharacter(RosterEntry Entry, string Class, EquipmentResponse Equipment, OathResponse? Oath, AvatarResponse? Avatar, CreatureResponse? Creature, SkillResponse Skills);

// Hand-made oath item icons (reference/oathicons): the image server has none for oath items.
// File "<item name>.png", or "<item name> [<Rarity>].png" for an item that exists in several rarities with different colours (Eternal Mist Oath).
static class OathIcons
{
    // Copies the icons into the site's icon folder as oath-<slug>.png and returns the file names (without .png) that exist.
    public static HashSet<string> Install(string referenceDir, string iconDir)
    {
        var dir = Path.Combine(referenceDir, "oathicons");
        var names = new HashSet<string>();
        if (!Directory.Exists(dir)) return names;
        Directory.CreateDirectory(iconDir);
        foreach (var file in Directory.GetFiles(dir, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            File.Copy(file, Path.Combine(iconDir, FileName(name)), overwrite: true);
            names.Add(name);
        }
        return names;
    }

    static string FileName(string name) => $"oath-{SetEmblems.Slug(name)}.png";

    // The rarity-specific icon wins over the plain one; null when there is neither.
    internal static string? Lookup(IReadOnlySet<string> names, string itemName, string? rarity)
    {
        if (rarity != null && names.Contains($"{itemName} [{rarity}]")) return FileName($"{itemName} [{rarity}]");
        return names.Contains(itemName) ? FileName(itemName) : null;
    }
}

// Makes a JSON tree smaller: every string is stored once in a table and replaced by -(index + 1). The data holds no negative numbers, so the
// page (unpack in characters.js) can tell a reference from a value by its sign.
static class JsonPack
{
    public static (JsonNode? Tree, List<string> Table) Pack(JsonNode? tree)
    {
        var table = new List<string>();
        var index = new Dictionary<string, int>();
        int Id(string text)
        {
            if (!index.TryGetValue(text, out var id)) { index[text] = id = table.Count; table.Add(text); }
            return -(id + 1);
        }

        JsonNode? Walk(JsonNode? node) => node switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Id(s)),
            JsonObject o => new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key, Walk(kv.Value)))),
            JsonArray a => new JsonArray(a.Select(Walk).ToArray()),
            _ => node.DeepClone(),
        };
        return (Walk(tree), table);
    }
}

// Every character above the fame floor, as leaderboard pages (light rows) and character pages (full cards in chunks).
// Files under stats/data/characters/:
//   <list>/<n>.js     rows of page n (0-based) of a list: all, dps, support or a class key
//   <class>/d<n>.js   full cards of the class's characters n*PageSize ... (same ranking as the class list)
//   <class>/ids.js    the class's character ids in rank order (finds a character's chunk)
static class CharacterCards
{
    internal const int PageSize = 20;
    const int GearCells = 12;      // Weapon and the 11 other gear slots; the oath item and its 11 crystals use the same 12 columns
    const int CellCount = 28;

    static readonly JsonSerializerOptions WebJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static readonly JsonSerializerOptions TextJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Highest fame first; the id breaks ties so the same data always gives the same order.
    internal static List<RosterEntry> Ranked(IEnumerable<RosterEntry> roster) =>
        roster.OrderByDescending(e => e.Fame).ThenBy(e => e.CharacterId, StringComparer.Ordinal).ToList();

    // Every character of the class that has gear and skills on disk and still belongs to it, highest fame first.
    static async Task<List<LoadedCharacter>> LoadAllAsync(CommandContext ctx, string classKey)
    {
        var dir = ctx.CharsDir(classKey);
        var all = new List<LoadedCharacter>();
        foreach (var entry in Ranked(await ctx.LoadRosterAsync(classKey)))
        {
            async Task<T?> Load<T>(string suffix)
            {
                var path = Path.Combine(dir, $"{entry.CharacterId}_{suffix}.json");
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Json.Options) : default;
            }

            var equipment = await Load<EquipmentResponse>("equipment");
            var skills = await Load<SkillResponse>("skill_style");
            if (equipment?.Equipment == null || skills?.Skill?.Style == null || !CharacterReader.BelongsTo(equipment, classKey)) continue;
            all.Add(new LoadedCharacter(entry, classKey, equipment, await Load<OathResponse>("oath"), await Load<AvatarResponse>("avatar"), await Load<CreatureResponse>("creature"), skills));
        }
        return all;
    }

    // The item ids the leaderboard row draws an icon for.
    internal static IEnumerable<string> RowIconIds(LoadedCharacter c)
    {
        var ids = c.Equipment.Equipment!.Select(i => i.ItemId)
            .Concat([c.Oath?.Oath?.Info?.ItemId])
            .Concat((c.Oath?.Oath?.Crystal ?? []).Select(x => x.ItemId))
            .Concat([c.Creature?.Creature?.ItemId])
            .Concat((c.Avatar?.Avatar ?? []).Where(s => s.SlotId is "WEAPON" or "AURORA").Select(s => s.ItemId));
        return ids.Where(id => !string.IsNullOrEmpty(id))!;
    }

    // Every item id a character page draws an icon for.
    internal static IEnumerable<string> IconIds(LoadedCharacter c)
    {
        var ids = RowIconIds(c)
            .Concat([c.Creature?.Creature?.CloneInfo?.ItemId])
            .Concat((c.Creature?.Creature?.Artifact ?? []).Select(a => a.ItemId))
            .Concat((c.Avatar?.Avatar ?? []).SelectMany(s => (s.Emblems ?? []).Select(e => e.ItemId).Prepend(s.ItemId)));
        return ids.Where(id => !string.IsNullOrEmpty(id))!;
    }

    // have = item ids with an icon file; icons = the named icons of the statistics (a title or creature the image server lacks still has one);
    // oathIcons = the hand-made oath item icons (see OathIcons).
    internal static CharacterCard Build(LoadedCharacter c, IReadOnlySet<string> have, IReadOnlyDictionary<string, Dictionary<string, string>> icons, IReadOnlySet<string> oathIcons)
    {
        string? Icon(string? id) => id != null && have.Contains(id) ? $"{id}.png" : null;
        string? Named(string category, string name) => icons.GetValueOrDefault(category)?.GetValueOrDefault(name);

        // A Platinum level the statistics never listed ("Phantom City Platinum [45]") borrows the icon of another level of the same title or creature.
        string? NamedLevel(string category, string name)
        {
            if (Named(category, name) is { } exact) return exact;
            var cut = name.LastIndexOf(" [", StringComparison.Ordinal);
            if (cut < 0) return null;
            var family = name[..(cut + 2)];
            return icons.GetValueOrDefault(category)?.FirstOrDefault(kv => kv.Key.StartsWith(family, StringComparison.Ordinal)).Value;
        }

        CardItem Gear(EquippedItem i)
        {
            var icon = Icon(i.ItemId);
            if (icon == null && i.SlotId == "TITLE") icon = NamedLevel("titles", CharacterReader.ShownName(i.ItemName));
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
                // The image server has no icon for the oath item itself: a hand-made one, else the set's crystal icon the statistics use.
                oath.Info?.ItemName is { } oathName ? new CardItem(oathName,
                    Icon(oath.Info.ItemId) ?? OathIcons.Lookup(oathIcons, oathName, oath.Info.ItemRarity) ?? Named("oath", oath.SetInfo?.SetName ?? ""), oath.Info.ItemRarity) : null,
                (oath.Crystal ?? []).Where(x => x.ItemName != null).Select(x => new CardItem(x.ItemName!, Icon(x.ItemId), x.ItemRarity, Tune: x.Tune?.Level)).ToArray()),
            creature?.ItemName == null ? null : new CardCreature(
                new CardItem(creature.ItemName, Icon(creature.ItemId) ?? NamedLevel("creatures", CharacterReader.ShownName(creature.ItemName)), creature.ItemRarity,
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

    static readonly string[] GridGear = ["WEAPON", "JACKET", "SHOULDER", "PANTS", "SHOES", "WAIST", "AMULET", "WRIST", "RING", "SUPPORT", "MAGIC_STON", "EARRING"];

    // The leaderboard grid, top row first: 0-11 Weapon and the other gear slots, 12 Title, 13 Pet;
    // 14 oath item, 15-25 its crystals, 26 weapon avatar, 27 Aura. The page lays them out in 14 columns (characters.js, itemGrid).
    internal static object?[]?[] Cells(CharacterCard c)
    {
        var cells = new object?[]?[CellCount];
        var gear = new Dictionary<string, CardItem>();
        foreach (var item in c.Gear) gear.TryAdd(item.Slot ?? "", item);
        for (var i = 0; i < GearCells; i++) cells[i] = Cell(gear.GetValueOrDefault(GridGear[i]));
        cells[12] = Cell(gear.GetValueOrDefault("TITLE"));
        cells[13] = Cell(c.Creature?.Item);
        cells[14] = Cell(c.Oath?.Item);
        var crystals = c.Oath?.Crystals ?? [];
        for (var i = 0; i < Math.Min(crystals.Length, GearCells - 1); i++) cells[15 + i] = Cell(crystals[i]);
        var avatar = new Dictionary<string, CardItem>();
        foreach (var a in c.Avatar) avatar.TryAdd(a.Slot, a.Item);
        cells[26] = Cell(avatar.GetValueOrDefault("WEAPON"));
        cells[27] = Cell(avatar.GetValueOrDefault("AURORA"));
        return cells;
    }

    static object?[]? Cell(CardItem? item)
    {
        if (item == null) return null;
        object?[] cell = [item.Name, item.Icon, item.Rarity, item.Reinforce, item.Amp, item.Tune, item.Level];
        var length = cell.Length;
        while (length > 1 && cell[length - 1] == null) length--;
        return cell[..length];
    }

    internal static CardRow Row(CharacterCard c) => new(c.Id, c.Class, c.Name, c.Server, c.Level, c.Fame, Cells(c));

    // Writes <fileKey>.js: window.DFO_CHARS[fileKey] = { size, total?, s: strings, d: packed tree }.
    static async Task WriteFileAsync(string dir, string fileKey, object tree, int? total = null)
    {
        var (packed, table) = JsonPack.Pack(JsonSerializer.SerializeToNode(tree, WebJson));
        var file = new JsonObject { ["size"] = PageSize, ["s"] = new JsonArray(table.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()), ["d"] = packed };
        if (total != null) file["total"] = total;
        await WriteScriptAsync(dir, fileKey, file.ToJsonString(TextJson));
    }

    static async Task WriteScriptAsync(string dir, string fileKey, string json)
    {
        var path = Path.Combine(dir, fileKey + ".js");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, $"window.DFO_CHARS=window.DFO_CHARS||{{}};window.DFO_CHARS[{JsonSerializer.Serialize(fileKey)}]={json};");
    }

    static async Task WritePagesAsync(string dir, string list, IReadOnlyList<CardRow> rows)
    {
        for (var page = 0; page * PageSize < rows.Count; page++)
            await WriteFileAsync(dir, $"{list}/{page}", rows.Skip(page * PageSize).Take(PageSize).ToList(), rows.Count);
    }

    // roleByClass: class key -> "DPS" or "Support". iconBudget limits the icon downloading of one run; icons not reached show as blank
    // tiles until a later run (the icon folder is kept between runs).
    public static async Task ExportAsync(CommandContext ctx, string siteDir, IReadOnlyDictionary<string, string> roleByClass, IReadOnlyDictionary<string, Dictionary<string, string>> icons, TimeSpan iconBudget)
    {
        var dir = Path.Combine(siteDir, "data", "characters");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var keys = roleByClass.Keys.Where(k => File.Exists(ctx.RosterPath(k)) && Directory.Exists(ctx.CharsDir(k))).ToList();

        // Pass 1: which icons are needed. The ones the leaderboard rows draw come first, so a cut-off run still has complete rows.
        var rowIds = new HashSet<string>();
        var detailIds = new HashSet<string>();
        foreach (var key in keys)
            foreach (var c in await LoadAllAsync(ctx, key))
            {
                rowIds.UnionWith(RowIconIds(c));
                detailIds.UnionWith(IconIds(c));
            }
        detailIds.ExceptWith(rowIds);
        Console.WriteLine($"Character icons needed: {rowIds.Count} for the rows, {detailIds.Count} more for the character pages.");
        var have = await SiteIcons.EnsureItemIconsAsync(Path.Combine(siteDir, "img", "items"), rowIds.Concat(detailIds), DateTime.UtcNow + iconBudget);
        var oathIcons = OathIcons.Install(ctx.ReferenceDir, Path.Combine(siteDir, "img", "items"));

        // Pass 2: the class lists and character chunks; the rows are kept for the lists across classes.
        var allRows = new List<(CardRow Row, string Role)>();
        var characters = 0;
        foreach (var key in keys)
        {
            var cards = (await LoadAllAsync(ctx, key)).Select(c => Build(c, have, icons, oathIcons)).ToList();
            if (cards.Count == 0) continue;
            characters += cards.Count;

            var rows = cards.Select(Row).ToList();
            await WritePagesAsync(dir, key, rows);
            for (var chunk = 0; chunk * PageSize < cards.Count; chunk++)
                await WriteFileAsync(dir, $"{key}/d{chunk}", cards.Skip(chunk * PageSize).Take(PageSize).ToList());
            await WriteScriptAsync(dir, $"{key}/ids", JsonSerializer.Serialize(new { size = PageSize, ids = cards.Select(c => c.Id) }, TextJson));
            allRows.AddRange(rows.Select(r => (r, roleByClass[key])));
        }

        // The lists across classes: the same rows, ranked by fame.
        IReadOnlyList<CardRow> ByFame(Func<string, bool> role) =>
            allRows.Where(r => role(r.Role)).Select(r => r.Row).OrderByDescending(r => r.Fame).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        await WritePagesAsync(dir, "all", ByFame(_ => true));
        await WritePagesAsync(dir, "dps", ByFame(role => role == "DPS"));
        await WritePagesAsync(dir, "support", ByFame(role => role == "Support"));
        Console.WriteLine($"Character pages: {characters} characters of {keys.Count} classes written to {dir}");
    }
}
