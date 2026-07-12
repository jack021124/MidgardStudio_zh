using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;

namespace MidgardStudio.Core.Validation.Validators;

/// <summary>
/// Bespoke mob_db rules: the strict dual-range ID check, drop-table size caps, element-level and
/// level sanity, and the MVP-without-Mvp-mode trap.
/// </summary>
public sealed class MobDbValidator : IRecordValidator
{
    private const int MaxMobDrop = 10;   // rathena MAX_MOB_DROP
    private const int MaxMvpDrop = 3;    // rathena MAX_MVP_DROP

    public bool AppliesTo(string dbId) => dbId == "mob_db";

    public IEnumerable<ValidationIssue> Validate(DbRecord record, OverlayTable table, ValidationContext context)
    {
        string key = record.Key.ToString();
        int id = record.GetInt("Id");

        // rAthena (mob.cpp) uses STRICT inequalities: (id>1000 && id<3999) || (id>20020 && id<31999).
        if (!((id > 1000 && id < 3999) || (id > 20020 && id < 31999)))
            yield return new ValidationIssue(ValidationSeverity.Error, "mob_db", key, "Id",
                $"Mob ID {id} is outside the valid range. Use 1001–3998 or 20021–31998 " +
                "(1000, 3999, 20020 and 31999 are themselves invalid).")
            { RuleId = "MOB.ID_RANGE", MessageKey = "Val_Msg_MobIdRange", MessageArgs = new object[] { id } };

        if (record.GetInt("Level") < 1)
            yield return new ValidationIssue(ValidationSeverity.Warning, "mob_db", key, "Level",
                "Mob Level should be at least 1.")
            { RuleId = "MOB.LEVEL_RANGE", MessageKey = "Val_Msg_MobLevelRange", MessageArgs = new object[] { context.L("Level") } };

        int elementLevel = record.GetInt("ElementLevel");
        if (record.Has("ElementLevel") && elementLevel is < 1 or > 4)
            yield return new ValidationIssue(ValidationSeverity.Warning, "mob_db", key, "ElementLevel",
                $"Element Level must be between 1 and 4 (was {elementLevel}).")
            { RuleId = "MOB.ELEMENTLVL_RANGE", MessageKey = "Val_Msg_MobElementLvl", MessageArgs = new object[] { elementLevel } };

        int dropCount = record.GetList("Drops")?.Count ?? 0;
        if (dropCount > MaxMobDrop)
            yield return new ValidationIssue(ValidationSeverity.Warning, "mob_db", key, "Drops",
                $"A mob can have at most {MaxMobDrop} normal drops; the extra {dropCount - MaxMobDrop} will be ignored.")
            { RuleId = "MOB.DROP_COUNT", MessageKey = "Val_Msg_MobDropCount", MessageArgs = new object[] { MaxMobDrop, dropCount - MaxMobDrop } };

        int mvpCount = record.GetList("MvpDrops")?.Count ?? 0;
        if (mvpCount > MaxMvpDrop)
            yield return new ValidationIssue(ValidationSeverity.Warning, "mob_db", key, "MvpDrops",
                $"A mob can have at most {MaxMvpDrop} MVP drops; the extra {mvpCount - MaxMvpDrop} will be ignored.")
            { RuleId = "MOB.MVPDROP_COUNT", MessageKey = "Val_Msg_MobMvpDropCount", MessageArgs = new object[] { MaxMvpDrop, mvpCount - MaxMvpDrop } };

        // MVP rewards configured but the Mvp mode flag is missing → rewards never trigger.
        bool hasMvpRewards = mvpCount > 0 || record.GetInt("MvpExp") > 0;
        bool hasMvpMode = record.GetSet("Modes")?.Contains("Mvp") ?? false;
        if (hasMvpRewards && !hasMvpMode)
            yield return new ValidationIssue(ValidationSeverity.Warning, "mob_db", key, "Modes",
                "Mob has MVP drops/EXP but no Mvp mode — the MVP rewards will not trigger.")
            { RuleId = "MOB.MVP_NO_MVPFLAG", MessageKey = "Val_Msg_MobMvpNoFlag" };
    }
}
