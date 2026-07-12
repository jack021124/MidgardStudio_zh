using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Validation.Validators;

/// <summary>
/// Cross-record uniqueness for any field flagged <see cref="FieldSchema.Unique"/> (e.g. AegisName,
/// which is unique but is not the record key). Detects custom records whose value collides with any
/// other effective record (custom or base), case-insensitively, mirroring rAthena's duplicate handling.
/// </summary>
public sealed class UniqueFieldValidator : IOverlayValidator
{
    public bool AppliesTo(string dbId) => true;

    public IEnumerable<ValidationIssue> Validate(OverlayTable table, ValidationScope scope, ValidationContext context)
    {
        var uniqueFields = table.Schema.Fields
            .Where(f => f.Unique && f.Kind is FieldKind.String or FieldKind.Reference or FieldKind.Enum)
            .ToList();
        if (uniqueFields.Count == 0) yield break;

        var records = table.Effective().ToList();

        foreach (var field in uniqueFields)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in records)
            {
                var value = rec.GetString(field.Name);
                if (string.IsNullOrWhiteSpace(value)) continue;
                counts[value] = counts.GetValueOrDefault(value) + 1;
            }

            foreach (var rec in records)
            {
                if (scope == ValidationScope.CustomOnly && rec.Origin == RecordOrigin.Base) continue;
                var value = rec.GetString(field.Name);
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (counts.GetValueOrDefault(value) > 1)
                    yield return new ValidationIssue(ValidationSeverity.Error, table.Schema.Id, rec.Key.ToString(),
                        field.Name, $"Duplicate {field.Label} '{value}' — it must be unique (case-insensitive).")
                    { RuleId = $"DUP.{field.Name.ToUpperInvariant()}", MessageKey = "Val_Msg_Duplicate", MessageArgs = new object[] { context.L(field.Label), value } };
            }
        }
    }
}
