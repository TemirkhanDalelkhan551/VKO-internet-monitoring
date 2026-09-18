using Npgsql;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Api.Security;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, MonitoringApiOptions options, CancellationToken ct) =>
        {
            var repository = context.RequestServices.GetService<PostgresUserRepository>();
            if (repository is null) return Results.Problem("User accounts require PostgreSQL.", statusCode:501);
            var result = await repository.LoginAsync(request, options.UserSessionLifetimeMinutes, ct);
            if (result is not null) context.Items[MonitoringRequestAccess.ItemKey] = new MonitoringRequestAccess(result.User, [], result.User.Login);
            context.Response.Headers.CacheControl = "no-store";
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireRateLimiting("user-login");

        app.MapGet("/api/auth/me", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = MonitoringRequestAccess.From(context)?.User;
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireRateLimiting("admin-read");

        app.MapPost("/api/auth/logout", async (HttpContext context, CancellationToken ct) =>
        {
            var repository = context.RequestServices.GetService<PostgresUserRepository>();
            var authorization = context.Request.Headers.Authorization.ToString();
            if (repository is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
            await repository.LogoutAsync(authorization[7..], ct); return Results.NoContent();
        }).RequireRateLimiting("admin-write");

        app.MapPost("/api/auth/password", async (PasswordChangeRequest request, HttpContext context, CancellationToken ct) =>
        {
            var user = MonitoringRequestAccess.From(context)?.User;
            if (user is null) return Results.Unauthorized();
            if (!UserRequestValidator.IsPasswordValid(request.NewPassword) || request.CurrentPassword is null || request.CurrentPassword.Length > 128)
                return Results.BadRequest(new { error = "Invalid password length." });
            var repository = context.RequestServices.GetRequiredService<PostgresUserRepository>();
            return await repository.SetPasswordAsync(user.UserId, request.NewPassword, request.CurrentPassword, ct)
                ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("admin-write");

        app.MapGet("/api/users", async (HttpContext context, CancellationToken ct) =>
        {
            var repository = context.RequestServices.GetService<PostgresUserRepository>();
            return repository is null ? Results.Problem("User accounts require PostgreSQL.", statusCode:501) : Results.Ok(await repository.ListAsync(ct));
        }).RequireRateLimiting("admin-read");

        app.MapPost("/api/users", async (UserCreateRequest request, HttpContext context, CancellationToken ct) =>
        {
            var errors = UserRequestValidator.Validate(request.Login ?? "", request.DisplayName, request.Role, request.SchoolId, request.DistrictCity, request.ProviderName, request.Password ?? "");
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var repository = context.RequestServices.GetService<PostgresUserRepository>();
            if (repository is null) return Results.Problem("User accounts require PostgreSQL.", statusCode:501);
            try { var user = await repository.CreateAsync(request, ct); return Results.Created($"/api/users/{user.UserId}", user); }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { return Results.Conflict(new { error = "Login already exists." }); }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation) { return Results.BadRequest(new { error = "School binding was not found." }); }
        }).RequireRateLimiting("admin-write");

        app.MapPut("/api/users/{userId:guid}", async (Guid userId, UserUpdateRequest request, HttpContext context, CancellationToken ct) =>
        {
            var errors = UserRequestValidator.Validate(null, request.DisplayName, request.Role, request.SchoolId, request.DistrictCity, request.ProviderName);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var repository = context.RequestServices.GetService<PostgresUserRepository>();
            if (repository is null) return Results.Problem("User accounts require PostgreSQL.", statusCode:501);
            try
            {
                return await repository.UpdateAsync(userId, request, ct) switch
                { "Updated" => Results.NoContent(), "NotFound" => Results.NotFound(), _ => Results.Conflict(new { error = "The last active administrator cannot be blocked or demoted." }) };
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation) { return Results.BadRequest(new { error = "School binding was not found." }); }
        }).RequireRateLimiting("admin-write");

        app.MapPost("/api/users/{userId:guid}/password", async (Guid userId, PasswordResetRequest request, HttpContext context, CancellationToken ct) =>
        {
            if (!UserRequestValidator.IsPasswordValid(request.NewPassword)) return Results.BadRequest(new { error = "Password must contain 12–128 characters." });
            var repository = context.RequestServices.GetService<PostgresUserRepository>();
            if (repository is null) return Results.Problem("User accounts require PostgreSQL.", statusCode:501);
            return await repository.SetPasswordAsync(userId, request.NewPassword, null, ct) ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("admin-write");

        app.MapGet("/api/audit", async (DateTimeOffset? from, DateTimeOffset? to, long? beforeId, int? limit, HttpContext context, TimeProvider clock, CancellationToken ct) =>
        {
            var repository = context.RequestServices.GetService<PostgresUserRepository>();
            if (repository is null) return Results.Problem("Audit requires PostgreSQL.", statusCode:501);
            var end = to ?? clock.GetUtcNow().AddMinutes(1); var start = from ?? end.AddDays(-30);
            if (start >= end) return Results.BadRequest(new { error = "Invalid period." });
            return Results.Ok(await repository.GetAuditAsync(start, end, beforeId, Math.Clamp(limit ?? 100,1,1000), ct));
        }).RequireRateLimiting("admin-read");
    }
}
