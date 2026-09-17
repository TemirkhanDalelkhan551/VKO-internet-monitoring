using System.Security.Cryptography;
using System.Text;
using VkoMonitoring.Api.Configuration;

namespace VkoMonitoring.Api.Security;

public sealed class TokenValidator(MonitoringApiOptions options)
{
    public bool IsDeviceAuthorized(
        Guid schoolId,
        Guid deviceId,
        Guid lineId,
        string? suppliedToken)
    {
        var deviceKey = deviceId.ToString("D");
        return options.DeviceTokens.TryGetValue(deviceKey, out var expectedToken) &&
               options.DeviceBindings.TryGetValue(deviceKey, out var binding) &&
               binding.SchoolId == schoolId &&
               binding.LineId == lineId &&
               EqualsInConstantTime(expectedToken, suppliedToken);
    }

    public bool IsAdminAuthorized(string? suppliedToken) =>
        EqualsInConstantTime(options.AdminToken, suppliedToken);

    private static bool EqualsInConstantTime(string expected, string? supplied)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
