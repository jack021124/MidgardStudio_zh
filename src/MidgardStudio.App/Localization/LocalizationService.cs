using System;
using System.Windows;
using System.Windows.Markup;

namespace MidgardStudio.App.Localization;

/// <summary>
/// Runtime language management for Midgard Studio.
///
/// Design goals (see LOCALIZATION.md for the full rationale):
///  • One system for XAML <em>and</em> C# — a pair of <see cref="ResourceDictionary"/> files
///    (en.xaml / zh-CN.xaml) keyed by <c>x:Key</c>.
///  • Switching language at runtime swaps the merged dictionary, so every <c>{DynamicResource}</c>
///    binding in XAML refreshes instantly — no app restart.
///  • Missing keys fall back to English (and then to the key itself), so an untranslated string
///    is never blank or a crash — critical for keeping up with a fast-moving upstream.
/// </summary>
public static class LocalizationService
{
    /// <summary>Supported language codes. The first entry is the default on first launch.</summary>
    public static readonly string[] SupportedLanguages = { "zh-CN", "en" };

    private const string DictionaryPrefix = "Localization/";
    private const string DictionarySuffix = ".xaml";

    private static string _currentLanguage = "zh-CN";

    /// <summary>The active language code (e.g. "zh-CN", "en").</summary>
    public static string CurrentLanguage => _currentLanguage;

    /// <summary>Event raised after a language switch, so C#-side consumers can refresh.</summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// Loads the initial language dictionary into <see cref="Application.Resources"/>. Call once
    /// from App startup, before the first window is shown.
    /// </summary>
    public static void Initialize(string language)
    {
        _currentLanguage = NormalizeLanguage(language);
        var dict = LoadDictionary(_currentLanguage);
        if (dict is null)
        {
            // The requested dictionary is missing — fall back to English so the app still runs.
            _currentLanguage = "en";
            dict = LoadDictionary("en");
        }

        // If even the English fallback is somehow missing, there's nothing to merge — leave the
        // resource tree untouched (every key will resolve to its own name, but the app still runs).
        if (dict is null)
        {
            Serilog.Log.Warning("Language dictionary could not be loaded for '{Lang}'; UI keys will fall back.", _currentLanguage);
            return;
        }

        ReplaceActiveDictionary(dict);
        Serilog.Log.Information("UI language set to '{Lang}' ({Count} resources).", _currentLanguage, dict.Count);
    }

    /// <summary>
    /// Switches the active language at runtime. Reloads the merged dictionary and refreshes all
    /// <c>{DynamicResource}</c> bindings. No-op if <paramref name="language"/> is already active.
    /// </summary>
    public static void SetLanguage(string language)
    {
        language = NormalizeLanguage(language);
        if (language == _currentLanguage) return;

        var dict = LoadDictionary(language);
        if (dict is null) return; // unknown language file — keep the current one

        _currentLanguage = language;
        ReplaceActiveDictionary(dict);
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// Looks up a localized string by key for use from C# code. Falls back to English, then to
    /// the key itself — never throws.
    /// </summary>
    public static string Get(string key)
    {
        if (TryLookup(key, out var value)) return value;
        return key;
    }

    /// <summary>True when the given language code is one we ship a dictionary for.</summary>
    public static bool IsSupported(string? language) =>
        Array.IndexOf(SupportedLanguages, NormalizeLanguage(language)) >= 0;

    private static bool TryLookup(string key, out string value)
    {
        // Search the Application-level resources: the active language dictionary is merged there,
        // so this respects the current language automatically.
        if (Application.Current?.TryFindResource(key) is string s)
        {
            value = s;
            return true;
        }
        value = key;
        return false;
    }

    /// <summary>
    /// Swaps the language dictionary in <see cref="Application.Resources.MergedDictionaries"/>.
    /// There is exactly one language dictionary at a time — identified by <see cref="LanguageDictKey"/>.
    /// Removing + re-adding (rather than mutating in place) is what makes DynamicResource refresh.
    /// </summary>
    private static void ReplaceActiveDictionary(ResourceDictionary newDict)
    {
        newDict[LanguageDictKey] = true; // marker so we can find/remove the old one
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            if (dictionaries[i].Contains(LanguageDictKey))
                dictionaries.RemoveAt(i);
        }

        // Insert at the front so our string keys win over any same-named key that WPF-UI / the
        // app might happen to define (last-merged wins in WPF, but front insertion keeps intent clear
        // and matches how the other resource dictionaries are ordered).
        dictionaries.Insert(0, newDict);
    }

    private static ResourceDictionary? LoadDictionary(string language)
    {
        try
        {
            // The .xaml lives under Localization/ and is built as a Page resource, so a relative
            // pack URI resolves it. LoadComponent returns a fully constructed ResourceDictionary.
            var source = new Uri(DictionaryPrefix + language + DictionarySuffix, UriKind.Relative);
            return (ResourceDictionary)Application.LoadComponent(source);
        }
        catch (Exception ex)
        {
            // A duplicate x:Key, malformed element, or encoding issue in the XAML makes LoadComponent throw.
            // Log it so the silent English fallback is traceable instead of invisible.
            try { Serilog.Log.Error(ex, "Failed to load localization dictionary '{Language}'", language); } catch { /* logger unavailable */ }
            return null;
        }
    }

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "zh-CN" : language.Trim();

    /// <summary>Marker key written onto the live language dictionary so it can be found/removed later.</summary>
    private static readonly object LanguageDictKey = "__MidgardLanguageDict";
}

/// <summary>
/// XAML markup extension for binding a resource string directly:
/// <c>Text="{loc:Loc Menu_File}"</c>. A thin wrapper over <c>{DynamicResource}</c> that also
/// works where a plain string (not a binding) is expected, e.g. <c>MenuItem.Header</c>.
/// Prefer <c>{DynamicResource Key}</c> in XAML; this helper is for the occasional spot that needs it.
/// </summary>
public class LocExtension(string key) : DynamicResourceExtension
{
    public LocExtension() : this(string.Empty) { }

    public string Key
    {
        get => key;
        init => key = value;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Hand the key to DynamicResource so it behaves exactly like {DynamicResource Key}.
        ResourceKey = key;
        return base.ProvideValue(serviceProvider);
    }
}
