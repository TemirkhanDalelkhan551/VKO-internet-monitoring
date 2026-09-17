using System.Globalization;

namespace VkoMonitoring.Agent.Core.Configuration;

public sealed record DailyWindow(TimeOnly Start, TimeOnly End)
{
    public static DailyWindow Parse(string value)
    {
        var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !TimeOnly.TryParseExact(parts[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(parts[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            end <= start)
        {
            throw new FormatException($"Measurement window '{value}' must use HH:mm-HH:mm and end after start.");
        }

        return new DailyWindow(start, end);
    }
}
