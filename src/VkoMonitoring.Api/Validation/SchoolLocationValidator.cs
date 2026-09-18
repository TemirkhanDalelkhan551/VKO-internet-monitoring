using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Validation;

public static class SchoolLocationValidator
{
    public static bool IsValid(SchoolLocationRequest location) =>
        location.Latitude is null && location.Longitude is null ||
        location.Latitude is { } latitude && double.IsFinite(latitude) && latitude is >= -90 and <= 90 &&
        location.Longitude is { } longitude && double.IsFinite(longitude) && longitude is >= -180 and <= 180;
}
