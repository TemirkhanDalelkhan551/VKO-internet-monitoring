using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Services;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Tests;

public sealed class IncidentDetectionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SingleProblemMeasurement_DoesNotOpenIncident()
    {
        var signals = new[] { CreateSignal(0, isProblem: true) };

        var transition = IncidentDetectionPolicy.DetermineTransition(
            signals,
            hasOpenIncident: false,
            new IncidentDetectionOptions());

        Assert.Equal(IncidentTransition.None, transition);
    }

    [Fact]
    public void ConsecutiveProblemMeasurements_OpenIncident()
    {
        var signals = new[]
        {
            CreateSignal(0, isProblem: true),
            CreateSignal(-10, isProblem: true),
            CreateSignal(-20, isProblem: false)
        };

        var transition = IncidentDetectionPolicy.DetermineTransition(
            signals,
            hasOpenIncident: false,
            new IncidentDetectionOptions { ConsecutiveProblemMeasurements = 2 });

        Assert.Equal(IncidentTransition.Open, transition);
    }

    [Fact]
    public void HealthyMeasurement_BreaksProblemSequence()
    {
        var signals = new[]
        {
            CreateSignal(0, isProblem: true),
            CreateSignal(-10, isProblem: false),
            CreateSignal(-20, isProblem: true)
        };

        var transition = IncidentDetectionPolicy.DetermineTransition(
            signals,
            hasOpenIncident: false,
            new IncidentDetectionOptions { ConsecutiveProblemMeasurements = 2 });

        Assert.Equal(IncidentTransition.None, transition);
    }

    [Fact]
    public void ConfiguredViolationDuration_OpensIncident()
    {
        var signals = new[]
        {
            CreateSignal(0, isProblem: true),
            CreateSignal(-20, isProblem: true)
        };

        var transition = IncidentDetectionPolicy.DetermineTransition(
            signals,
            hasOpenIncident: false,
            new IncidentDetectionOptions
            {
                ConsecutiveProblemMeasurements = 5,
                MinimumViolationMinutes = 15
            });

        Assert.Equal(IncidentTransition.Open, transition);
    }

    [Fact]
    public void OpenIncident_RequiresConsecutiveRecoveryMeasurements()
    {
        var oneHealthy = new[]
        {
            CreateSignal(0, isProblem: false),
            CreateSignal(-10, isProblem: true)
        };
        var twoHealthy = new[]
        {
            CreateSignal(0, isProblem: false),
            CreateSignal(-10, isProblem: false),
            CreateSignal(-20, isProblem: true)
        };
        var options = new IncidentDetectionOptions { ConsecutiveRecoveryMeasurements = 2 };

        Assert.Equal(
            IncidentTransition.None,
            IncidentDetectionPolicy.DetermineTransition(oneHealthy, true, options));
        Assert.Equal(
            IncidentTransition.Resolve,
            IncidentDetectionPolicy.DetermineTransition(twoHealthy, true, options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasurementServerFailure_DoesNotOpenOrResolveIncident(bool hasOpenIncident)
    {
        var signals = new[]
        {
            new IncidentSignal(Guid.NewGuid(), Now, !hasOpenIncident, MeasurementFailureKind.MeasurementServerUnavailable),
            CreateSignal(-10, !hasOpenIncident),
            CreateSignal(-20, !hasOpenIncident)
        };

        Assert.Equal(IncidentTransition.None,
            IncidentDetectionPolicy.DetermineTransition(signals, hasOpenIncident, new IncidentDetectionOptions()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasurementServerFailure_BreaksEvidenceSequence(bool hasOpenIncident)
    {
        var signals = new[]
        {
            CreateSignal(0, !hasOpenIncident),
            new IncidentSignal(Guid.NewGuid(), Now.AddMinutes(-10), !hasOpenIncident, MeasurementFailureKind.MeasurementServerUnavailable),
            CreateSignal(-30, !hasOpenIncident)
        };

        Assert.Equal(IncidentTransition.None,
            IncidentDetectionPolicy.DetermineTransition(signals, hasOpenIncident,
                new IncidentDetectionOptions { MinimumViolationMinutes = 15 }));
    }

    [Fact]
    public void InternetUnavailable_RemainsEvidenceOfLineProblem()
    {
        var signals = new[]
        {
            new IncidentSignal(Guid.NewGuid(), Now, true, MeasurementFailureKind.InternetUnavailable),
            new IncidentSignal(Guid.NewGuid(), Now.AddMinutes(-10), true, MeasurementFailureKind.InternetUnavailable)
        };
        Assert.Equal(IncidentTransition.Open,
            IncidentDetectionPolicy.DetermineTransition(signals, false, new IncidentDetectionOptions()));
    }

    private static IncidentSignal CreateSignal(int minutes, bool isProblem) =>
        new(Guid.NewGuid(), Now.AddMinutes(minutes), isProblem);
}
