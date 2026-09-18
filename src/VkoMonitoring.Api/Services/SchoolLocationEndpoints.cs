using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Api.Services;

public static class SchoolLocationEndpoints
{
    public static void MapSchoolLocationEndpoints(this WebApplication app)
    {
        app.MapPut("/api/schools/{schoolId:guid}/location", async (
            Guid schoolId, SchoolLocationRequest location, IMonitoringReadRepository repository,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!SchoolLocationValidator.IsValid(location))
                return Results.BadRequest(new { error = "Provide both coordinates within latitude [-90,90] and longitude [-180,180], or clear both." });
            var locations = context.RequestServices.GetService<PostgresSchoolLocationRepository>();
            if (locations is null)
                return Results.Problem("School coordinates require PostgreSQL.", statusCode: 501);
            if (await repository.GetSchoolAsync(schoolId, cancellationToken) is null)
                return Results.NotFound();
            return await locations.UpdateAsync(schoolId, location, cancellationToken) ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("admin-write");
    }
}
