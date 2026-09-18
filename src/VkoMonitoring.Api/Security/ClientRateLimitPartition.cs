using System.Net;
using Microsoft.AspNetCore.Http;

namespace VkoMonitoring.Api.Security;

public static class ClientRateLimitPartition
{
    public static string GetKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Connection.RemoteIpAddress?
            .MapToIPv4()
            .ToString()
            ?? IPAddress.None.ToString();
    }
}
