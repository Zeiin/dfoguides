using System.Text.Json;

namespace Collector;

// Only the fields the statistics and the character pages need; everything else is ignored.
// Property names match the API camelCase via Json.Options.

record EquipmentResponse(List<EquippedItem>? Equipment, List<SetItemInfo>? SetItemInfo, string? JobName = null, string? JobGrowName = null, int? Fame = null,
    string? CharacterName = null, string? ServerId = null, int? Level = null, string? AdventureName = null);
record EquippedItem(string SlotId, string ItemName, string? ItemRarity, string? ItemId = null, string? SetItemName = null,
    int Reinforce = 0, string? AmplificationName = null, int Refine = 0, ItemEnchant? Enchant = null);
record ItemEnchant(List<StatEntry>? Status);
record StatEntry(string Name, JsonElement Value);
record SetItemInfo(string SetItemName);

record OathResponse(OathBlock? Oath);
record OathBlock(OathSetInfo? SetInfo, List<OathCrystal>? Crystal = null, OathItem? Info = null);
record OathItem(string? ItemId, string? ItemName, string? ItemRarity);
record OathSetInfo(string? SetName, string? SetOptionName, string? SetRarityName = null);
record OathCrystal(string? ItemId, string? ItemName = null, string? ItemRarity = null, OathTune? Tune = null);
record OathTune(int Level);

record AvatarResponse(List<AvatarSlot>? Avatar);
record AvatarSlot(string SlotId, string? ItemName, List<EmblemEntry>? Emblems, string? ItemId = null);
record EmblemEntry(string? SlotColor, string? ItemName, string? ItemId = null);

record CreatureResponse(CreatureBlock? Creature);
record CreatureBlock(string? ItemName, string? ItemId = null, string? ItemRarity = null, [property: System.Text.Json.Serialization.JsonPropertyName("clone")] CreatureClone? CloneInfo = null, List<CreatureArtifact>? Artifact = null);
record CreatureClone(string? ItemId, string? ItemName);
record CreatureArtifact(string? ItemId, string? ItemName, string? ItemRarity);

record SkillResponse(SkillBlock? Skill);
record SkillBlock(SkillStyle? Style);
record SkillStyle(List<SkillEntry>? Active, List<SkillEntry>? Passive, List<TypedSkill>? Evolution, List<TypedSkill>? Enhancement);
record SkillEntry(string SkillId, string Name, int Level);
record TypedSkill(string SkillId, int Type);
