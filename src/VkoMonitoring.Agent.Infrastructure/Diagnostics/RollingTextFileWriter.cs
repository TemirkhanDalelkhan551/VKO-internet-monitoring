using System.Text;

namespace VkoMonitoring.Agent.Infrastructure.Diagnostics;

public sealed class RollingTextFileWriter
{
    private const string FilePrefix = "agent-";
    private readonly Lock _sync = new();
    private readonly string _directory;
    private readonly long _maximumFileSizeBytes;
    private readonly int _retentionDays;
    private readonly TimeProvider _timeProvider;
    private DateOnly? _lastCleanupDate;

    public RollingTextFileWriter(
        string directory,
        long maximumFileSizeBytes,
        int retentionDays,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retentionDays);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _directory = directory;
        _maximumFileSizeBytes = maximumFileSizeBytes;
        _retentionDays = retentionDays;
        _timeProvider = timeProvider;
    }

    public void WriteLine(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            Directory.CreateDirectory(_directory);
            var now = _timeProvider.GetUtcNow();
            DeleteExpiredFilesOncePerDay(now);

            var line = message + Environment.NewLine;
            var requiredBytes = Encoding.UTF8.GetByteCount(line);
            var path = FindWritableFile(now, requiredBytes);
            File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private string FindWritableFile(DateTimeOffset now, int requiredBytes)
    {
        var date = now.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        for (var sequence = 0; ; sequence++)
        {
            var suffix = sequence == 0 ? string.Empty : $"-{sequence:D2}";
            var path = Path.Combine(_directory, $"{FilePrefix}{date}{suffix}.log");
            if (!File.Exists(path) || new FileInfo(path).Length + requiredBytes <= _maximumFileSizeBytes)
            {
                return path;
            }
        }
    }

    private void DeleteExpiredFilesOncePerDay(DateTimeOffset now)
    {
        var currentDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (_lastCleanupDate == currentDate)
        {
            return;
        }

        var deleteBefore = now.UtcDateTime.AddDays(-_retentionDays);
        foreach (var file in new DirectoryInfo(_directory).EnumerateFiles($"{FilePrefix}*.log"))
        {
            if (file.LastWriteTimeUtc < deleteBefore)
            {
                file.Delete();
            }
        }

        _lastCleanupDate = currentDate;
    }
}
