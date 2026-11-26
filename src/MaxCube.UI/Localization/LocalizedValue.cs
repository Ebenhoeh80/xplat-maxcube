namespace MaxCube.UI.Localization;

/// <summary>
/// Ein bereits gerenderteter Oberflächentext zusammen mit seiner Renderfunktion. Die Funktion
/// bleibt nach dem Sprachwechsel erhalten, sodass auch zwischengespeicherte VM-Properties wie
/// Status, Erfolgsmeldung oder Schreibvorschau ohne Cube-Zugriff neu lokalisiert werden können.
/// </summary>
public sealed class LocalizedValue
{
    private readonly Func<Localizer, string>? _render;

    public LocalizedValue(string value)
    {
        Current = value;
        _render = null;
    }

    public LocalizedValue(Func<Localizer, string> render, Localizer localizer)
    {
        _render = render;
        Current = render(localizer);
    }

    private LocalizedValue(string current, Func<Localizer, string> render)
    {
        Current = current;
        _render = render;
    }

    /// <summary>Text in der Sprache, in der er erzeugt wurde.</summary>
    public string Current { get; }

    /// <summary>Neue Instanz mit dem Text in der jetzt gewählten Sprache.</summary>
    public LocalizedValue In(Localizer localizer)
        => new(_render is null ? Current : _render(localizer));

    /// <summary>Hängt ein sprachneutrales Suffix an, ohne die Renderfunktion zu verlieren.</summary>
    public LocalizedValue Append(string suffix)
        => _render is null
            ? new LocalizedValue(Current + suffix)
            : new LocalizedValue(Current + suffix, localizer => _render(localizer) + suffix);

    /// <summary>Erzeugt einen Wert aus einer dynamischen Vorlage (z. B. mit Ausnahmetext).</summary>
    public static LocalizedValue From(Func<Localizer, string> render, Localizer localizer) => new(render, localizer);

    public static implicit operator string(LocalizedValue value) => value.Current;

    public override string ToString() => Current;
}
