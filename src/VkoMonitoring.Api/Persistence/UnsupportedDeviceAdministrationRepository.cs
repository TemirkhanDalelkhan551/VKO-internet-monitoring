namespace VkoMonitoring.Api.Persistence;

public sealed class UnsupportedDeviceAdministrationRepository : IDeviceAdministrationRepository
{
    public Task<bool> SetBlockedAsync(
        Guid deviceId,
        bool isBlocked,
        CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<bool> ReplaceTokenHashAsync(
        Guid deviceId,
        byte[] tokenHash,
        CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<VkoMonitoring.Api.Models.DeviceLifecycleResult?> RebindAsync(
        Guid deviceId, Guid schoolId, Guid lineId, string reason, string actor, CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<VkoMonitoring.Api.Models.DeviceLifecycleResult?> ReplaceAsync(
        Guid deviceId, Guid replacementDeviceId, string reason, string actor, CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<VkoMonitoring.Api.Models.DeviceLifecycleResult?> DecommissionAsync(
        Guid deviceId, string reason, string actor, CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<IReadOnlyList<VkoMonitoring.Api.Models.DeviceLifecycleEvent>> GetHistoryAsync(
        Guid deviceId, CancellationToken cancellationToken) => throw CreateException();

    private static NotSupportedException CreateException() =>
        new("Device administration requires PostgreSQL storage.");
}
