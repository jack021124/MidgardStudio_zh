using System;
using System.IO;
using System.Text.Json;

namespace MidgardStudio.App.Services;

/// <summary>How edits reach disk.</summary>
public enum SaveMode
{
    /// <summary>Files are written only when the user clicks Save (the default — nothing is automatic).</summary>
    Manual,
    /// <summary>Pending changes are written on a fixed timer.</summary>
    Interval,
    /// <summary>Pending changes are written shortly after each edit.</summary>
    OnEdit,
}

/// <summary>How strictly a manual save reacts to outstanding Error-level validation issues.</summary>
public enum ValidationGateMode
{
    /// <summary>Saving never inspects validation (legacy behavior).</summary>
    Advisory,
    /// <summary>A manual save with errors shows a list and lets the user save anyway or stop to fix.</summary>
    SoftGate,
    /// <summary>A manual save is blocked until Error-level issues are fixed (warnings/info still allowed).</summary>
    HardGate,
}

/// <summary>App-wide preferences (not tied to a server profile).</summary>
public sealed class AppSettings
{
    public SaveMode SaveMode { get; set; } = SaveMode.Manual;
    public int SaveIntervalSeconds { get; set; } = 60;

    /// <summary>How a manual save reacts to outstanding Error-level validation issues.</summary>
    public ValidationGateMode SaveGate { get; set; } = ValidationGateMode.SoftGate;

    /// <summary>When false, the save gate is never run regardless of <see cref="SaveGate"/>.</summary>
    public bool ValidateOnSave { get; set; } = true;

    /// <summary>User-overridden keyboard shortcuts, keyed by action (e.g. "Save" → "Ctrl+S").</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();

    /// <summary>How many dated backup snapshots to keep per profile before the oldest are pruned.</summary>
    public int BackupRetention { get; set; } = 30;

    /// <summary>True once the user has been shown the one-time onboarding tour. Drives whether the
    /// onboarding screens are presented on launch (they run exactly once).</summary>
    public bool HasSeenOnboarding { get; set; }

    /// <summary>Interface language code (e.g. "zh-CN", "en"). Defaults to Simplified Chinese on
    /// first launch; persisted so the choice survives restarts. See <see cref="Localization.LocalizationService"/>.</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>Options for the client-item Autocomplete generator.</summary>
    public MidgardStudio.Core.Lua.AutocompleteConfig Autocomplete { get; set; } = new();
}

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON in %APPDATA%\Midgard Studio.</summary>
public sealed class AppSettingsService
{
    /// <summary>The configurable keyboard shortcuts: action key, friendly label, default gesture.</summary>
    public static readonly (string Key, string Display, string Default)[] ShortcutDefs =
    {
        ("Save", "Save", "Ctrl+S"),
        ("Undo", "Undo", "Ctrl+Z"),
        ("Redo", "Redo", "Ctrl+Y"),
        ("NewEntry", "New entry", "Ctrl+N"),
        ("Duplicate", "Duplicate entry", "Ctrl+D"),
        ("CopyYaml", "Copy entry as YAML", "Ctrl+Shift+C"),
        ("Forge", "Forge new item", "Ctrl+Shift+N"),
        ("FindInList", "Find in current list", "Ctrl+F"),
        ("FindEverywhere", "Find in all databases", "Ctrl+Shift+F"),
        ("QuickOpen", "Quick open", "Ctrl+K"),
        ("Reload", "Reload database", "F5"),
        ("Validate", "Run validation", "F6"),
        ("ToggleMode", "Toggle Renewal / Pre-Renewal", "Ctrl+M"),
        ("BackupManager", "Backup manager", "Ctrl+Shift+B"),
        ("Settings", "Settings", "Ctrl+Shift+OemComma"),
        ("Configuration", "Profiles & configuration", "Ctrl+OemComma"),
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    public AppSettingsService()
    {
        _path = Path.Combine(MidgardStudio.Core.AppPaths.RoamingDir, "app-settings.json");
        Settings = Load();
    }

    public AppSettings Settings { get; }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch { /* corrupt settings -> defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        // Atomic write so a crash mid-save can't truncate the settings file.
        var tmp = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, Json));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
        catch
        {
            // Don't leave a half-written .tmp behind in %APPDATA% if the replace/move failed.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
