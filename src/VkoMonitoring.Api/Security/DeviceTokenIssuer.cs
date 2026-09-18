using System.Security.Cryptography;

namespace VkoMonitoring.Api.Security;

public static class DeviceTokenIssuer
{
    private const int TokenSizeBytes = 32;

    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenSizeBytes));
}
