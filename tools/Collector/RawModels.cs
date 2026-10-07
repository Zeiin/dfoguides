namespace Collector;

// Only the fields the statistics need; everything else is ignored.
// Property names match the API camelCase via Json.Options.

record EquipmentResponse(List<EquippedItem>? Equipment, List<SetItemInfo>? SetItemInfo);
record EquippedItem(string SlotId, string ItemName, string? ItemRarity, string? ItemId = null);
record SetItemInfo(string SetItemName);

record OathResponse(OathBlock? Oath);
record OathBlock(OathSetInfo? SetInfo, List<OathCrystal>? Crystal = null);
record OathSetInfo(string? SetName, string? SetOptionName);
record OathCrystal(string? ItemId);

record AvatarResponse(List<AvatarSlot>? Avatar);
record AvatarSlot(string SlotId, string? ItemName, List<EmblemEntry>? Emblems, string? ItemId = null);
record EmblemEntry(string? SlotColor, string? ItemName, string? ItemId = null);

record CreatureResponse(CreatureBlock? Creature);
record CreatureBlock(string? ItemName, string? ItemId = null);

record SkillResponse(SkillBlock? Skill);
record SkillBlock(SkillStyle? Style);
record SkillStyle(List<SkillEntry>? Active, List<SkillEntry>? Passive, List<TypedSkill>? Evolution, List<TypedSkill>? Enhancement);
record SkillEntry(string SkillId, string Name, int Level);
record TypedSkill(string SkillId, int Type);
