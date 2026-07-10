using System.Collections.Concurrent;
using System.IO;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.Lookup;
using MidgardStudio.Core.Lua;
using MidgardStudio.Core.Overlay;
using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Validation;
using MidgardStudio.Core.Workspace;

namespace MidgardStudio.App.Services;

/// <summary>
/// Holds the live editing state: the active mode, the global undo stack, the validation service, the
/// script catalog, and a cache of per-database ModeSets (Renewal + Pre-Renewal sharing one import).
/// The active profile can be swapped at runtime via <see cref="ApplyProfile"/>.
/// </summary>
public sealed class WorkspaceSession
{
    private readonly WorkspaceLoader _loader = new();
    private readonly ConcurrentDictionary<string, ModeSet> _modeSets = new(StringComparer.Ordinal);
    private WorkspaceConfig _config;

    public WorkspaceSession(IWorkspaceConfigService configService, AppSettingsService appSettings)
    {
        _config = configService.Load();
        Mode = _config.DefaultMode;
        Validation = ValidationEngine.CreateDefault();
        ScriptCatalog = LoadScriptCatalog(_config);
        _clientCodec = new LuaFileCodec(appSettings.Settings.GlobalCodepage);
    }

    public EditCommandStack Commands { get; } = new();

    private LuaFileCodec _clientCodec;

    /// <summary>The codec for loose client lua/.lub files. Follows the global text-encoding setting
    /// (Settings ▸ General ▸ Global text encoding, <see cref="AppSettings.GlobalCodepage"/>, default 1252).
    /// See <see cref="ApplyClientCodepage"/> to swap it at runtime; the client services drop their caches
    /// via <see cref="WorkspaceReloaded"/> and re-read on next access.</summary>
    public LuaFileCodec ClientCodec => _clientCodec;

    /// <summary>Rebuilds the client codec from a new codepage and drops cached client data so files are
    /// re-read with the new encoding on next access. Call when the global text encoding changes.</summary>
    public void ApplyClientCodepage(int codepage)
    {
        if (codepage <= 0) codepage = 1252;
        if (codepage == _clientCodec.Codepage) return;
        _clientCodec = new LuaFileCodec(codepage);
        // Client services subscribe to WorkspaceReloaded to drop their caches; fire it so they re-read
        // with the new codec on next access. (Server DB caches are unaffected — those are UTF-8.)
        WorkspaceReloaded?.Invoke();
    }

    /// <summary>The profile's codepage used as the fallback when a SERVER YAML db isn't valid UTF-8
    /// (a Latin-1 / EUC-KR translated item_db). Guarded so a missing/0 value falls back to 1252.</summary>
    public int ClientCodepage => SafeCodepage(_config);

    private static int SafeCodepage(WorkspaceConfig config) => config.ClientCodepage > 0 ? config.ClientCodepage : 1252;

    public ValidationEngine Validation { get; }

    public ScriptCommandCatalog ScriptCatalog { get; private set; }

    public ServerMode Mode { get; private set; }

    public WorkspacePaths Paths => _config.Paths;

    public WorkspaceConfig Config => _config;

    public event Action? ModeChanged;

    /// <summary>Raised after <see cref="ApplyProfile"/> swaps the active profile (caches were reset).</summary>
    public event Action? WorkspaceReloaded;

    /// <summary>Swaps the active profile: resets the data caches, the undo stack and the script catalog
    /// so subsequent loads read the new server's files. Safe to call from the UI thread.</summary>
    public void ApplyProfile(WorkspaceConfig config)
    {
        _config = config;
        Mode = config.DefaultMode;
        _modeSets.Clear();
        Commands.Clear();
        ScriptCatalog = LoadScriptCatalog(config);
        WorkspaceReloaded?.Invoke();
        ModeChanged?.Invoke();
    }

    /// <summary>Loads (or returns cached) the ModeSet for a schema. Safe to call from a background thread.
    /// Only the current mode's base is parsed eagerly; the other mode loads on first <see cref="ModeSet.For"/>.</summary>
    public ModeSet GetModeSet(DbSchema schema) =>
        _modeSets.GetOrAdd(schema.Id, _ => _loader.LoadModeSet(schema, _config.Paths, Mode, SafeCodepage(_config)));

    /// <summary>The overlay for the current mode.</summary>
    public OverlayTable GetActiveOverlay(DbSchema schema) => GetModeSet(schema).For(Mode);

    public void SetMode(ServerMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        ModeChanged?.Invoke();
    }

    public bool IsDirty => _modeSets.Values.Any(m => m.IsDirty);

    /// <summary>Schema ids of databases with unsaved edits (drives the auto-backup label).</summary>
    public IReadOnlyList<string> DirtyDatabaseIds() =>
        _modeSets.Where(kv => kv.Value.IsDirty).Select(kv => kv.Key).ToList();

    /// <summary>Schema id + the import file path that will be written for each database with unsaved edits.
    /// Capture before <see cref="SaveAll"/> (which clears the dirty flags) to label the save summary.</summary>
    public IReadOnlyList<(string Id, string ImportFilePath)> DirtySaveTargets() =>
        _modeSets.Where(kv => kv.Value.IsDirty)
                 .Select(kv => (kv.Key, kv.Value.ImportFilePath)) // mode-independent; doesn't build the inactive mode
                 .ToList();

    public int SaveAll()
    {
        int saved = 0;
        foreach (var modeSet in _modeSets.Values.Where(m => m.IsDirty))
        {
            // Both overlays share one import layer; saving through the built (active) overlay writes the
            // single import file — no need to build the inactive mode just to save.
            modeSet.Save();
            saved++;
        }
        Commands.MarkSaved();
        return saved;
    }

    private static ScriptCommandCatalog LoadScriptCatalog(WorkspaceConfig config)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Paths.ServerDbRoot)) return ScriptCommandCatalog.LoadFromDocs(string.Empty);
            string docsDir = Path.GetFullPath(Path.Combine(config.Paths.ServerDbRoot, "..", "..", "docs"));
            return ScriptCommandCatalog.LoadFromDocs(docsDir);
        }
        catch
        {
            return ScriptCommandCatalog.LoadFromDocs(string.Empty);
        }
    }
}
