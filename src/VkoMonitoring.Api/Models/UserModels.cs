using System.Text.Json.Serialization;

namespace VkoMonitoring.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole { School, District, Regional, Provider, Administrator }

public sealed record UserOverview(Guid UserId, string Login, string DisplayName, UserRole Role,
    Guid? SchoolId, string? DistrictCity, string? ProviderName, bool IsBlocked);
public sealed record UserCreateRequest(string Login, string DisplayName, string Password, UserRole Role,
    Guid? SchoolId, string? DistrictCity, string? ProviderName);
public sealed record UserUpdateRequest(string DisplayName, UserRole Role, Guid? SchoolId,
    string? DistrictCity, string? ProviderName, bool IsBlocked);
public sealed record LoginRequest(string Login, string Password);
public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);
public sealed record PasswordResetRequest(string NewPassword);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserOverview User);
public sealed record AuditEntry(long AuditId, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    Guid? UserId, string Actor, string Action, string Path, int? StatusCode, string? ClientIp);
