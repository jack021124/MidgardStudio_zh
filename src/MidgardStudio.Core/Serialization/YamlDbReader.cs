using System.Globalization;
using System.Text;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schema;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MidgardStudio.Core.Serialization;

/// <summary>
/// Reads a rAthena YAML database file into <see cref="DbFile"/> using YamlDotNet's streaming event
/// parser (not the DOM, which rejects the duplicate keys present in some official files). Each Body
/// entry is coerced into a schema-described <see cref="DbRecord"/>; unknown keys go to
/// <see cref="DbRecord.Extras"/>. Duplicate keys resolve last-wins.
/// </summary>
public sealed class YamlDbReader
{
    static YamlDbReader()
    {
        // cp1252 / cp949 / cp1251 / … aren't built into .NET 8 — register the Windows codepage provider
        // so the legacy-encoding fallback below (Encoding.GetEncoding) can resolve them. The App also
        // registers this at startup; doing it here too keeps headless Core/test usage self-sufficient.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Reads a rAthena YAML database file. rAthena db files are UTF-8, so UTF-8 is tried first (strict).
    /// Some translated private-server packs store names in a legacy codepage (e.g. a Brazilian item_db
    /// saved as Windows-1252, a kRO db in EUC-KR); when the bytes aren't valid UTF-8 we fall back to
    /// <paramref name="fallbackCodepage"/> so those names display correctly instead of becoming U+FFFD
    /// replacement characters. Writing is always UTF-8 (see <see cref="YamlDbWriter"/>) — independent of
    /// this display fallback, so the import layer this app writes stays standard.
    /// </summary>
    public DbFile ReadFile(string path, DbSchema schema, RecordOrigin origin = RecordOrigin.Base, int fallbackCodepage = 1252)
        => Read(DecodeBytes(File.ReadAllBytes(path), fallbackCodepage), schema, origin);

    /// <summary>Decodes db bytes as UTF-8 when valid, else as the configured legacy codepage.</summary>
    private static string DecodeBytes(byte[] bytes, int fallbackCodepage)
    {
        // A UTF-8 BOM is unambiguous; strip it. Otherwise try strict UTF-8 and only fall back on bytes
        // that can't be valid UTF-8 (a legacy single/double-byte db).
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(fallbackCodepage <= 0 ? 1252 : fallbackCodepage).GetString(bytes); }
            catch (ArgumentException) { return Encoding.Latin1.GetString(bytes); } // unknown codepage -> Latin-1 (every byte maps)
        }
    }

    public DbFile Read(string yaml, DbSchema schema, RecordOrigin origin = RecordOrigin.Base)
    {
        // Tolerate a class of malformed rAthena db files where a string field's plain-scalar value itself
        // contains ": " (colon-space) — common in private-server packs with translated item names like
        // `Name: 春之季外套: 春`. YAML reads ": " as a mapping separator, so YamlDotNet throws
        // "While scanning a plain scalar value, found invalid mapping." We wrap just those values in double
        // quotes for PARSING ONLY (the parsed string is identical; quotes are stripped by the emitter). The
        // on-disk file is never modified — base files are read-only and saves only touch the import layer.
        yaml = QuoteAmbiguousScalars(yaml);
        using var reader = new StringReader(yaml);
        return Read(reader, schema, origin);
    }

    /// <summary>Wraps plain-scalar mapping values that contain ": " in double quotes, line by line, so the
    /// YAML scanner doesn't mistake them for nested mappings. Only touches a line that looks like an
    /// indented mapping entry (<c>key: value</c>) whose value is NOT already quoted/block and contains a
    /// ": " after the key. Sequence items (<c>- key: value</c>), already-quoted values, and block scalars
    /// (<c>|</c>/<c>&gt;</c>) are left alone.</summary>
    private static string QuoteAmbiguousScalars(string yaml)
    {
        if (string.IsNullOrEmpty(yaml) || yaml.IndexOf(": ") < 0) return yaml;

        var lines = yaml.Split('\n');
        bool changed = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // Must be an indented mapping entry (leading spaces, then "Key: value"). Skip sequence items
            // ("- Key: value") and top-level keys (no indent) — those have well-formed values in practice,
            // and rewriting them risks mis-detecting document structure.
            int firstNonWs = 0;
            while (firstNonWs < line.Length && line[firstNonWs] == ' ') firstNonWs++;
            if (firstNonWs == 0 || firstNonWs >= line.Length) continue;        // no indent, or blank
            if (line[firstNonWs] == '-' || line[firstNonWs] == '#') continue;  // sequence item or comment
            int colon = line.IndexOf(": ", firstNonWs, StringComparison.Ordinal);
            if (colon < 0) continue;

            // The value starts after "Key: ". Check whether the value ITSELF contains another ": ".
            int valueStart = colon + 2;
            if (valueStart >= line.Length) continue;
            char v0 = line[valueStart];
            if (v0 == '"' || v0 == '\'' || v0 == '|' || v0 == '>' || v0 == '&' || v0 == '*' || v0 == '!' || v0 == '%' || v0 == '@' || v0 == '`')
                continue;  // already quoted / block scalar / anchor / alias / tag / directive — leave it
            int innerColon = line.IndexOf(": ", valueStart, StringComparison.Ordinal);
            if (innerColon < 0) continue;  // value has no ": " — fine as-is

            // Wrap the value in double quotes, escaping any literal double-quotes inside it. Trailing \r
            // (CRLF files split on \n) is kept outside the quotes.
            string value = line.Substring(valueStart);
            string trailing = string.Empty;
            if (value.EndsWith('\r')) { trailing = "\r"; value = value.Substring(0, value.Length - 1); }
            value = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            lines[i] = line.Substring(0, valueStart) + "\"" + value + "\"" + trailing;
            changed = true;
        }
        return changed ? string.Join('\n', lines) : yaml;
    }

    public DbFile Read(TextReader reader, DbSchema schema, RecordOrigin origin = RecordOrigin.Base)
    {
        var file = new DbFile { HeaderType = schema.HeaderType, HeaderVersion = schema.HeaderVersion };

        var parser = new Parser(reader);
        parser.Consume<StreamStart>();
        if (!parser.TryConsume<DocumentStart>(out _))
            return file;

        if (ReadGeneric(parser) is not Dictionary<string, object?> root)
            return file;

        if (root.TryGetValue("Header", out var h) && h is Dictionary<string, object?> header)
        {
            if (header.TryGetValue("Type", out var t) && t is string ts && !string.IsNullOrEmpty(ts))
                file.HeaderType = ts;
            if (header.TryGetValue("Version", out var v) && int.TryParse(v as string, out var ver))
                file.HeaderVersion = ver;
        }

        if (root.TryGetValue("Body", out var b) && b is List<object?> body)
        {
            foreach (var entry in body)
            {
                if (entry is Dictionary<string, object?> dict)
                {
                    var record = CoerceRecord(dict, schema, origin);
                    record.AttachNestedOwners();
                    file.Records.Add(record);
                }
            }
        }

        return file;
    }

    /// <summary>Materializes the next YAML node as a generic tree: string | Dictionary | List.</summary>
    private static object? ReadGeneric(IParser parser)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
            return scalar.Value;

        if (parser.TryConsume<MappingStart>(out _))
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value ?? string.Empty;
                map[key] = ReadGeneric(parser); // last-wins on duplicate keys
            }
            return map;
        }

        if (parser.TryConsume<SequenceStart>(out _))
        {
            var list = new List<object?>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                list.Add(ReadGeneric(parser));
            return list;
        }

        // Anchors/aliases/other — skip a single event to keep progressing.
        parser.MoveNext();
        return null;
    }

    private static DbRecord CoerceRecord(Dictionary<string, object?> dict, DbSchema schema, RecordOrigin origin)
    {
        var record = new DbRecord(schema) { Origin = origin };
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in schema.Fields)
        {
            if (dict.TryGetValue(field.Name, out var raw) && raw is not null && TryCoerce(raw, field, out var value))
            {
                record.SetRaw(field.Name, value);
                consumed.Add(field.Name);
            }
            // A value whose YAML shape doesn't match the field kind (e.g. a per-level array on a scalar
            // field) is left unconsumed and preserved verbatim in Extras below, so it round-trips.
        }

        foreach (var (key, value) in dict)
        {
            if (!consumed.Contains(key))
                record.Extras[key] = value;
        }

        record.IsDirty = false;
        return record;
    }

    /// <summary>Coerces a raw YAML value into the typed model value, returning false when the value's
    /// shape doesn't match the field kind (so the caller preserves it verbatim instead of losing it).</summary>
    private static bool TryCoerce(object raw, FieldSchema field, out object? value)
    {
        value = null;
        switch (field.Kind)
        {
            case FieldKind.Int:
                if (raw is not string ints || !int.TryParse(ints, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                    return false; // unparseable -> preserve verbatim in Extras instead of silently coercing to 0
                value = iv; return true;
            case FieldKind.Long:
                if (raw is not string longs || !long.TryParse(longs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return false;
                value = l; return true;
            case FieldKind.Bool:
                if (raw is not string) return false;
                value = ParseBool((string)raw); return true;
            case FieldKind.String:
            case FieldKind.Enum:
            case FieldKind.Reference:
                if (raw is not string) return false;
                value = (string)raw; return true;
            case FieldKind.Script:
                if (raw is not string) return false;
                value = new ScriptValue((string)raw); return true;
            case FieldKind.Flags:
            case FieldKind.BoolMap:
                if (raw is not Dictionary<string, object?>) return false;
                value = ReadBoolSet(raw); return true;
            case FieldKind.Object:
                if (raw is not Dictionary<string, object?> m || field.ObjectSchema is null) return false;
                value = CoerceRecord(m, field.ObjectSchema, RecordOrigin.Base); return true;
            case FieldKind.ObjectList:
                if (raw is not List<object?> || field.ObjectSchema is null) return false;
                value = ReadObjectList(raw, field); return true;
            case FieldKind.ScalarList:
                if (raw is not List<object?> sl) return false;
                value = new List<object?>(sl); return true;
            case FieldKind.LevelInt:
                if (raw is not string && raw is not List<object?>) return false;
                var level = ReadLevel(raw, field, out var levelComplete);
                if (!levelComplete) return false; // a malformed per-level entry -> preserve the raw value in Extras
                value = level; return true;
            default:
                if (raw is not string) return false;
                value = (string)raw; return true;
        }
    }

    private static LevelList ReadLevel(object raw, FieldSchema field, out bool complete)
    {
        var list = new LevelList();
        complete = true;
        if (raw is string s)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) list.Scalar = v;
            else complete = false; // non-numeric scalar -> let the caller preserve it verbatim
        }
        else if (raw is List<object?> seq)
        {
            foreach (var item in seq)
            {
                if (item is Dictionary<string, object?> m
                    && m.TryGetValue("Level", out var lo) && int.TryParse(lo as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl)
                    && m.TryGetValue(field.LevelValueKey, out var vo) && int.TryParse(vo as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                    list.Levels.Add(new LevelEntry(lvl, val));
                else
                    complete = false; // an entry didn't match {Level, <valueKey>} -> preserve the whole raw list
            }
        }
        return list;
    }

    private static BoolMap ReadBoolSet(object raw)
    {
        var map = new BoolMap();
        if (raw is Dictionary<string, object?> dict)
        {
            foreach (var (key, value) in dict)
            {
                if (ParseBool(value as string)) map.Add(key);
                else map.Excluded.Add(key); // token: false → an exclusion, preserved for round-trip / "all-except"
            }
        }
        return map;
    }

    private static List<DbRecord> ReadObjectList(object raw, FieldSchema field)
    {
        var list = new List<DbRecord>();
        if (raw is List<object?> seq && field.ObjectSchema is not null)
        {
            foreach (var item in seq)
            {
                if (item is Dictionary<string, object?> m)
                    list.Add(CoerceRecord(m, field.ObjectSchema, RecordOrigin.Base));
            }
        }
        return list;
    }

    private static bool ParseBool(string? s) =>
        s is not null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");
}
