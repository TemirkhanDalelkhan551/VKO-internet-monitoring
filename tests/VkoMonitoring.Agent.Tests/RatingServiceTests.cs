using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class RatingServiceTests
{
    [Fact]
    public void BuildItem_UsesVisiblePenaltyWeightsAndDoesNotHideIncidents()
    {
        var schoolId = Guid.NewGuid();
        var row = new MeasurementReportRow(schoolId, "Школа", Guid.NewGuid(), "Точка", null,
            DateTimeOffset.UtcNow, 20, 20, 100, 30, 2, "Online", false);
        var thresholds = new OperationalSettings(["00:00-23:59"], 20, 20, 100, 30, 2, 99);

        var rating = RatingService.BuildItem(schoolId, "Школа", null, null, [row], 1,
            MeasurementFreshness.Fresh, thresholds);

        Assert.Equal(98, rating.Score);
        Assert.Equal("Стабильно", rating.Category);
        Assert.Contains(rating.Metrics, metric => metric.Name == "Инциденты" && metric.Penalty == 2);
        Assert.All(rating.Metrics.Where(metric => metric.Name != "Инциденты"), metric => Assert.Equal(0, metric.Penalty));
    }

    [Fact]
    public void BuildItem_MarksObjectWithoutMeasurementsAsNoData()
    {
        var rating = RatingService.BuildItem(Guid.NewGuid(), "Школа", null, null, [], 0,
            MeasurementFreshness.Missing, new OperationalSettings([], 20, 20, 100, 30, 2, 99));

        Assert.Null(rating.Score);
        Assert.Equal("Нет данных", rating.Category);
    }
}
