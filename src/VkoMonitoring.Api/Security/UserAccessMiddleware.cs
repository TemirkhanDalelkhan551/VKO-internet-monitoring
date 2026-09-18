using Microsoft.AspNetCore.RateLimiting;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Persistence;

namespace VkoMonitoring.Api.Security;

public sealed class UserAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MonitoringApiOptions options, TokenValidator tokens)
    {
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy is not ("admin-read" or "admin-write" or "user-login")) { await next(context); return; }
        var repository = context.RequestServices.GetService<PostgresUserRepository>();
        var login = context.Request.Path == "/api/auth/login";
        MonitoringRequestAccess? access = null;
        if (!login)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && repository is not null)
            {
                var user = await repository.AuthenticateAsync(authorization[7..], context.RequestAborted);
                if (user is not null) access = new MonitoringRequestAccess(user,
                    await repository.GetAllowedLinesAsync(user, context.RequestAborted), user.Login);
            }
            else if (options.EnableLegacyAdminToken && tokens.IsLegacyAdminAuthorized(context.Request.Headers["X-Admin-Token"].FirstOrDefault()))
                access = new MonitoringRequestAccess(null, null, "bootstrap-admin");
        }
        context.Items[MonitoringRequestAccess.ItemKey] = access;
        context.Response.Headers.CacheControl = "no-store";
        var needsAudit = true;
        long? auditId = null;
        if (needsAudit && repository is not null)
        {
            // Persist the attempt BEFORE mutation. A crash leaves an unfinished audit entry, not an invisible action.
            auditId = await repository.BeginAuditAsync(access?.User?.UserId, access?.Actor ?? "anonymous",
                login ? "Login" : context.Request.Method, context.Request.Path.Value ?? "/",
                context.Connection.RemoteIpAddress?.ToString(), context.RequestAborted);
        }
        var completed = false;
        try
        {
            if (!login)
            {
                if (access is null) { context.Response.StatusCode = 401; return; }
                if (access.User is { } user && !MonitoringRequestAccess.CanUse(user.Role, context.Request.Method, context.Request.Path.Value!))
                { context.Response.StatusCode = 403; return; }
                if (access.AllowedLineIds is not null && repository is not null)
                {
                    foreach (var (route, table) in new[] { ("deviceId", "devices"), ("incidentId", "incidents") })
                    {
                        if (Guid.TryParse(context.Request.RouteValues.GetValueOrDefault(route)?.ToString(), out var id))
                        {
                            var line = await repository.FindResourceLineAsync(table, id, context.RequestAborted);
                            if (line is null || !access.CanAccessLine(line.Value)) { context.Response.StatusCode = 404; return; }
                        }
                    }
                }
            }
            await next(context); completed = true;
        }
        finally
        {
            if (auditId is not null)
                await repository!.CompleteAuditAsync(auditId.Value,
                    completed || context.Response.StatusCode >= 400 ? context.Response.StatusCode : 500, CancellationToken.None,
                    MonitoringRequestAccess.From(context)?.User);
        }
    }
}
