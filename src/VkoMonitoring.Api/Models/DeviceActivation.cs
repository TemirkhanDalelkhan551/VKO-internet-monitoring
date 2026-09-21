namespace VkoMonitoring.Api.Models;

public sealed record ActivationCodeCreateRequest(Guid SchoolId, Guid LineId, int? LifetimeMinutes);

public sealed record ActivationCodeCreateResult(string ActivationCode, DateTimeOffset ExpiresAtUtc);

public sealed record ActivationCodeOverview(
    Guid ActivationCodeId,
    Guid SchoolId,
    string SchoolName,
    Guid LineId,
    string LineName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? UsedAtUtc,
    Guid? UsedByDeviceId,
    DateTimeOffset? RevokedAtUtc,
    string Status);

public sealed record ActivationCodePreviewRequest(string ActivationCode, string? DeviceIdentifier = null);

public sealed record ActivationCodePreviewResult(
    Guid SchoolId,
    string SchoolName,
    Guid LineId,
    string LineName,
    string? ProviderName,
    string? ConnectionType,
    DateTimeOffset ExpiresAtUtc,
    bool IsRecovery,
    bool IsReconfiguration = false);

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
    string DeviceIdentifier,
    bool Recovered,
    bool Reconfigured = false);

public sealed record DeviceActivationResult(
    Guid SchoolId,
    Guid LineId,
    Guid DeviceId,
    string DeviceIdentifier,
    string DeviceToken,
    bool Recovered,
    bool Reconfigured = false);
