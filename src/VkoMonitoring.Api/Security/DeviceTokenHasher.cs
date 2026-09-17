using System.Security.Cryptography;
using System.Text;

namespace VkoMonitoring.Api.Security;

public static class DeviceTokenHasher
{
    public static byte[] Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
