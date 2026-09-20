namespace VkoMonitoring.Api.Persistence;

public interface IDeviceAdministrationRepository
{
    Task<bool> SetBlockedAsync(
        Guid deviceId,
        bool isBlocked,
        CancellationToken cancellationToken);

    Task<bool> ReplaceTokenHashAsync(
        Guid deviceId,
        byte[] tokenHash,
        CancellationToken cancellationToken);

    Task<VkoMonitoring.Api.Models.DeviceLifecycleResult?> RebindAsync(
        Guid deviceId, Guid schoolId, Guid lineId, string reason, string actor,
        CancellationToken cancellationToken);

    Task<VkoMonitoring.Api.Models.DeviceLifecycleResult?> ReplaceAsync(
        Guid deviceId, Guid replacementDeviceId, string reason, string actor,
        CancellationToken cancellationToken);

    Task<VkoMonitoring.Api.Models.DeviceLifecycleResult?> DecommissionAsync(
        Guid deviceId, string reason, string actor,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VkoMonitoring.Api.Models.DeviceLifecycleEvent>> GetHistoryAsync(
        Guid deviceId, CancellationToken cancellationToken);
}
