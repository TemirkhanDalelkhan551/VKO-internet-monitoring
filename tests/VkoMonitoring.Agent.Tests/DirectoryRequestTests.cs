using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Agent.Tests;

public sealed class DirectoryRequestTests
{
    private static SchoolSaveRequest School(string name = "Школа", string? email = null) => new(name, null, null, null, null, null, email);
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Школа\n2")]
    public void InvalidSchoolNamesAreRejected(string name) => Assert.Contains("name", DirectoryRequestValidator.Validate(School(name)));
    [Fact]
    public void SchoolNameLengthIsBounded() => Assert.Contains("name", DirectoryRequestValidator.Validate(School(new string('x', 201))));
    [Theory]
    [InlineData("person@example.com", true)]
    [InlineData(null, true)]
    [InlineData("bad email", false)]
    [InlineData("Person <person@example.com>", false)]
    public void ContactEmailMustBeAnAddress(string? email, bool valid) => Assert.Equal(valid, DirectoryRequestValidator.Validate(School(email: email)).Count == 0);
    [Theory]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("0.001", true)]
    [InlineData("1.0001", false)]
    [InlineData("999999999.999", true)]
    [InlineData("1000000000", false)]
    public void SpeedsFitDatabasePrecision(string speed, bool valid)
    {
        var value = decimal.Parse(speed, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(valid, DirectoryRequestValidator.Validate(new LineSaveRequest("Линия", null, null, value, value, null, null, "Primary")).Count == 0);
    }
    [Theory]
    [InlineData("Primary", true)]
    [InlineData("Backup", true)]
    [InlineData("Disabled", true)]
    [InlineData("Unknown", false)]
    public void OptionalContractsAndKnownLineStatusesAreAccepted(string status, bool valid) =>
        Assert.Equal(valid, DirectoryRequestValidator.Validate(new LineSaveRequest("Линия", null, null, null, null, null, null, status)).Count == 0);
}
