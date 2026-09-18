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

    private static NotSupportedException CreateException() =>
        new("Device administration requires PostgreSQL storage.");
}
