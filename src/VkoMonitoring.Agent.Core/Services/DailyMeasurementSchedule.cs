using System.Security.Cryptography;
using System.Text;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class DailyMeasurementSchedule : IMeasurementSchedule
{
    private readonly Guid _deviceId;
    private IReadOnlyList<DailyWindow> _windows;

    public DailyMeasurementSchedule(AgentOptions options)
    {
        _deviceId = options.DeviceId;
        _windows = options.MeasurementWindows.Select(DailyWindow.Parse).ToArray();
    }

    public bool TryUpdateWindows(IEnumerable<string> windows)
    {
        try
        {
            var parsed = windows.Select(DailyWindow.Parse).ToArray();
            if (parsed.Length == 0) return false;
            Volatile.Write(ref _windows, parsed);
            return true;
        }
        catch (FormatException) { return false; }
    }

    public DateTimeOffset GetNextRun(DateTimeOffset now)
    {
        for (var dayOffset = 0; dayOffset <= 1; dayOffset++)
        {
            var date = DateOnly.FromDateTime(now.Date.AddDays(dayOffset));
            var windows = Volatile.Read(ref _windows);
            for (var index = 0; index < windows.Count; index++)
            {
                var candidate = CreateRunTime(date, windows[index], index, now.Offset);
                if (candidate > now)
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("Unable to calculate the next measurement time.");
    }

    private DateTimeOffset CreateRunTime(DateOnly date, DailyWindow window, int windowIndex, TimeSpan offset)
    {
        var start = date.ToDateTime(window.Start);
        var durationSeconds = (int)(window.End - window.Start).TotalSeconds;
        var seed = Encoding.UTF8.GetBytes($"{_deviceId:N}:{date:yyyy-MM-dd}:{windowIndex}");
        var hash = SHA256.HashData(seed);
        var randomSeconds = BitConverter.ToUInt32(hash, 0) % (uint)durationSeconds;
        return new DateTimeOffset(start.AddSeconds(randomSeconds), offset);
    }
}
