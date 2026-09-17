namespace VkoMonitoring.Api.Security;

public sealed class ConfigurationDeviceAuthenticator(TokenValidator tokenValidator)
    : IDeviceAuthenticator
{
    public Task<bool> IsAuthorizedAsync(
        Guid schoolId,
        Guid deviceId,
        Guid lineId,
        string? suppliedToken,
        CancellationToken cancellationToken) =>
        Task.FromResult(tokenValidator.IsDeviceAuthorized(
            schoolId,
            deviceId,
            lineId,
            suppliedToken));
}
