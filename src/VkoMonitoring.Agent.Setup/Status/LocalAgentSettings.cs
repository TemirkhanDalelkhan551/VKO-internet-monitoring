namespace VkoMonitoring.Agent.Setup.Status;

public sealed record LocalAgentSettings(
    Guid SchoolId,
    Guid DeviceId,
    Guid LineId,
    Uri ApiBaseUri,
    string DeviceToken)
{
    public string[] MeasurementWindows { get; init; } = [];
}
