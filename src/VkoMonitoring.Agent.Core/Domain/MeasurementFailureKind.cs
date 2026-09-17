using System.Text.Json.Serialization;

namespace VkoMonitoring.Agent.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<MeasurementFailureKind>))]
public enum MeasurementFailureKind
{
    None = 0,
    InternetUnavailable = 1,
    MeasurementServerUnavailable = 2,
    PartialMeasurement = 3
}
