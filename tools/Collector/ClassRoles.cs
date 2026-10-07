namespace Collector;

// Support (buffer) classes; everything else is DPS.
static class ClassRoles
{
    static readonly HashSet<string> Supports =
    [
        "Priest_F_Neo_Crusader",
        "Priest_M_Neo_Crusader",
        "Mage_F_Neo_Enchantress",
        "Archer_Neo_Muse",
        "Gunner_F_Neo_Paramedic",
    ];

    public static bool IsSupport(string classKey) => Supports.Contains(classKey);
}
