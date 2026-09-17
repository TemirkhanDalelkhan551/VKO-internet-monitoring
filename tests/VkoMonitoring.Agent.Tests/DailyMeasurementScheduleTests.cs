using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class DailyMeasurementScheduleTests
{
    [Fact]
    public void GetNextRun_ReturnsTimeInsideNextWindow()
    {
        var options = CreateOptions();
        var schedule = new DailyMeasurementSchedule(options);
        var now = new DateTimeOffset(2026, 9, 15, 7, 30, 0, TimeSpan.FromHours(5));

        var result = schedule.GetNextRun(now);

        Assert.Equal(now.Date, result.Date);
        Assert.InRange(result.TimeOfDay, TimeSpan.FromHours(8), TimeSpan.FromHours(9));
    }

    [Fact]
    public void GetNextRun_IsStableForSameDeviceAndDate()
    {
        var schedule = new DailyMeasurementSchedule(CreateOptions());
        var now = new DateTimeOffset(2026, 9, 15, 7, 30, 0, TimeSpan.FromHours(5));

        var first = schedule.GetNextRun(now);
        var second = schedule.GetNextRun(now);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetNextRun_MovesToTomorrowAfterLastWindow()
    {
        var schedule = new DailyMeasurementSchedule(CreateOptions());
        var now = new DateTimeOffset(2026, 9, 15, 22, 0, 0, TimeSpan.FromHours(5));

        var result = schedule.GetNextRun(now);

        Assert.Equal(new DateTime(2026, 9, 16), result.Date);
        Assert.InRange(result.TimeOfDay, TimeSpan.FromHours(8), TimeSpan.FromHours(9));
    }

    private static AgentOptions CreateOptions() => new()
    {
        DeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        MeasurementWindows = ["08:00-09:00", "12:00-13:00", "16:00-17:00", "20:00-21:00"]
    };
}
