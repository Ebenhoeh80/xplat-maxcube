using System.Runtime.InteropServices;
using System.Text;

namespace MaxCube.Infrastructure.Platform;

/// <summary>
/// Ermittelt die Systemsprache ohne ICU: Die App läuft mit <c>InvariantGlobalization</c>, damit sie
/// auch auf schlanken Linux-Systemen ohne libicu startet; dort ist <c>CultureInfo.CurrentUICulture</c>
/// immer „invariant“. Linux/macOS: <c>LC_ALL</c>, <c>LC_MESSAGES</c>, <c>LANGUAGE</c>, <c>LANG</c>;
/// Windows: <c>GetUserDefaultLocaleName</c>.
/// </summary>
public static class SystemLanguage
{
    private static readonly string[] Variables = ["LC_ALL", "LC_MESSAGES", "LANGUAGE", "LANG"];

    /// <summary>Zwei-Buchstaben-Code der Systemsprache oder null, wenn sie nicht bestimmbar ist.</summary>
    public static string? Detect(Func<string, string?>? environment = null, Func<string?>? windowsLocale = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        foreach (var variable in Variables)
        {
            var code = Parse(environment(variable));
            if (code is not null)
            {
                return code;
            }
        }

        if (windowsLocale is not null || OperatingSystem.IsWindows())
        {
            return Parse((windowsLocale ?? WindowsLocaleName)());
        }

        return null;
    }

    /// <summary>
    /// Liest „de_DE.UTF-8“, „de-DE“ oder „en:de“ (erster Eintrag zählt). „C“ und „POSIX“ sind keine Sprache.
    /// </summary>
    public static string? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var first = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null || first.Equals("C", StringComparison.OrdinalIgnoreCase)
            || first.Equals("POSIX", StringComparison.OrdinalIgnoreCase) || first.Length < 2)
        {
            return null;
        }

        var code = first[..2].ToLowerInvariant();
        return code.All(char.IsAsciiLetterLower) ? code : null;
    }

    private static Func<string?> WindowsLocaleName => () =>
    {
        try
        {
            var buffer = new StringBuilder(85);
            return GetUserDefaultLocaleName(buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetUserDefaultLocaleName(StringBuilder localeName, int bufferLength);
}
