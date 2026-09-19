using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class UnsupportedDeviceActivationRepository : IDeviceActivationRepository
{
    public Task<bool> CreateCodeAsync(
        Guid activationCodeId,
        Guid schoolId,
        Guid lineId,
        byte[] codeHash,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Device activation requires PostgreSQL storage.");

    public Task<IReadOnlyList<ActivationCodeOverview>> ListCodesAsync(
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Device activation requires PostgreSQL storage.");

    public Task<bool> RevokeCodeAsync(Guid activationCodeId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Device activation requires PostgreSQL storage.");

    public Task<ActivationCodePreviewResult?> PreviewAsync(
        byte[] codeHash,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Device activation requires PostgreSQL storage.");

    public Task<ActivatedDeviceBinding?> ActivateAsync(
        DeviceActivationRequest request,
        Guid deviceId,
        string deviceIdentifier,
        byte[] codeHash,
        byte[] deviceTokenHash,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Device activation requires PostgreSQL storage.");
}
