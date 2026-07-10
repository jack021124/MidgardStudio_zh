using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MidgardStudio.App.Services;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Grf;

namespace MidgardStudio.App.ViewModels;

/// <summary>One editable keyboard shortcut row in the Settings ▸ Shortcuts tab.</summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly Action<string, string> _set;

    public ShortcutRowViewModel(string key, string display, string gesture, Action<string, string> set)
    {
        Key = key;
        _display = display;
        _gesture = gesture;
        _set = set;
    }

    public string Key { get; }

    [ObservableProperty] private string _display;

    /// <summary>Re-resolves the action label after a language switch (gesture is unchanged).</summary>
    public void RefreshDisplay(string display) => Display = display;

    [ObservableProperty] private string _gesture;

    partial void OnGestureChanged(string value) => _set(Key, value);
}

/// <summary>
/// Settings panel (File ▸ Settings), rendered with a left-side nav menu (General / Shortcuts / About).
/// General hosts the saving-behaviour + backup-retention preferences; Shortcuts edits the configurable
/// keyboard gestures (driven by <see cref="AppSettingsService.ShortcutDefs"/>). Changes persist
/// immediately and notify the shell to re-arm its auto-save timers and rebuild its key bindings.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _settings;
    private readonly Action _onChanged;

    public SettingsViewModel(AppSettingsService settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;
        _mode = settings.Settings.SaveMode;
        _intervalSeconds = settings.Settings.SaveIntervalSeconds;
        _backupRetention = settings.Settings.BackupRetention;
        _saveGate = settings.Settings.SaveGate;
        _validateBeforeSave = settings.Settings.ValidateOnSave;

        BuildShortcuts();

        // Seed the language dropdown from the current (already-initialized) UI language.
        Languages.Add(new("zh-CN", Localization.LocalizationService.Get("Settings_Language_Chinese")));
        Languages.Add(new("en", Localization.LocalizationService.Get("Settings_Language_English")));

        BuildAutocomplete();

        // Rebuild the localized rows when the interface language changes at runtime so toggle/label/
        // shortcut names follow the new language without a restart.
        Localization.LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        // Re-resolve the shortcut action labels in place (gestures are unchanged).
        for (int i = 0; i < Shortcuts.Count && i < AppSettingsService.ShortcutDefs.Length; i++)
            Shortcuts[i].RefreshDisplay(L("Shortcut_" + AppSettingsService.ShortcutDefs[i].Key));

        // Rebuild the autocomplete rows so toggle/colour/field-label names read in the new language.
        AutocompleteToggles.Clear();
        SemanticColors.Clear();
        ElementColors.Clear();
        FieldLabels.Clear();
        BuildAutocomplete();
    }

    private void BuildShortcuts()
    {
        foreach (var (key, _, def) in AppSettingsService.ShortcutDefs)
        {
            string gesture = _settings.Settings.Shortcuts.TryGetValue(key, out var g) && !string.IsNullOrWhiteSpace(g) ? g : def;
            Shortcuts.Add(new ShortcutRowViewModel(key, L("Shortcut_" + key), gesture, SetShortcut));
        }
    }

    private static string L(string key) => Localization.LocalizationService.Get(key);

    // ===== Autocomplete settings =====

    private AutocompleteConfig Ac => _settings.Settings.Autocomplete;

    /// <summary>Which lines the Autocomplete generator includes.</summary>
    public ObservableCollection<AcToggleViewModel> AutocompleteToggles { get; } = new();

    /// <summary>Semantic colors (values, labels, attack, defense, skill).</summary>
    public ObservableCollection<AcColorViewModel> SemanticColors { get; } = new();

    /// <summary>Per-element colors used on the "Element:" line.</summary>
    public ObservableCollection<AcColorViewModel> ElementColors { get; } = new();

    /// <summary>Per-field label overrides.</summary>
    public ObservableCollection<AcLabelViewModel> FieldLabels { get; } = new();

    // Manual properties (not [ObservableProperty]) so the ctor can seed the backing field without persisting.
    private bool _overwriteExisting;
    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set { if (SetProperty(ref _overwriteExisting, value)) { Ac.OverwriteExisting = value; _settings.Save(); } }
    }

    private string _defaultUnidentifiedDescription = string.Empty;
    public string DefaultUnidentifiedDescription
    {
        get => _defaultUnidentifiedDescription;
        set { if (SetProperty(ref _defaultUnidentifiedDescription, value ?? string.Empty)) { Ac.DefaultUnidentifiedDescription = _defaultUnidentifiedDescription; _settings.Save(); } }
    }

    private string _missingValueText = string.Empty;
    public string MissingValueText
    {
        get => _missingValueText;
        set { if (SetProperty(ref _missingValueText, value ?? string.Empty)) { Ac.MissingValueText = _missingValueText; _settings.Save(); RefreshPreview(); } }
    }

    private void BuildAutocomplete()
    {
        _overwriteExisting = Ac.OverwriteExisting;
        _defaultUnidentifiedDescription = Ac.DefaultUnidentifiedDescription;
        _missingValueText = Ac.MissingValueText;

        foreach (var (key, _) in AutocompleteConfig.ToggleKeys)
            AutocompleteToggles.Add(new AcToggleViewModel(L("AcToggle_" + key), Ac.GetToggle(key),
                v => { Ac.SetToggle(key, v); _settings.Save(); RefreshPreview(); }));

        void AddSemantic(string resKey, Func<string> get, Action<string> set) =>
            SemanticColors.Add(new AcColorViewModel(L(resKey), get(), hex => { set(NormalizeHex(hex)); _settings.Save(); RefreshPreview(); }));
        AddSemantic("AcColor_Values", () => Ac.ValueColor, v => Ac.ValueColor = v);
        AddSemantic("AcColor_Labels", () => Ac.LabelColor, v => Ac.LabelColor = v);
        AddSemantic("AcColor_Attack", () => Ac.AttackColor, v => Ac.AttackColor = v);
        AddSemantic("AcColor_Defense", () => Ac.DefenseColor, v => Ac.DefenseColor = v);
        AddSemantic("AcColor_Skill", () => Ac.SkillColor, v => Ac.SkillColor = v);

        foreach (var name in AutocompleteConfig.DefaultElementColors().Keys)
            ElementColors.Add(new AcColorViewModel(name, Ac.ElementColor(name),
                hex => { Ac.ElementColors[name] = NormalizeHex(hex); _settings.Save(); RefreshPreview(); }));

        foreach (var (key, _) in AutocompleteConfig.LabelKeys)
        {
            string current = Ac.Labels.TryGetValue(key, out var ov) ? ov : string.Empty;
            FieldLabels.Add(new AcLabelViewModel(L("AcLabel_" + key), current, v =>
            {
                if (string.IsNullOrWhiteSpace(v)) Ac.Labels.Remove(key);
                else Ac.Labels[key] = v.Trim();
                _settings.Save();
                RefreshPreview();
            }));
        }

        RefreshPreview();
    }

    private static string NormalizeHex(string? hex) =>
        (hex ?? string.Empty).TrimStart('#', '^').Trim().ToUpperInvariant();

    // ----- Live preview -----

    /// <summary>The sample item's generated text (^RRGGBB-coded) reflecting the current settings.</summary>
    public string PreviewText { get; private set; } = string.Empty;

    private bool _skillResolved;
    private Func<string, string?>? _skill;

    private Func<string, string?>? Resolver()
    {
        if (_skillResolved) return _skill;
        _skillResolved = true;
        try { if (App.Services.GetService<SkillLookupService>() is { } s) _skill = s.Display; } catch { /* host not ready */ }
        return _skill;
    }

    private void RefreshPreview()
    {
        var gen = new ItemAutocomplete(Ac, Resolver());
        var lines = gen.IdentifiedDescription(SampleItem);
        PreviewText = gen.DisplayName(SampleItem) + Environment.NewLine + string.Join(Environment.NewLine, lines);
        OnPropertyChanged(nameof(PreviewText));
    }

    private static readonly DbRecord SampleItem = BuildSample();

    private static DbRecord BuildSample()
    {
        var r = new DbRecord(ItemDbSchema.Instance);
        r.SetRaw("Id", 40000);
        r.SetRaw("Name", "Sample Fire Sword");
        r.SetRaw("Type", "Weapon");
        r.SetRaw("SubType", "1hSword");
        r.SetRaw("Attack", 120);
        r.SetRaw("WeaponLevel", 4);
        r.SetRaw("Weight", 800);
        r.SetRaw("EquipLevelMin", 50);
        r.SetRaw("Refineable", true);
        r.SetRaw("Locations", new HashSet<string>(StringComparer.Ordinal) { "Right_Hand" });
        r.SetRaw("Jobs", new HashSet<string>(StringComparer.Ordinal) { "Swordman", "Knight" });
        r.SetRaw("Script", new ScriptValue("bonus bStr,3;\nbonus bAtkEle,Ele_Fire;\nbonus3 bAutoSpell,\"MG_FIREBOLT\",3,50;"));
        return r;
    }

    /// <summary>Editable keyboard shortcuts (Settings ▸ Shortcuts).</summary>
    public ObservableCollection<ShortcutRowViewModel> Shortcuts { get; } = new();

    private void SetShortcut(string key, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) _settings.Settings.Shortcuts.Remove(key);
        else _settings.Settings.Shortcuts[key] = gesture.Trim();
        _settings.Save();
        _onChanged();
    }

    [ObservableProperty] private SaveMode _mode;
    [ObservableProperty] private int _intervalSeconds;
    [ObservableProperty] private int _backupRetention;

    partial void OnBackupRetentionChanged(int value)
    {
        _settings.Settings.BackupRetention = Math.Max(1, value);
        _settings.Save();
    }

    public bool IsManual { get => Mode == SaveMode.Manual; set { if (value) Mode = SaveMode.Manual; } }
    public bool IsInterval { get => Mode == SaveMode.Interval; set { if (value) Mode = SaveMode.Interval; } }
    public bool IsOnEdit { get => Mode == SaveMode.OnEdit; set { if (value) Mode = SaveMode.OnEdit; } }

    partial void OnModeChanged(SaveMode value)
    {
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IsInterval));
        OnPropertyChanged(nameof(IsOnEdit));
        Persist();
    }

    partial void OnIntervalSecondsChanged(int value) => Persist();

    private void Persist()
    {
        _settings.Settings.SaveMode = Mode;
        _settings.Settings.SaveIntervalSeconds = Math.Max(5, IntervalSeconds);
        _settings.Save();
        _onChanged();
    }

    // ===== Save validation gate =====

    [ObservableProperty] private ValidationGateMode _saveGate;
    [ObservableProperty] private bool _validateBeforeSave;

    // ===== Interface language =====

    /// <summary>The interface languages offered in Settings, in display order. Each item carries the
    /// culture code (<see cref="AppSettings.Language"/>) and a localized display name. Rebuilt after a
    /// switch so the picker's own labels read in the newly-active language.</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = [];

    /// <summary>The currently selected language code. Setter persists to settings and swaps the live
    /// dictionary so the whole UI refreshes without a restart.</summary>
    public string SelectedLanguage
    {
        get => _settings.Settings.Language;
        set
        {
            if (value == _settings.Settings.Language) return;
            _settings.Settings.Language = value;
            _settings.Save();
            Localization.LocalizationService.SetLanguage(value);
            // Rebuild the dropdown labels in the now-active language, then re-announce the selection so
            // the ComboBox keeps the right item highlighted.
            RebuildLanguages();
            OnPropertyChanged(nameof(SelectedLanguage));
        }
    }

    private void RebuildLanguages()
    {
        var current = SelectedLanguage;
        Languages.Clear();
        Languages.Add(new("zh-CN", Localization.LocalizationService.Get("Settings_Language_Chinese")));
        Languages.Add(new("en", Localization.LocalizationService.Get("Settings_Language_English")));
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    // ===== Global text encoding (single source of truth — the GRF Browser reads from here too) =====

    private static readonly EncodingChoice CustomEncoding = new(-1, L("Settings_Encoding_Custom"));

    /// <summary>The codepages offered in the selector: the fixed choices plus a "Custom codepage…" entry
    /// (CodePage == -1) that reveals a free-text box when selected.</summary>
    public ObservableCollection<EncodingChoice> EncodingChoices { get; } =
        new(ViewEncoding.Choices.Append(CustomEncoding));

    /// <summary>True when the "Custom codepage…" entry is selected, showing the free-text box.</summary>
    public bool CustomEncodingVisible => SelectedGlobalEncoding?.CodePage < 0;

    /// <summary>The text typed into the custom-codepage box (validated against <see cref="ViewEncoding.IsKnown"/>).</summary>
    [ObservableProperty] private string _customEncodingText = string.Empty;

    /// <summary>Applies the codepage typed into the custom box (bound to Enter / a Go button).</summary>
    [RelayCommand]
    private void ApplyCustomEncoding()
    {
        if (int.TryParse(CustomEncodingText.Trim(), out int cp) && ViewEncoding.IsKnown(cp))
        {
            _settings.Settings.GlobalCodepage = cp;
            _settings.Save();
            OnPropertyChanged(nameof(SelectedGlobalEncoding));
            OnPropertyChanged(nameof(CustomEncodingVisible));
            _onChanged();
        }
        else
        {
            CustomEncodingText = string.Empty;
        }
    }

    /// <summary>The global fallback codepage. Persists to settings on change.</summary>
    public EncodingChoice? SelectedGlobalEncoding
    {
        get
        {
            int cp = _settings.Settings.GlobalCodepage;
            return EncodingChoices.FirstOrDefault(e => e.CodePage == cp) ?? EncodingChoices[0];
        }
        set
        {
            if (value is null) return;
            // Picking "Custom codepage…" just reveals the box; the actual codepage is applied by ApplyCustomEncoding.
            if (value.CodePage < 0) { OnPropertyChanged(nameof(CustomEncodingVisible)); return; }
            if (value.CodePage == _settings.Settings.GlobalCodepage) return;
            _settings.Settings.GlobalCodepage = value.CodePage;
            _settings.Save();
            OnPropertyChanged(nameof(SelectedGlobalEncoding));
            OnPropertyChanged(nameof(CustomEncodingVisible));
            _onChanged();
        }
    }

    public bool IsGateAdvisory { get => SaveGate == ValidationGateMode.Advisory; set { if (value) SaveGate = ValidationGateMode.Advisory; } }
    public bool IsGateSoft { get => SaveGate == ValidationGateMode.SoftGate; set { if (value) SaveGate = ValidationGateMode.SoftGate; } }
    public bool IsGateHard { get => SaveGate == ValidationGateMode.HardGate; set { if (value) SaveGate = ValidationGateMode.HardGate; } }

    partial void OnSaveGateChanged(ValidationGateMode value)
    {
        OnPropertyChanged(nameof(IsGateAdvisory));
        OnPropertyChanged(nameof(IsGateSoft));
        OnPropertyChanged(nameof(IsGateHard));
        _settings.Settings.SaveGate = value;
        _settings.Save();
    }

    partial void OnValidateBeforeSaveChanged(bool value)
    {
        _settings.Settings.ValidateOnSave = value;
        _settings.Save();
    }
}

