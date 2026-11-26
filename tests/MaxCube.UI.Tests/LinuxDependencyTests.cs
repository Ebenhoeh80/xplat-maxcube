using FluentAssertions;
using Xunit;

namespace MaxCube.UI.Tests;

/// <summary>
/// 0.6.2: Der Linux-Start darf nicht an undokumentierten Systembibliotheken scheitern.
/// Die Liste ist aus den DllImports von SkiaSharp und Avalonia.X11 abgeleitet.
/// </summary>
public class LinuxDependencyTests
{
    private static readonly string[] Libraries =
    {
        "libfontconfig.so.1", "libX11.so.6", "libXcursor.so.1", "libXext.so.6",
        "libXfixes.so.3", "libXi.so.6", "libXrandr.so.2", "libICE.so.6",
        "libSM.so.6", "libGL.so.1", "libvulkan.so.1",
    };

    private static readonly string[] Packages =
    {
        "libfontconfig1", "libx11-6", "libxcursor1", "libxext6", "libxfixes3",
        "libxi6", "libxrandr2", "libice6", "libsm6", "libgl1", "libvulkan1",
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MaxCube.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("MaxCube.slnx nicht gefunden.");
    }

    [Fact]
    public void App_and_start_script_check_the_same_libraries()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "MaxCube.UI.Desktop", "Program.cs"));
        var start = File.ReadAllText(Path.Combine(root, "start.sh"));

        foreach (var library in Libraries)
        {
            program.Should().Contain(library, $"{library} muss vom App-Startcheck geprüft werden");
            start.Should().Contain(library, $"{library} muss vom Startskript geprüft werden");
        }
    }

    [Fact]
    public void Every_required_package_is_documented_in_both_readmes()
    {
        var root = RepositoryRoot();
        var readmes = new[] { "README.md", "README.de.md" }
            .Select(name => File.ReadAllText(Path.Combine(root, name)));

        foreach (var readme in readmes)
        {
            foreach (var package in Packages)
            {
                readme.Should().Contain(package, $"{package} muss als Systemvoraussetzung dokumentiert sein");
            }
        }
    }
}
