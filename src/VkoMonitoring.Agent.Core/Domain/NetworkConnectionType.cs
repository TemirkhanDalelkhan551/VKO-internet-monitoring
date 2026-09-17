using System.Text.Json.Serialization;

namespace VkoMonitoring.Agent.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<NetworkConnectionType>))]
public enum NetworkConnectionType
{
    Unknown,
    Ethernet,
    WiFi,
    Mobile,
    Other
}
