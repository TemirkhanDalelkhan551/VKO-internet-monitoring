using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class UnsupportedDeviceRegistrationRepository : IDeviceRegistrationRepository
{
    public Task RegisterAsync(
        DeviceRegistrationRequest request,
        Guid schoolId,
        Guid lineId,
        Guid deviceId,
        string deviceIdentifier,
        byte[] tokenHash,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Device registration requires PostgreSQL storage.");
}
