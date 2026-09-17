using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Measurement;

public sealed class WindowsSystemLoadSampler(
    AgentOptions options,
    TimeProvider timeProvider) : ISystemLoadSampler
{
    public async Task<SystemLoadSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SystemLoadSnapshot(0, 0);
        }

        var beforeCpu = ReadCpuTimes();
        var beforeNetworkBytes = ReadNetworkBytes();
        var startedAt = timeProvider.GetTimestamp();
        await Task.Delay(
            TimeSpan.FromMilliseconds(options.SystemLoadSampleMilliseconds),
            timeProvider,
            cancellationToken);
        var elapsed = timeProvider.GetElapsedTime(startedAt);
        var afterCpu = ReadCpuTimes();
        var afterNetworkBytes = ReadNetworkBytes();

        return new SystemLoadSnapshot(
            CalculateCpuUsage(beforeCpu, afterCpu),
            CalculateNetworkMbps(beforeNetworkBytes, afterNetworkBytes, elapsed));
    }

    private static CpuTimes ReadCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new InvalidOperationException("Не удалось получить загрузку процессора Windows.");
        }

        return new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
    }

    private static long ReadNetworkBytes() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Sum(networkInterface =>
            {
                try
                {
                    var statistics = networkInterface.GetIPv4Statistics();
                    return statistics.BytesReceived + statistics.BytesSent;
                }
                catch (NetworkInformationException)
                {
                    return 0L;
                }
            });

    private static double CalculateCpuUsage(CpuTimes before, CpuTimes after)
    {
        var idle = after.Idle - before.Idle;
        var total = (after.Kernel - before.Kernel) + (after.User - before.User);
        return total == 0
            ? 0
            : Math.Round(Math.Clamp((total - idle) * 100d / total, 0, 100), 2);
    }

    private static double CalculateNetworkMbps(long before, long after, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var transferredBytes = Math.Max(0, after - before);
        return Math.Round(transferredBytes * 8d / seconds / 1_000_000d, 3);
    }

    private static ulong ToUInt64(FileTime fileTime) =>
        ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;

    [DllImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        public readonly uint LowDateTime;
        public readonly uint HighDateTime;
    }

    private sealed record CpuTimes(ulong Idle, ulong Kernel, ulong User);
}
