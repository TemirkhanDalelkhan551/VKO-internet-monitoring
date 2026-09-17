using VkoMonitoring.Agent.Infrastructure.Diagnostics;

namespace VkoMonitoring.Agent.Tests;

public sealed class RollingTextFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"vko-log-tests-{Guid.NewGuid():N}");

    [Fact]
    public void WriteLine_CreatesUtf8LogAndRotatesBySize()
    {
        var writer = new RollingTextFileWriter(
            _directory,
            maximumFileSizeBytes: 20,
            retentionDays: 14,
            TimeProvider.System);

        writer.WriteLine("first diagnostic line");
        writer.WriteLine("second diagnostic line");

        var files = Directory.GetFiles(_directory, "agent-*.log");
        var contents = files.Select(File.ReadAllText).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Contains(contents, content => content.Contains("first diagnostic line", StringComparison.Ordinal));
        Assert.Contains(contents, content => content.Contains("second diagnostic line", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteLine_DeletesExpiredLogFiles()
    {
        Directory.CreateDirectory(_directory);
        var expiredPath = Path.Combine(_directory, "agent-20000101.log");
        File.WriteAllText(expiredPath, "expired");
        File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-30));
        var writer = new RollingTextFileWriter(
            _directory,
            maximumFileSizeBytes: 1_000,
            retentionDays: 14,
            TimeProvider.System);

        writer.WriteLine("current");

        Assert.False(File.Exists(expiredPath));
        Assert.Single(Directory.GetFiles(_directory, "agent-*.log"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
