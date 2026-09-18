using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Api.Services;

public static class DirectoryEndpoints
{
    public static void MapDirectoryEndpoints(this WebApplication app)
    {
        app.MapPost("/api/schools", async (SchoolSaveRequest request, HttpContext context, CancellationToken ct) =>
        {
            var errors = DirectoryRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var repository = context.RequestServices.GetService<PostgresDirectoryRepository>();
            if (repository is null) return Unsupported();
            var id = Guid.NewGuid();
            await repository.SaveSchoolAsync(id, request, true, ct);
            return Results.Created($"/api/schools/{id}", new { schoolId = id });
        }).RequireRateLimiting("admin-write");

        app.MapPut("/api/schools/{schoolId:guid}", async (Guid schoolId, SchoolSaveRequest request,
            IMonitoringReadRepository read, HttpContext context, CancellationToken ct) =>
        {
            var errors = DirectoryRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var repository = context.RequestServices.GetService<PostgresDirectoryRepository>();
            if (repository is null) return Unsupported();
            if (await read.GetSchoolAsync(schoolId, ct) is null) return Results.NotFound();
            return await repository.SaveSchoolAsync(schoolId, request, false, ct) ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("admin-write");

        app.MapPost("/api/schools/{schoolId:guid}/lines", async (Guid schoolId, LineSaveRequest request,
            IMonitoringReadRepository read, HttpContext context, CancellationToken ct) =>
        {
            var errors = DirectoryRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var repository = context.RequestServices.GetService<PostgresDirectoryRepository>();
            if (repository is null) return Unsupported();
            if (await read.GetSchoolAsync(schoolId, ct) is null) return Results.NotFound();
            var id = Guid.NewGuid();
            return await repository.SaveLineAsync(schoolId, id, request, true, ct)
                ? Results.Created($"/api/schools/{schoolId}", new { lineId = id }) : Results.NotFound();
        }).RequireRateLimiting("admin-write");

        app.MapPut("/api/schools/{schoolId:guid}/lines/{lineId:guid}", async (Guid schoolId, Guid lineId,
            LineSaveRequest request, IMonitoringReadRepository read, HttpContext context, CancellationToken ct) =>
        {
            var errors = DirectoryRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var repository = context.RequestServices.GetService<PostgresDirectoryRepository>();
            if (repository is null) return Unsupported();
            var school = await read.GetSchoolAsync(schoolId, ct);
            if (school is null || !school.Lines.Any(line => line.LineId == lineId)) return Results.NotFound();
            return await repository.SaveLineAsync(schoolId, lineId, request, false, ct) ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("admin-write");
    }

    private static IResult Unsupported() => Results.Problem("School and line administration requires PostgreSQL.", statusCode: 501);
}
