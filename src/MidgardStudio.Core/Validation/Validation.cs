using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Core.Validation;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>How much of an overlay to validate.</summary>
public enum ValidationScope
{
    /// <summary>Only authored entries (overrides and new customs). The server's base data is not the user's concern.</summary>
    CustomOnly,

    /// <summary>Every effective record, including the read-only base bundle.</summary>
    FullScan,
}

/// <summary>
/// A single validation finding. The first five members are positional for backwards compatibility
/// (existing call sites and XAML bindings). <see cref="RuleId"/>, <see cref="Mode"/> and
/// <see cref="Fix"/> are optional enrichments.
/// </summary>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string DbId,
    string Key,
    string? Field,
    string Message)
{
    /// <summary>Stable identifier for the rule that produced this issue, e.g. "MOB.ID_RANGE".
    /// Used for grouping, suppression, and assertion-stable tests.</summary>
    public string? RuleId { get; init; }

    /// <summary>The server mode this finding applies to (null = both / mode-agnostic).</summary>
    public ServerMode? Mode { get; init; }

    /// <summary>An optional one-click remedy.</summary>
    public QuickFix? Fix { get; init; }

    /// <summary>Overrides the display category when the finding's <see cref="DbId"/> (which drives navigation)
    /// doesn't match how it should be grouped — e.g. a client mob-sprite check has DbId "mob_db" (so "Go to"
    /// opens the Monsters list) but belongs under the "Client Mobs" category. Null = group by DbId.</summary>
    public string? Category { get; init; }

    /// <summary>Localization key for <see cref="Message"/> (e.g. "Val_Msg_FieldRequired"). When set, the App
    /// layer resolves a localized template via this key and formats it with <see cref="MessageArgs"/>; the
    /// English <see cref="Message"/> is the fallback. Null when the message isn't yet localized.</summary>
    public string? MessageKey { get; init; }

    /// <summary>Arguments for the localized <see cref="MessageKey"/> template. Label args should already be
    /// resolved through <see cref="ValidationContext.L"/> by the validator.</summary>
    public object[]? MessageArgs { get; init; }
}

/// <summary>Validates a single record (the common case — enum, bounds, references, …). Cheap enough
/// to run live as the user edits one record.</summary>
public interface IRecordValidator
{
    bool AppliesTo(string dbId);

    IEnumerable<ValidationIssue> Validate(DbRecord record, OverlayTable table, ValidationContext context);
}

/// <summary>Validates a whole overlay at once (cross-record rules such as duplicate AegisName).</summary>
public interface IOverlayValidator
{
    bool AppliesTo(string dbId);

    IEnumerable<ValidationIssue> Validate(OverlayTable table, ValidationScope scope, ValidationContext context);
}
