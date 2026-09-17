namespace VkoMonitoring.Api.Models;

public sealed record DeviceRegistrationRequest(
    Guid? SchoolId,
    string SchoolName,
    string? DistrictCity,
    string? Address,
    Guid? LineId,
    string LineName,
    string? ProviderName,
    string? ConnectionType,
    decimal? ContractedDownloadMbps,
    decimal? ContractedUploadMbps,
    string LineStatus,
    Guid? DeviceId,
    string? DeviceIdentifier,
    string DeviceName,
    string? Room);

public sealed record DeviceRegistrationResult(
    Guid SchoolId,
    Guid LineId,
    Guid DeviceId,
    string DeviceIdentifier,
    string DeviceToken);
