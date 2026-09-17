using System.Security.Cryptography;
using System.Text;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class DailyMeasurementSchedule : IMeasurementSchedule
{
    private readonly Guid _deviceId;
    private readonly IReadOnlyList<DailyWindow> _windows;

    public DailyMeasurementSchedule(AgentOptions options)
    {
        _deviceId = options.DeviceId;
        _windows = options.MeasurementWindows.Select(DailyWindow.Parse).ToArray();
    }

    public DateTimeOffset GetNextRun(DateTimeOffset now)
    {
        for (var dayOffset = 0; dayOffset <= 1; dayOffset++)
        {
            var date = DateOnly.FromDateTime(now.Date.AddDays(dayOffset));
            for (var index = 0; index < _windows.Count; index++)
            {
                var candidate = CreateRunTime(date, _windows[index], index, now.Offset);
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
