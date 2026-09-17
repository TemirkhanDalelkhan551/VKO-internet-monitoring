using System.Net.NetworkInformation;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Measurement;

public sealed class NetworkContextProvider : INetworkContextProvider
{
    public NetworkConnectionType GetConnectionType()
    {
        var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                HasGateway(networkInterface))
            .OrderByDescending(networkInterface =>
                networkInterface.GetIPv4Statistics().BytesReceived +
                networkInterface.GetIPv4Statistics().BytesSent)
            .ToArray();

        return activeInterfaces.FirstOrDefault()?.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => NetworkConnectionType.WiFi,
            NetworkInterfaceType.Ethernet or
            NetworkInterfaceType.Ethernet3Megabit or
            NetworkInterfaceType.FastEthernetFx or
            NetworkInterfaceType.FastEthernetT or
            NetworkInterfaceType.GigabitEthernet => NetworkConnectionType.Ethernet,
            NetworkInterfaceType.Ppp or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 =>
                NetworkConnectionType.Mobile,
            null => NetworkConnectionType.Unknown,
            _ => NetworkConnectionType.Other
        };
    }

    private static bool HasGateway(NetworkInterface networkInterface)
    {
        try
        {
            return networkInterface.GetIPProperties().GatewayAddresses.Any(address =>
                !address.Address.Equals(System.Net.IPAddress.Any) &&
                !address.Address.Equals(System.Net.IPAddress.IPv6Any));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }
}
