using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Infrastructure.Api;

public sealed class ConfigurationDeviceTokenProvider(AgentOptions options) : IDeviceTokenProvider
{
    public string GetToken() => options.DeviceToken;
}
