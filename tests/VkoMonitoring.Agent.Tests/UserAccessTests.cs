using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Security;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Agent.Tests;

public sealed class UserAccessTests
{
    public static IEnumerable<object[]> Permissions()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            yield return [role, "GET", "/api/schools", true];
            yield return [role, "GET", "/api/audit", role == UserRole.Administrator];
            yield return [role, "POST", "/api/users", role == UserRole.Administrator];
            yield return [role, "POST", "/api/devices/register", role is UserRole.Administrator or UserRole.Regional];
            yield return [role, "POST", "/api/incidents", role is UserRole.Administrator or UserRole.Regional or UserRole.School];
            yield return [role, "PUT", "/api/incidents/id/status", role is UserRole.Administrator or UserRole.Regional or UserRole.Provider];
            yield return [role, "PUT", "/api/incidents/id/assignment", role is UserRole.Administrator or UserRole.Regional];
            yield return [role, "POST", "/api/auth/password", true];
        }
    }

    [Theory, MemberData(nameof(Permissions))]
    public void RolePermissionMatrix(UserRole role, string method, string path, bool allowed) =>
        Assert.Equal(allowed, MonitoringRequestAccess.CanUse(role, method, path));

    [Fact]
    public void EmptyScopeDeniesEveryLine()
    {
        var access = new MonitoringRequestAccess(null, [], "test");
        Assert.False(access.CanAccessLine(Guid.NewGuid()));
        Assert.Equal("false", access.SqlCondition("line_id"));
    }

    [Theory]
    [InlineData("/API/USERS")]
    [InlineData("/api/Users/")]
    [InlineData("/API/AUDIT")]
    public void RouteCaseAndTrailingSlashCannotBypassAdministratorRestriction(string path) =>
        Assert.False(MonitoringRequestAccess.CanUse(UserRole.Regional, "GET", path));

    [Fact]
    public void ScopeContainsOnlyGrantedLines()
    {
        var line = Guid.NewGuid(); var access = new MonitoringRequestAccess(null, [line], "test");
        Assert.True(access.CanAccessLine(line)); Assert.False(access.CanAccessLine(Guid.NewGuid()));
        Assert.Contains(line.ToString("D"), access.SqlCondition("line_id"));
    }

    [Theory]
    [InlineData(UserRole.School)]
    [InlineData(UserRole.District)]
    [InlineData(UserRole.Provider)]
    public void ScopedRoleCannotBeCreatedWithoutBinding(UserRole role) =>
        Assert.NotEmpty(UserRequestValidator.Validate("valid-user", "User", role, null, null, null, "long-password-123"));

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void PasswordLengthBounds(int length, bool valid) =>
        Assert.Equal(valid, UserRequestValidator.IsPasswordValid(new string('x', length)));

    [Theory]
    [InlineData("a")]
    [InlineData("bad login")]
    [InlineData("<script>")]
    public void LoginRejectsUnsafeOrShortIdentifiers(string login) =>
        Assert.Contains("login", UserRequestValidator.Validate(login, "User", UserRole.Administrator, null, null, null));
}
