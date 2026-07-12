using System.Collections;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;

namespace MidgardStudio.Core.Validation.Validators;

/// <summary>
/// item_combos rules. Combo members are a ScalarList of item AegisNames (not a Reference field), so
/// resolution and the ≥2-member requirement are handled here rather than by the generic resolver.
/// rAthena rejects the entire combo if a member is missing or fewer than two are listed.
/// </summary>
public sealed class ItemComboValidator : IRecordValidator
{
    public bool AppliesTo(string dbId) => dbId == "item_combos";

    public IEnumerable<ValidationIssue> Validate(DbRecord record, OverlayTable table, ValidationContext context)
    {
        string key = record.Key.ToString();
        var combos = record.GetList("Combos");
        if (combos is null) yield break;

        bool canResolve = context.References.Knows("item_db");

        foreach (var combo in combos)
        {
            var members = ReadMembers(combo.Get("Combo"));

            if (members.Count < 2)
            {
                yield return new ValidationIssue(ValidationSeverity.Error, "item_combos", key, "Combos",
                    "A combo needs at least 2 items; rAthena rejects combos with fewer.")
                { RuleId = "XREF.COMBO_MIN_MEMBERS", MessageKey = "Val_Msg_ComboMinMembers" };
                continue;
            }

            if (!canResolve) continue;

            foreach (var member in members)
                if (!context.References.Contains("item_db", member))
                    yield return new ValidationIssue(ValidationSeverity.Error, "item_combos", key, "Combos",
                        $"Combo member '{member}' was not found in item_db; rAthena rejects the whole combo.")
                    { RuleId = "XREF.COMBO_MEMBER_MISSING", MessageKey = "Val_Msg_ComboMemberMissing", MessageArgs = new object[] { member } };
        }
    }

    private static List<string> ReadMembers(object? value)
    {
        var result = new List<string>();
        if (value is IEnumerable items and not string)
            foreach (var item in items)
            {
                string s = item?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
            }
        return result;
    }
}
