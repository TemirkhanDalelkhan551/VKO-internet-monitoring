using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Security;

public sealed record MonitoringRequestAccess(UserOverview? User, Guid[]? AllowedLineIds, string Actor)
{
    public const string ItemKey = "MonitoringAccess";
    public static MonitoringRequestAccess? From(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as MonitoringRequestAccess : null;
    public bool CanAccessLine(Guid lineId) => AllowedLineIds is null || AllowedLineIds.Contains(lineId);
    // Values are server-produced UUIDs, never request text. SQL predicates are applied before aggregation/limits.
    public string SqlCondition(string column) => AllowedLineIds is null ? "true" :
        AllowedLineIds.Length == 0 ? "false" :
        $"{column} IN ({string.Join(",", AllowedLineIds.Select(id => $"'{id:D}'::uuid"))})";

    public static bool CanUse(UserRole role, string method, string path)
    {
        path = path.TrimEnd('/').ToLowerInvariant();
        if (path.StartsWith("/api/auth/", StringComparison.Ordinal)) return true;
        if (path.StartsWith("/api/users", StringComparison.Ordinal) || path.StartsWith("/api/audit", StringComparison.Ordinal))
            return role == UserRole.Administrator;
        if (HttpMethods.IsGet(method)) return true;
        if (role is UserRole.Administrator or UserRole.Regional) return true;
        if (role == UserRole.School) return HttpMethods.IsPost(method) && path == "/api/incidents";
        return role == UserRole.Provider && path.StartsWith("/api/incidents/", StringComparison.Ordinal) &&
            ((HttpMethods.IsPut(method) && path.EndsWith("/status", StringComparison.Ordinal)) ||
             (HttpMethods.IsPost(method) && path.EndsWith("/comments", StringComparison.Ordinal)));
    }
}

public sealed class MonitoringAccessContext(IHttpContextAccessor accessor)
{
    public MonitoringRequestAccess? Current => accessor.HttpContext is { } context ? MonitoringRequestAccess.From(context) : null;
    public string SqlCondition(string column) => Current?.SqlCondition(column) ?? "true";
}
