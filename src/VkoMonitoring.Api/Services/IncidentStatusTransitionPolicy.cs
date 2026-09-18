using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Services;

public static class IncidentStatusTransitionPolicy
{
    public static bool CanTransition(IncidentStatus current, IncidentStatus requested) =>
        current == requested || (current, requested) switch
        {
            (IncidentStatus.New, IncidentStatus.SentToProvider) => true,
            (IncidentStatus.SentToProvider, IncidentStatus.InProgress) => true,
            (IncidentStatus.SentToProvider, IncidentStatus.WaitingForInformation) => true,
            (IncidentStatus.InProgress, IncidentStatus.WaitingForInformation) => true,
            (IncidentStatus.InProgress, IncidentStatus.Resolved) => true,
            (IncidentStatus.WaitingForInformation, IncidentStatus.InProgress) => true,
            (IncidentStatus.WaitingForInformation, IncidentStatus.Resolved) => true,
            (IncidentStatus.Resolved, IncidentStatus.Closed) => true,
            (IncidentStatus.Resolved, IncidentStatus.InProgress) => true,
            _ => false
        };
}
