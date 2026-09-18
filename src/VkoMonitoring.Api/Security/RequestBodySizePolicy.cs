using Microsoft.AspNetCore.Http;

namespace VkoMonitoring.Api.Security;

public static class RequestBodySizePolicy
{
    public const long MaximumApiRequestBytes = 256 * 1024;

    public static long GetMaximumBytes(PathString requestPath, int maximumSpeedTestBytes) =>
        requestPath.StartsWithSegments("/speed/upload", StringComparison.OrdinalIgnoreCase)
            ? maximumSpeedTestBytes
            : MaximumApiRequestBytes;
}
