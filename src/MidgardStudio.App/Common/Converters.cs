using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.App.Common;

/// <summary>Maps a record origin to an accent brush for the list pill.</summary>
public sealed class OriginToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RecordOrigin.Overridden => new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)), // amber
        RecordOrigin.NewCustom => new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x8A)),  // teal
        _ => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),                        // muted
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a record origin to a short label.</summary>
public sealed class OriginToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RecordOrigin.Overridden => Localization.LocalizationService.Get("Db_Origin_Override"),
        RecordOrigin.NewCustom => Localization.LocalizationService.Get("Db_Origin_Custom"),
        _ => Localization.LocalizationService.Get("Db_Origin_Base"),
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a validation severity enum to a localized label (the Severity column chip text).</summary>
public sealed class SeverityToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ValidationSeverity.Error => Localization.LocalizationService.Get("Val_Sev_Error"),
        ValidationSeverity.Warning => Localization.LocalizationService.Get("Val_Sev_Warning"),
        ValidationSeverity.Info => Localization.LocalizationService.Get("Val_Sev_Info"),
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A validation issue -> its display category (the Database column): the explicit Category when set,
/// else the friendly label of its DbId. Bind the whole issue: <c>{Binding Converter={StaticResource ...}}</c>.</summary>
public sealed class IssueCategoryConverter : IValueConverter
{
    /// <summary>Internal Category constants (stored on the issue) → localization resource key.</summary>
    private static readonly Dictionary<string, string> CategoryMap = new(StringComparer.Ordinal)
    {
        ["Client Mobs"] = "ValSrc_ClientMobs",
        ["Cash Shop"] = "Label_CashShop",
    };

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ValidationIssue i) return string.Empty;
        if (i.Category is { } cat && CategoryMap.TryGetValue(cat, out var resKey))
            return Localization.LocalizationService.Get(resKey);
        return i.Category ?? DbIdToSourceLabelConverter.Label(i.DbId);
    }

    /// <summary>Resolves the localized display label for an issue's category (shared by the source-chip
    /// builder in <c>ValidationViewModel</c>, which has the issue but binds the chip text to a string).</summary>
    public static string ResolveLabel(ValidationIssue issue)
    {
        if (issue.Category is { } cat && CategoryMap.TryGetValue(cat, out var resKey))
            return Localization.LocalizationService.Get(resKey);
        return issue.Category ?? DbIdToSourceLabelConverter.Label(issue.DbId);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A validation issue -> the file the user edits to fix it (the Filename column).</summary>
public sealed class IssueFileConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ValidationIssue i) return string.Empty;
        return i.DbId switch
        {
            "client_items" => "itemInfo_C.lua",
            "client_skills" => i.Field switch
            {
                "Description" => "skilldescript.lub",
                "SKID" => "skillid.lub",
                _ => "skillinfolist.lub",
            },
            "mob_db" when i.Category == "Client Mobs" => "npcidentity.lub",
            _ => i.DbId + ".yml", // server db import file
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A count (int) > 0 -> Visible, else Collapsed (for optional metadata sections).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true -> Wrap, false -> NoWrap (the preview text "wrap" toggle).</summary>
public sealed class BoolToTextWrappingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A full path -> just the file name (for the GRF source picker).</summary>
public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? System.IO.Path.GetFileName(s) : value;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a validation issue's db id to a friendly source label for the grouped panel headers
/// ("item_db" -> "Items", "client_skills" -> "Client Skills"); unknown ids are humanized as a fallback.</summary>
public sealed class DbIdToSourceLabelConverter : IValueConverter
{
    /// <summary>db id → localization resource key. Values resolve through LocalizationService so the
    /// labels follow the active language (the old static English map is gone).</summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["item_db"] = "ValSrc_Items",
        ["mob_db"] = "ValSrc_Monsters",
        ["skill_db"] = "ValSrc_Skills",
        ["client_skills"] = "ValSrc_ClientSkills",
        ["client_items"] = "ValSrc_ClientItems",
        ["item_combo_db"] = "ValSrc_ItemCombos",
        ["mob_skill_db"] = "ValSrc_MobSkills",
    };

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        Label(value as string);

    /// <summary>The friendly source label for a db id (shared by the XAML group/chip and the view model).</summary>
    public static string Label(string? id)
    {
        if (string.IsNullOrEmpty(id)) return Localization.LocalizationService.Get("ValSrc_Other");
        if (Map.TryGetValue(id, out var resKey)) return Localization.LocalizationService.Get(resKey);
        var words = id.Replace("_db", string.Empty).Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string -> true (used to open an InfoBar when there is a message).</summary>
public sealed class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean (used for IsReadOnly = !IsEditable).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);
}

/// <summary>Non-null -> Visible, null -> Collapsed.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string -> Visible, else Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True -> Visible, False -> Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}