/// <summary>One include/exclude toggle in the Autocomplete settings.</summary>
public sealed partial class AcToggleViewModel : ObservableObject
{
    private readonly Action<bool> _set;

    public AcToggleViewModel(string display, bool isOn, Action<bool> set)
    {
        Display = display;
        _isOn = isOn;
        _set = set;
    }

    public string Display { get; }

    [ObservableProperty] private bool _isOn;

    partial void OnIsOnChanged(bool value) => _set(value);
}

/// <summary>One editable color row (semantic or per-element) with a live swatch.</summary>
public sealed partial class AcColorViewModel : ObservableObject
{
    private readonly Action<string> _set;

    public AcColorViewModel(string name, string hex, Action<string> set)
    {
        Name = name;
        _hex = hex;
        _set = set;
    }

    public string Name { get; }

    [ObservableProperty] private string _hex;

    public Brush Swatch => BrushFromHex(Hex);

    partial void OnHexChanged(string value)
    {
        _set(value);
        OnPropertyChanged(nameof(Swatch));
    }

    private static Brush BrushFromHex(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + (hex ?? string.Empty).TrimStart('#', '^'))); }
        catch { return Brushes.Transparent; }
    }
}

/// <summary>One per-field label override (blank = use the default word).</summary>
public sealed partial class AcLabelViewModel : ObservableObject
{
    private readonly Action<string> _set;

    public AcLabelViewModel(string defaultLabel, string @override, Action<string> set)
    {
        DefaultLabel = defaultLabel;
        _override = @override;
        _set = set;
    }

    /// <summary>The built-in label word (shown as the placeholder).</summary>
    public string DefaultLabel { get; }

    [ObservableProperty] private string _override;

    partial void OnOverrideChanged(string value) => _set(value);
}

/// <summary>One entry in the Settings ▸ Language dropdown. <see cref="Code"/> is the persisted value
/// (matches <see cref="AppSettings.Language"/>); <see cref="Display"/> is the user-facing label, which is
/// itself localized so the picker reads naturally in whichever language is active.</summary>
public sealed class LanguageOption(string code, string display)
{
    public string Code { get; } = code;
    public string Display { get; set; } = display;
}
