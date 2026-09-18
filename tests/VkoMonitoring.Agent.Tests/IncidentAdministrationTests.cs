using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Agent.Tests;

public sealed class IncidentAdministrationTests
{
    [Theory]
    [InlineData(IncidentStatus.New, IncidentStatus.SentToProvider)]
    [InlineData(IncidentStatus.SentToProvider, IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.SentToProvider, IncidentStatus.WaitingForInformation)]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.WaitingForInformation)]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.WaitingForInformation, IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.WaitingForInformation, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.InProgress)]
    public void StatusPolicy_AllowsSupportedWorkflowTransitions(
        IncidentStatus current,
        IncidentStatus requested)
    {
        Assert.True(IncidentStatusTransitionPolicy.CanTransition(current, requested));
    }

    [Theory]
    [InlineData(IncidentStatus.New, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.New, IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.SentToProvider, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.InProgress)]
    public void StatusPolicy_RejectsWorkflowSkips(
        IncidentStatus current,
        IncidentStatus requested)
    {
        Assert.False(IncidentStatusTransitionPolicy.CanTransition(current, requested));
    }

    [Fact]
    public void StatusPolicy_TreatsSameStatusAsIdempotent()
    {
        Assert.True(IncidentStatusTransitionPolicy.CanTransition(
            IncidentStatus.InProgress,
            IncidentStatus.InProgress));
    }

    [Fact]
    public void ManualIncidentValidator_AcceptsCompleteRequest()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new ManualIncidentCreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "HighPing",
            "Высокая задержка",
            "Ping устойчиво превышает допустимый порог.",
            now.AddMinutes(-10),
            "Ответственный специалист",
            "Администратор",
            "Создано после проверки.");

        var errors = IncidentAdministrationValidator.Validate(request, now);

        Assert.Empty(errors);
    }

    [Fact]
    public void ManualIncidentValidator_RejectsMissingFieldsAndFutureStart()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new ManualIncidentCreateRequest(
            Guid.Empty,
            Guid.Empty,
            " ",
            "",
            " ",
            now.AddHours(1),
            null,
            "",
            null);

        var errors = IncidentAdministrationValidator.Validate(request, now);

        Assert.Contains(nameof(request.SchoolId), errors.Keys);
        Assert.Contains(nameof(request.LineId), errors.Keys);
        Assert.Contains(nameof(request.ProblemType), errors.Keys);
        Assert.Contains(nameof(request.Title), errors.Keys);
        Assert.Contains(nameof(request.Description), errors.Keys);
        Assert.Contains(nameof(request.Actor), errors.Keys);
        Assert.Contains(nameof(request.StartedAtUtc), errors.Keys);
    }
}
