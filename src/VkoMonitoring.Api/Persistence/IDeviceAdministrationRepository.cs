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
}
