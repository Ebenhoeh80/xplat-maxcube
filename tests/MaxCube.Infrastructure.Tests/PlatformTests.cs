using FluentAssertions;
using MaxCube.Infrastructure.Platform;
using Xunit;

namespace MaxCube.Infrastructure.Tests;

/// <summary>
/// Gremium Runde 5: Diese Tests laufen in der CI unter Windows UND Linux und belegen, dass
/// Datenpfade, Instanzschutz und Log-Aufbewahrung auf beiden Systemen funktionieren.
/// </summary>
public class PlatformTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "maxcube-platform-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist optional.
        }
    }

    [Fact]
    public void Paths_follow_the_override_and_create_their_directories()
    {
        var paths = new AppPaths(_root).EnsureCreated();

        paths.Root.Should().Be(Path.GetFullPath(_root));
        paths.Database.Should().Be(Path.Combine(paths.Root, "maxcube.db"));
        Directory.Exists(paths.Logs).Should().BeTrue();
        Directory.Exists(paths.Reports).Should().BeTrue();
    }

    [Fact]
    public void The_default_path_lives_in_the_per_user_data_folder()
    {
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
        try
        {
            var paths = new AppPaths();

            Path.GetFileName(paths.Root).Should().Be("MaxCube");
            Path.IsPathRooted(paths.Root).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, previous);
        }
    }

    [Fact]
    public void A_second_instance_is_refused_until_the_first_one_ends()
    {
        var path = new AppPaths(_root).LockFile;

        using (var first = InstanceLock.TryAcquire(path))
        {
            first.Should().NotBeNull();
            InstanceLock.TryAcquire(path).Should().BeNull("der Cube erlaubt nur eine Sitzung");
        }

        using var again = InstanceLock.TryAcquire(path);
        again.Should().NotBeNull("die Sperre verschwindet mit dem Ende der ersten Instanz");
    }

    [Fact]
    public void Disposing_the_lock_twice_is_harmless_and_removes_the_file()
    {
        var path = new AppPaths(_root).LockFile;
        var instance = InstanceLock.TryAcquire(path);

        instance!.Dispose();
        instance.Dispose();

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void The_log_writes_one_file_per_day_and_never_throws()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
        var log = new FileAppLog(Path.Combine(_root, "logs"), time);

        log.Write("INFO", "erste Zeile\nzweite Zeile");
        time.Now = time.Now.AddDays(1);
        log.Write("WARN", "nächster Tag");

        var files = Directory.GetFiles(Path.Combine(_root, "logs")).Select(Path.GetFileName).OrderBy(name => name).ToArray();
        files.Should().Equal("maxcube-20261124.log", "maxcube-20261125.log");
        File.ReadAllText(Path.Combine(_root, "logs", "maxcube-20261124.log")).Should().Contain("[INFO] erste Zeile zweite Zeile");
    }

    [Fact]
    public void Cleanup_removes_only_logs_older_than_the_retention_period()
    {
        var logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        foreach (var name in new[] { "maxcube-20261001.log", "maxcube-20261110.log", "maxcube-20261124.log", "fremd.txt", "maxcube-kaputt.log" })
        {
            File.WriteAllText(Path.Combine(logs, name), "x");
        }

        var log = new FileAppLog(logs, new FixedTime(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)));

        var removed = log.Cleanup(retentionDays: 30);

        removed.Should().Be(1);
        Directory.GetFiles(logs).Select(Path.GetFileName).Should().BeEquivalentTo(
            "maxcube-20261110.log", "maxcube-20261124.log", "fremd.txt", "maxcube-kaputt.log");
        log.Cleanup(0).Should().Be(0, "ohne gültige Aufbewahrung wird nichts gelöscht");
    }

    private sealed class FixedTime : TimeProvider
    {
        public FixedTime(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
