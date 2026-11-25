using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace MaxCube.UI.ViewModels;

/// <summary>
/// Gremium 0.5.0: „Über die App“ mit Autor, Lizenz und GitHub-Projekt. Der Link wird
/// ausschließlich nach einem Benutzerklick geöffnet; es gibt keinen Update-Check,
/// keine Telemetrie und keine externen Abhängigkeiten zur Laufzeit.
/// </summary>
public partial class MainWindowViewModel
{
    public const string AuthorName = "Ebenhoeh80";
    public const string LicenseName = "MIT";
    public const string ProjectUrl = "https://github.com/Ebenhoeh80/xplat-maxcube";

    /// <summary>Volltext der MIT-Lizenz, wie er auch im Repository liegt.</summary>
    public const string LicenseText = """
        MIT License

        Copyright (c) 2026 Daniel Ebenhöh

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    public string LicenseFullText => LicenseText;

    public string AboutVersionText => L("AboutVersion", AppVersion);

    public string AboutAuthorText => L("AboutAuthor", AuthorName);

    public string AboutLicenseTextLine => L("AboutLicense", LicenseName);

    public string AboutProjectText => L("AboutProject", ProjectUrl);

    public string AboutDisclaimerText => L("AboutDisclaimer");

    public string AboutPrivacyText => L("AboutPrivacy");

    /// <summary>Öffnet die Projektseite nur auf ausdrücklichen Benutzerklick.</summary>
    [RelayCommand]
    private void OpenProjectUrl()
    {
        var start = new ProcessStartInfo(ProjectUrl) { UseShellExecute = true };
        Process.Start(start);
    }
}
