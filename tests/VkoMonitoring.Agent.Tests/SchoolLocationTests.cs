using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Validation;
using VkoMonitoring.Api.Security;

namespace VkoMonitoring.Agent.Tests;

public sealed class SchoolLocationTests
{
    [Fact]
    public void MapImagesAllowOnlyConfiguredOriginWithoutAllowingExternalScriptsOrInlineStyles()
    {
        var policy = MapContentSecurityPolicy.Create("https://tile.openstreetmap.org/{z}/{x}/{y}.png");
        Assert.Contains("img-src 'self' data: https://tile.openstreetmap.org;", policy);
        Assert.Contains("script-src 'self'; style-src 'self';", policy);
        Assert.DoesNotContain("unsafe-inline", policy);
        Assert.DoesNotContain("{z}", policy);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com/tiles")]
    [InlineData("https://user:password@example.com/tiles")]
    public void UnsafeTileConfigurationIsRejected(string url) =>
        Assert.Throws<InvalidOperationException>(() => MapContentSecurityPolicy.Create(url));

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(49.97, 82.61, true)]
    [InlineData(0d, 0d, true)]
    [InlineData(-90d, -180d, true)]
    [InlineData(90d, 180d, true)]
    [InlineData(90.01, 82d, false)]
    [InlineData(49d, -180.01, false)]
    [InlineData(null, 82d, false)]
    [InlineData(49d, null, false)]
    [InlineData(double.NaN, 82d, false)]
    [InlineData(49d, double.PositiveInfinity, false)]
    public void CoordinatesMustBeAPairOfFiniteValuesInRange(double? latitude, double? longitude, bool valid)
    {
        Assert.Equal(valid, SchoolLocationValidator.IsValid(new SchoolLocationRequest(latitude, longitude)));
    }
}
