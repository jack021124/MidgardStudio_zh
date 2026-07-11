using System;
using System.IO;
using System.Linq;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Workspace;
using Xunit;

namespace MidgardStudio.Tests;

/// <summary>The profile-load compatibility pre-check: warn/block on files we can't safely round-trip,
/// before the user can load the profile and (potentially) save over them. (audit #8 / #20 / #21 / #4)</summary>
public class ProfileCompatibilityCheckTests
{
    [Fact]
    public void Flags_a_wrong_type_import_and_a_missing_client_table()
    {
        string root = Path.Combine(Path.GetTempPath(), "ms-compat-" + Guid.NewGuid().ToString("N"));
        string serverDb = Path.Combine(root, "db");
        string lua = Path.Combine(root, "lua");
        Directory.CreateDirectory(Path.Combine(serverDb, "import"));
        Directory.CreateDirectory(Path.Combine(lua, "skillinfoz"));
        try
        {
            // a MOB_DB document mis-placed at import/item_db.yml (wrong db for this path)
            File.WriteAllText(Path.Combine(serverDb, "import", "item_db.yml"),
                "Header:\n  Type: MOB_DB\n  Version: 5\nBody: []\n");
            // a skillid.lub whose SKID table was renamed (old/unsupported client)
            File.WriteAllText(Path.Combine(lua, "skillinfoz", "skillid.lub"), "SKILL_IDs = {\n\tNV_BASIC = 0,\n}\n");

            var paths = new WorkspacePaths { ServerDbRoot = serverDb, LuaFilesRoot = lua };
            var findings = ProfileCompatibilityCheck.Run(paths, new[] { ItemDbSchema.Instance }, ServerMode.Renewal, 1252);

            Assert.Contains(findings, f => f.Severity == CompatSeverity.Blocker && f.File == "item_db.yml");
            Assert.Contains(findings, f => f.Severity == CompatSeverity.Warning && f.File == "skillid.lub");
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }

    [Fact]
    public void A_matching_profile_yields_no_findings()
    {
        string root = Path.Combine(Path.GetTempPath(), "ms-compat-ok-" + Guid.NewGuid().ToString("N"));
        string serverDb = Path.Combine(root, "db");
        Directory.CreateDirectory(Path.Combine(serverDb, "import"));
        try
        {
            var schema = ItemDbSchema.Instance;
            File.WriteAllText(Path.Combine(serverDb, "import", "item_db.yml"),
                $"Header:\n  Type: {schema.HeaderType}\n  Version: {schema.HeaderVersion}\nBody: []\n");

            var paths = new WorkspacePaths { ServerDbRoot = serverDb };
            var findings = ProfileCompatibilityCheck.Run(paths, new[] { schema }, ServerMode.Renewal, 1252);

            Assert.Empty(findings);
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }

    [Fact]
    public void Reports_version_drift_once_when_both_import_and_base_drift()
    {
        // Both import/ and re/ declare a Version older than the editor's — previously this emitted TWO
        // identical VersionDrift warnings (one per file); the base-file check is now suppressed when the
        // import file already drifted, so the user sees it only once.
        string root = Path.Combine(Path.GetTempPath(), "ms-compat-dup-" + Guid.NewGuid().ToString("N"));
        string serverDb = Path.Combine(root, "db");
        Directory.CreateDirectory(Path.Combine(serverDb, "import"));
        Directory.CreateDirectory(Path.Combine(serverDb, "re"));
        try
        {
            var schema = ItemDbSchema.Instance;
            int older = schema.HeaderVersion - 1;
            File.WriteAllText(Path.Combine(serverDb, "import", "item_db.yml"),
                $"Header:\n  Type: {schema.HeaderType}\n  Version: {older}\nBody: []\n");
            File.WriteAllText(Path.Combine(serverDb, "re", "item_db.yml"),
                $"Header:\n  Type: {schema.HeaderType}\n  Version: {older}\nBody: []\n");

            var paths = new WorkspacePaths { ServerDbRoot = serverDb };
            var findings = ProfileCompatibilityCheck.Run(paths, new[] { schema }, ServerMode.Renewal, 1252);

            Assert.Single(findings, f => f.MessageKey == "Compat_VersionDrift");
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }

    [Fact]
    public void Reports_version_drift_for_base_file_when_import_does_not_drift()
    {
        // import/ is current but re/ is old — the base file's drift is still surfaced (the dedup only
        // suppresses the base check when the import ALREADY drifted). Uses mob_db, whose base file is
        // re/mob_db.yml (Standard layout: same name as the import file).
        string root = Path.Combine(Path.GetTempPath(), "ms-compat-base-" + Guid.NewGuid().ToString("N"));
        string serverDb = Path.Combine(root, "db");
        Directory.CreateDirectory(Path.Combine(serverDb, "import"));
        Directory.CreateDirectory(Path.Combine(serverDb, "re"));
        try
        {
            var schema = MobDbSchema.Instance;
            File.WriteAllText(Path.Combine(serverDb, "import", "mob_db.yml"),
                $"Header:\n  Type: {schema.HeaderType}\n  Version: {schema.HeaderVersion}\nBody: []\n");
            File.WriteAllText(Path.Combine(serverDb, "re", "mob_db.yml"),
                $"Header:\n  Type: {schema.HeaderType}\n  Version: {schema.HeaderVersion - 1}\nBody: []\n");

            var paths = new WorkspacePaths { ServerDbRoot = serverDb };
            var findings = ProfileCompatibilityCheck.Run(paths, new[] { schema }, ServerMode.Renewal, 1252);

            Assert.Single(findings, f => f.MessageKey == "Compat_VersionDrift");
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }
}
