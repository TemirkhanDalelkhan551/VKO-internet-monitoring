using System.Text.Json.Serialization;

namespace VkoMonitoring.Agent.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionStatus>))]
public enum ConnectionStatus
{
    Online = 1,
    Degraded = 2,
    Offline = 3
}
