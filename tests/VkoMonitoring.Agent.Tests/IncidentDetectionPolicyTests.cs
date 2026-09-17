using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Services;

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

    private static IncidentSignal CreateSignal(int minutes, bool isProblem) =>
        new(Guid.NewGuid(), Now.AddMinutes(minutes), isProblem);
}
