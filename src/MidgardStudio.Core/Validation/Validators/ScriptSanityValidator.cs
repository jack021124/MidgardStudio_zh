using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;

namespace MidgardStudio.Core.Validation.Validators;

/// <summary>
/// Conservative script sanity. rAthena does NOT syntax-check scripts at load time, so over-flagging
/// is worse than under-flagging: we only report clearly-unbalanced delimiters (a near-certain typo),
/// ignoring the contents of quoted strings and line comments.
/// </summary>
public sealed class ScriptSanityValidator : IRecordValidator
{
    public bool AppliesTo(string dbId) => true;

    public IEnumerable<ValidationIssue> Validate(DbRecord record, OverlayTable table, ValidationContext context)
    {
        foreach (var field in table.Schema.Fields)
        {
            if (field.Kind != FieldKind.Script) continue;
            var script = record.GetScript(field.Name);
            if (script is null || script.IsEmpty) continue;

            if (!IsBalanced(script.Text))
                yield return new ValidationIssue(ValidationSeverity.Warning, table.Schema.Id, record.Key.ToString(),
                    field.Name, $"{field.Label} has unbalanced braces/brackets/parentheses — it may error in-game.")
                { RuleId = "SCRIPT.UNBALANCED", MessageKey = "Val_Msg_ScriptUnbalanced", MessageArgs = new object[] { context.L(field.Label) } };
        }
    }

    private static bool IsBalanced(string text)
    {
        int curly = 0, paren = 0, square = 0;
        bool inString = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\') { i++; continue; } // skip escaped char
                if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '/' when next == '*': inBlockComment = true; i++; break;
                case '/' when next == '/': inLineComment = true; i++; break;
                case '{': curly++; break;
                case '}': curly--; break;
                case '(': paren++; break;
                case ')': paren--; break;
                case '[': square++; break;
                case ']': square--; break;
            }

            if (curly < 0 || paren < 0 || square < 0) return false;
        }

        // An unterminated string or block comment likely means the script was truncated — flag it.
        return curly == 0 && paren == 0 && square == 0 && !inString && !inBlockComment;
    }
}
