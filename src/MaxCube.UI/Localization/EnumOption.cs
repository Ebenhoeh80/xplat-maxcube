namespace MaxCube.UI.Localization;

/// <summary>
/// Auswahloption mit dem unveränderten Domänenwert und einem lokalisierten Label. Die ComboBox
/// zeigt <see cref="ToString"/>, das ViewModel bindet weiter an <see cref="Value"/>.
/// </summary>
public sealed record EnumOption<T>(T Value, string Label)
{
    public override string ToString() => Label;

    // Die Auswahl bleibt über Sprachwechsel erhalten, obwohl Labels neu erzeugt werden.
    public bool Equals(EnumOption<T>? other)
        => other is not null && EqualityComparer<T>.Default.Equals(Value, other.Value);

    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
}
