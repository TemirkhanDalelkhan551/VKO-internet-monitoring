using System.Text.Json;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;

namespace VkoMonitoring.Api.Persistence;

public sealed class JsonFileDevicePresenceRepository : IDevicePresenceRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MonitoringApiOptions _options;
    private readonly string _storageDirectory;

    public JsonFileDevicePresenceRepository(MonitoringApiOptions options)
    {
        _options = options;
        _storageDirectory = Path.GetFullPath(Path.Combine(options.DataDirectory, "devices"));
        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task<bool> RecordHeartbeatAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        if (!_options.DeviceBindings.TryGetValue(heartbeat.DeviceId.ToString("D"), out var binding) ||
            binding.SchoolId != heartbeat.SchoolId ||
            binding.LineId != heartbeat.LineId)
        {
            return false;
        }

        var destinationPath = Path.Combine(_storageDirectory, $"{heartbeat.DeviceId:N}.json");
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    heartbeat,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
