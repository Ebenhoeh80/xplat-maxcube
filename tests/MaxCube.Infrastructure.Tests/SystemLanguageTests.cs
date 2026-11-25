using FluentAssertions;
using MaxCube.Infrastructure.Platform;
using Xunit;

namespace MaxCube.Infrastructure.Tests;

/// <summary>
/// Die App läuft mit InvariantGlobalization (startet damit auch ohne libicu); die Systemsprache wird
/// deshalb aus Umgebungsvariablen (Linux) bzw. der Win32-Locale (Windows) gelesen.
/// </summary>
public class SystemLanguageTests
{
    [Theory]
    [InlineData("de_DE.UTF-8", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("EN_us", "en")]
    [InlineData("fr_FR@euro", "fr")]
    [InlineData("pl:en", "pl")]
    [InlineData("C", null)]
    [InlineData("POSIX", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("x", null)]
    [InlineData("12_34", null)]
    public void Locale_strings_are_reduced_to_the_language_code(string? value, string? expected)
    {
        SystemLanguage.Parse(value).Should().Be(expected);
    }

    [Fact]
    public void The_posix_variable_order_is_respected()
    {
        var environment = new Dictionary<string, string?>
        {
            ["LANG"] = "en_US.UTF-8",
            ["LANGUAGE"] = "it:en",
            ["LC_ALL"] = "nl_NL.UTF-8",
        };

        SystemLanguage.Detect(name => environment.GetValueOrDefault(name)).Should().Be("nl");
        environment.Remove("LC_ALL");
        SystemLanguage.Detect(name => environment.GetValueOrDefault(name)).Should().Be("it");
        environment.Remove("LANGUAGE");
        SystemLanguage.Detect(name => environment.GetValueOrDefault(name)).Should().Be("en");
    }

    [Fact]
    public void A_neutral_c_locale_falls_through_to_the_next_variable()
    {
        var environment = new Dictionary<string, string?> { ["LC_ALL"] = "C", ["LANG"] = "fr_FR.UTF-8" };

        SystemLanguage.Detect(name => environment.GetValueOrDefault(name)).Should().Be("fr");
    }

    [Fact]
    public void The_windows_locale_is_used_when_no_variable_is_set()
    {
        SystemLanguage.Detect(_ => null, () => "pl-PL").Should().Be("pl");
        SystemLanguage.Detect(_ => null, () => null).Should().BeNull();
    }

    [Fact]
    public void Detection_never_throws_on_this_machine()
    {
        var act = () => SystemLanguage.Detect();

        act.Should().NotThrow();
    }
}
