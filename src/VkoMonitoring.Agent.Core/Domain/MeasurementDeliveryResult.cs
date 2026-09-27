namespace VkoMonitoring.Agent.Core.Domain;

public enum MeasurementDeliveryOutcome
{
    Delivered,
    PermanentlyRejected
}

public sealed record MeasurementDeliveryResult(
    MeasurementDeliveryOutcome Outcome,
    int? ResponseStatusCode = null)
{
    public static MeasurementDeliveryResult Delivered { get; } =
        new(MeasurementDeliveryOutcome.Delivered);

    public static MeasurementDeliveryResult PermanentlyRejected(int responseStatusCode) =>
        new(MeasurementDeliveryOutcome.PermanentlyRejected, responseStatusCode);
}
