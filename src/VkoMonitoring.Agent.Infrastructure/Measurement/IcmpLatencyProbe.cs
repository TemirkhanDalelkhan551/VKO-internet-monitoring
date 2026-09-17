using System.Net.NetworkInformation;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Measurement;

public sealed class IcmpLatencyProbe(AgentOptions options) : ILatencyProbe
{
    public async Task<LatencyMetrics> MeasureAsync(CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        var successfulRoundTrips = new List<long>(options.PingAttempts);

        for (var attempt = 0; attempt < options.PingAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reply = await ping.SendPingAsync(
                options.PingHost,
                TimeSpan.FromMilliseconds(options.PingTimeoutMilliseconds),
                cancellationToken: cancellationToken);

            if (reply.Status == IPStatus.Success)
            {
                successfulRoundTrips.Add(reply.RoundtripTime);
            }
        }

        if (successfulRoundTrips.Count == 0)
        {
            throw new PingException($"No successful ping replies from {options.PingHost}.");
        }

        var packetLoss = 100d * (options.PingAttempts - successfulRoundTrips.Count) / options.PingAttempts;
        return new LatencyMetrics(
            Math.Round(successfulRoundTrips.Average(), 2),
            CalculateJitter(successfulRoundTrips),
            Math.Round(packetLoss, 2));
    }

    private static double CalculateJitter(IReadOnlyList<long> roundTrips)
    {
        if (roundTrips.Count < 2)
        {
            return 0;
        }

        var differences = new double[roundTrips.Count - 1];
        for (var index = 1; index < roundTrips.Count; index++)
        {
            differences[index - 1] = Math.Abs(roundTrips[index] - roundTrips[index - 1]);
        }

        return Math.Round(differences.Average(), 2);
    }
}
