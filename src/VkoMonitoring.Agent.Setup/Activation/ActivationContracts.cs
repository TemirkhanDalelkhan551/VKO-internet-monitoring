namespace VkoMonitoring.Agent.Setup.Activation;

public sealed record AgentActivationRequest(
    string ActivationCode,
    string DeviceIdentifier,
    string DeviceName,
    string? Room,
    string? ConnectionType);

public sealed record AgentActivationResult(
    Guid SchoolId,
    Guid LineId,
    Guid DeviceId,
    string DeviceIdentifier,
    string DeviceToken);
