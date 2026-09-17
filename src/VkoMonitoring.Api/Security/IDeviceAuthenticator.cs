namespace VkoMonitoring.Api.Security;

public interface IDeviceAuthenticator
{
    Task<bool> IsAuthorizedAsync(
        Guid schoolId,
        Guid deviceId,
        Guid lineId,
        string? suppliedToken,
        CancellationToken cancellationToken);
}
