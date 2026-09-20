using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public interface IDeviceActivationRepository
{
    Task<bool> CreateCodeAsync(
        Guid activationCodeId,
        Guid schoolId,
        Guid lineId,
        byte[] codeHash,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivationCodeOverview>> ListCodesAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<bool> RevokeCodeAsync(Guid activationCodeId, CancellationToken cancellationToken);

    Task<ActivationCodePreviewResult?> PreviewAsync(
        byte[] codeHash,
        string? deviceIdentifier,
        CancellationToken cancellationToken);

    Task<ActivatedDeviceBinding?> ActivateAsync(
        DeviceActivationRequest request,
        Guid deviceId,
        string deviceIdentifier,
        byte[] codeHash,
        byte[] deviceTokenHash,
        CancellationToken cancellationToken);
}
