using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface INetworkContextProvider
{
    NetworkConnectionType GetConnectionType();
}
