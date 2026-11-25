using System.ComponentModel;
using System.Globalization;

namespace MaxCube.UI.Localization;

/// <summary>
/// Sprachumschaltung für die Oberfläche (Deutsch, Englisch, Französisch, Italienisch, Niederländisch,
/// Polnisch wie im Original: jquery.translate.lang_{de,en,fr,it,nl,pl}.js, indexController.js:125-134).
/// XAML bindet über den Indexer (<c>{Binding Loc[Schlüssel]}</c>); beim Sprachwechsel meldet das
/// Objekt <c>Item[]</c> als geändert, sodass sich alle Texte ohne Neustart aktualisieren.
/// Ein unbekannter Schlüssel erscheint als <c>!Schlüssel!</c> statt eine Ausnahme zu werfen.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<LanguageOption> Languages =
    [
        new("de", "Deutsch"),
        new("en", "English"),
        new("fr", "Français"),
        new("it", "Italiano"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
    ];

    private int _index;

    public Localizer(string language = "de") => _index = IndexOf(language);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Aktuelle Sprache als Zwei-Buchstaben-Code.</summary>
    public string Language => Languages[_index].Code;

    /// <summary>Alle bekannten Schlüssel (für Vollständigkeitstests).</summary>
    public static IEnumerable<string> Keys => Strings.Table.Keys;

    /// <summary>Die sechs Texte eines Schlüssels in der Reihenfolge von <see cref="Languages"/>.</summary>
    public static IReadOnlyList<string> TextsFor(string key) => Strings.Table[key];

    public string this[string key] => Get(key);

    /// <summary>Wechselt die Sprache; unbekannte Codes fallen auf Deutsch zurück.</summary>
    public void SetLanguage(string language)
    {
        var index = IndexOf(language);
        if (index == _index)
        {
            return;
        }

        _index = index;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    public string Get(string key)
    {
        if (!Strings.Table.TryGetValue(key, out var texts))
        {
            return $"!{key}!";
        }

        var text = texts[_index];
        return string.IsNullOrEmpty(text) ? texts[0] : text;
    }

    public string Format(string key, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>
    /// Sprachwahl wie im Original (Cookie/Browser/Einstellung, indexController.js:323-479):
    /// gespeicherte Auswahl, sonst Systemsprache (Zwei-Buchstaben-Code), sonst Deutsch.
    /// </summary>
    public static string Detect(string? stored, string? systemLanguage)
    {
        var storedCode = stored?.Trim().ToLowerInvariant();
        if (storedCode is not null && Languages.Any(language => language.Code == storedCode))
        {
            return storedCode;
        }

        var systemCode = systemLanguage?.Trim().ToLowerInvariant();
        return systemCode is not null && Languages.Any(language => language.Code == systemCode) ? systemCode : "de";
    }

    private static int IndexOf(string? language)
    {
        var code = language?.Trim().ToLowerInvariant();
        for (var index = 0; index < Languages.Count; index++)
        {
            if (Languages[index].Code == code)
            {
                return index;
            }
        }

        return 0;
    }
}

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}
