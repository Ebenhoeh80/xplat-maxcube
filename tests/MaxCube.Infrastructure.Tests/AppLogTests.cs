using System.IO.Compression;
using FluentAssertions;
using MaxCube.Infrastructure.Platform;
using Xunit;

namespace MaxCube.Infrastructure.Tests;

public sealed class AppLogTests
{
    [Fact]
    public void Disabled_log_writes_nothing_and_enabled_log_writes_locally()
    {
        using var folder = new TempFolder();
        var log = new FileAppLog(folder.Path);

        log.IsEnabled = false;
        log.Write("INFO", "nicht sichtbar");
        log.ReadRecent(10).Should().BeEmpty();

        log.IsEnabled = true;
        log.Write("INFO", "Verbindung wiederhergestellt");
        var lines = log.ReadRecent(10);
        lines.Should().ContainSingle();
        lines[0].Should().Contain("[INFO] Verbindung wiederhergestellt");
    }

    [Fact]
    public void ReadRecent_returns_only_the_newest_entries_in_order()
    {
        using var folder = new TempFolder();
        var log = new FileAppLog(folder.Path);
        log.Write("INFO", "eins");
        log.Write("WARN", "zwei");
        log.Write("ERROR", "drei");

        var lines = log.ReadRecent(2);

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("zwei");
        lines[1].Should().Contain("drei");
    }

    [Fact]
    public void Lines_carry_the_thread_context_for_support()
    {
        using var folder = new TempFolder();
        var log = new FileAppLog(folder.Path);
        log.Write("INFO", "Test");
        log.Write("WARN", "Test");

        var lines = log.ReadRecent(2);
        lines.Should().OnlyContain(line => line.Contains("[INFO] Test (T") || line.Contains("[WARN] Test (T"));
        lines[0].Should().MatchRegex(@"\(T\d+(,UI)?\)$");
    }

    [Fact]
    public void Export_creates_a_zip_with_the_log_files_only_on_request()
    {
        using var folder = new TempFolder();
        var log = new FileAppLog(folder.Path);
        log.Write("INFO", "Protokoll exportiert");
        var target = Path.Combine(folder.Path, "export");

        var archive = log.Export(target);

        File.Exists(archive).Should().BeTrue();
        Path.GetFileName(archive).Should().StartWith("log-export-").And.EndWith(".zip");
        using var zip = ZipFile.OpenRead(archive);
        zip.Entries.Should().ContainSingle();
        zip.Entries[0].FullName.Should().StartWith("maxcube-").And.EndWith(".log");
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder() => Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "maxcube-logtest-" + Guid.NewGuid().ToString("N"));

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
