using System.IO.Compression;
using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Infrastructure.Platform;
using Xunit;

namespace MaxCube.Infrastructure.Tests;

public sealed class AppLogTests
{
    private static readonly DateTimeOffset ExportTime =
        new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

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
        var log = new FileAppLog(folder.Path, new FixedTime(ExportTime));
        log.Write("INFO", "Protokoll exportiert");
        var target = Path.Combine(folder.Path, "export");

        var archive = log.Export(target);

        File.Exists(archive).Should().BeTrue();
        Path.GetFileName(archive).Should().StartWith("log-export-").And.EndWith(".zip");
        using var zip = ZipFile.OpenRead(archive);
        zip.Entries.Should().ContainSingle();
        zip.Entries[0].FullName.Should().StartWith("maxcube-").And.EndWith(".log");
    }

    [Fact]
    public void Export_filters_lines_by_minimum_level()
    {
        using var folder = new TempFolder();
        var log = new FileAppLog(folder.Path, new FixedTime(ExportTime));
        log.Write("INFO", "nur Info");
        log.Write("WARN", "auch Warnung");
        log.Write("ERROR", "auch Fehler");

        var archive = log.Export(Path.Combine(folder.Path, "export"),
            new LogExportOptions { MinLevel = LogExportLevel.Warn });

        var text = ReadExportedText(archive);
        text.Should().NotContain("nur Info");
        text.Should().Contain("auch Warnung").And.Contain("auch Fehler");
    }

    [Fact]
    public void Export_redacts_ip_serial_and_known_names_on_request()
    {
        using var folder = new TempFolder();
        var log = new FileAppLog(folder.Path, new FixedTime(ExportTime));
        log.Write("INFO", "Verbindung zu 192.168.1.24 mit JEQ47110815 im Raum Wohnzimmer");
        log.Write("WARN", "Gerät Küche antwortet von 10.0.0.7");

        var archive = log.Export(Path.Combine(folder.Path, "export"), new LogExportOptions
        {
            RedactPersonalData = true,
            KnownNames = new[] { "Wohnzimmer", "Küche" },
        });

        var text = ReadExportedText(archive);
        text.Should().Contain("[IP]").And.Contain("[SERIAL]").And.Contain("[NAME]");
        text.Should().NotContain("192.168.1.24").And.NotContain("10.0.0.7");
        text.Should().NotContain("JEQ47110815").And.NotContain("Wohnzimmer").And.NotContain("Küche");
    }

    [Fact]
    public void Export_without_matching_lines_returns_no_path_and_creates_no_archive()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Path);
        File.WriteAllText(Path.Combine(folder.Path, "maxcube-20261125.log"),
            "2026-11-25 12:00:00 +00:00 [DEBUG] vertraulich");
        var log = new FileAppLog(folder.Path, new FixedTime(ExportTime));
        var target = Path.Combine(folder.Path, "export");

        var archive = log.Export(target, new LogExportOptions { MinLevel = LogExportLevel.Error });

        archive.Should().BeEmpty();
        Directory.Exists(target).Should().BeTrue();
        Directory.GetFiles(target, "*.zip").Should().BeEmpty();
    }

    [Fact]
    public void Default_export_keeps_unredacted_lines_and_the_original_file_name()
    {
        using var folder = new TempFolder();
        var log = new FileAppLog(folder.Path, new FixedTime(ExportTime));
        log.Write("INFO", "IP 192.168.1.24, JEQ47110815, Wohnzimmer");

        var archive = log.Export(Path.Combine(folder.Path, "export"));

        var text = ReadExportedText(archive, "maxcube-20261125.log");
        text.Should().Contain("192.168.1.24").And.Contain("JEQ47110815").And.Contain("Wohnzimmer");
        text.Should().NotContain("[IP]").And.NotContain("[SERIAL]").And.NotContain("[NAME]");
    }

    [Fact]
    public void Null_log_store_supports_both_export_overloads()
    {
        var store = new NullLogStore();

        store.Export("irgendwo").Should().BeEmpty();
        store.Export("irgendwo", new LogExportOptions()).Should().BeEmpty();
    }

    private static string ReadExportedText(string archive, string? expectedEntryName = null)
    {
        File.Exists(archive).Should().BeTrue();
        using var zip = ZipFile.OpenRead(archive);
        zip.Entries.Should().ContainSingle();
        if (expectedEntryName is not null)
        {
            zip.Entries[0].FullName.Should().Be(expectedEntryName);
        }

        using var reader = new StreamReader(zip.Entries[0].Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Cleanup_removes_only_files_older_than_the_retention_window()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Path);
        var old = Path.Combine(folder.Path, "maxcube-20260101.log");
        var recent = Path.Combine(folder.Path, "maxcube-20261125.log");
        File.WriteAllText(old, "[INFO] alt");
        File.WriteAllText(recent, "[INFO] aktuell");
        var log = new FileAppLog(folder.Path, new FixedTime(ExportTime));

        var removed = log.Cleanup(7);

        removed.Should().Be(1);
        File.Exists(old).Should().BeFalse();
        File.Exists(recent).Should().BeTrue();
    }

    private sealed class FixedTime : TimeProvider
    {
        public FixedTime(DateTimeOffset now) => Now = now;

        private DateTimeOffset Now { get; }

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
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
