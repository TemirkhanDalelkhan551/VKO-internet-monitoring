namespace VkoMonitoring.Api.Models;

public sealed record ActivationCodeCreateRequest(Guid SchoolId, Guid LineId, int? LifetimeMinutes);

public sealed record ActivationCodeCreateResult(string ActivationCode, DateTimeOffset ExpiresAtUtc);

public sealed record DeviceActivationRequest(
    string ActivationCode,
    string? DeviceIdentifier,
    string DeviceName,
    string? Room,
    string? ConnectionType);

public sealed record ActivatedDeviceBinding(
    Guid SchoolId,
    Guid LineId,
    Guid DeviceId,
    string DeviceIdentifier);

public sealed record DeviceActivationResult(
    Guid SchoolId,
    Guid LineId,
    Guid DeviceId,
    string DeviceIdentifier,
    string DeviceToken);
