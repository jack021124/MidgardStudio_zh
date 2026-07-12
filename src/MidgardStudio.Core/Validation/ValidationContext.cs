using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.Core.Validation;

/// <summary>
/// Ambient inputs a validator needs that aren't carried by the record itself: the active server
/// mode (renewal-aware caps) and a cross-database reference index. This is the seam that keeps Core
/// rules free of any WPF / GRF / client dependency — the App answers the abstract questions.
/// </summary>
public sealed class ValidationContext
{
    public required ServerMode Mode { get; init; }

    public required IReferenceIndex References { get; init; }

    /// <summary>Optional resolver that turns a raw English schema label (e.g. "Attack") into the active
    /// language's label (e.g. "攻击力"). When set, message arguments built in Core that carry a label are
    /// pre-localized, so the App-layer formatter only needs to look up the message template. Null in tests /
    /// headless Core usage — the raw English label is used instead.</summary>
    public Func<string, string>? LabelResolver { get; init; }

    /// <summary>Resolves <paramref name="label"/> through <see cref="LabelResolver"/> when set; otherwise
    /// returns the label unchanged. Validators build message args with this so label args come out localized.</summary>
    public string L(string label) => LabelResolver is null ? label : LabelResolver(label);

    public static ValidationContext Create(IReferenceIndex references, ServerMode mode = ServerMode.Renewal) =>
        new() { Mode = mode, References = references };
}
