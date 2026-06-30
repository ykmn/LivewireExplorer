using System.Windows;
using LivewireBrowser.Core.Settings;

namespace LivewireBrowser.App.Localization;

/// <summary>
/// Thin wrapper over the merged Strings.{lang}.xaml ResourceDictionary (Application-level)
/// that's the single source of truth for every UI string — XAML reads it via
/// {DynamicResource Str_X} (live-updates automatically when the dictionary is swapped),
/// C# code (ViewModels, code-behind) reads the same entries via Loc.Get so there's no
/// separate string table to keep in sync.
/// </summary>
public static class Loc
{
    private const string LanguageDictionaryMarkerKey = "__LanguageDictionary";

    public static event Action? LanguageChanged;

    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    public static void Apply(AppLanguage language)
    {
        Current = language;
        var uri = new Uri(
            language == AppLanguage.Russian ? "Localization/Strings.ru.xaml" : "Localization/Strings.en.xaml",
            UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Contains(LanguageDictionaryMarkerKey))
                merged.RemoveAt(i);
        }

        merged.Add(dictionary);
        LanguageChanged?.Invoke();
    }

    public static string Get(string key) => Application.Current.Resources[key] as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
