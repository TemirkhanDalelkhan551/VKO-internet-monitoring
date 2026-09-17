using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public interface IDeviceRegistrationRepository
{
    Task RegisterAsync(
        DeviceRegistrationRequest request,
        Guid schoolId,
        Guid lineId,
        Guid deviceId,
        string deviceIdentifier,
        byte[] tokenHash,
        CancellationToken cancellationToken);
}
