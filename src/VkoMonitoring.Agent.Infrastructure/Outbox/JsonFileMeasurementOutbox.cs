using System.Text.Json;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Outbox;

public sealed class JsonFileMeasurementOutbox : IMeasurementOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _outboxDirectory;
    private readonly string _quarantineDirectory;
    private readonly int _maxQueuedMeasurements;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileMeasurementOutbox(AgentOptions options)
    {
        _outboxDirectory = Path.GetFullPath(Path.Combine(options.DataDirectory, "outbox"));
        _quarantineDirectory = Path.GetFullPath(Path.Combine(options.DataDirectory, "quarantine"));
        _maxQueuedMeasurements = options.MaxQueuedMeasurements;
        Directory.CreateDirectory(_outboxDirectory);
        Directory.CreateDirectory(_quarantineDirectory);
    }

    public async Task EnqueueAsync(InternetMeasurement measurement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        var destinationPath = GetPath(measurement.EventId);
        var temporaryPath = destinationPath + ".tmp";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureCapacity();

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, measurement, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<InternetMeasurement>> GetPendingAsync(CancellationToken cancellationToken)
    {
        var pending = new List<InternetMeasurement>();
        var files = Directory
            .EnumerateFiles(_outboxDirectory, "*.json")
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.CreationTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var measurement = await ReadMeasurementAsync(file.FullName, cancellationToken);
                pending.Add(measurement);
            }
            catch (JsonException)
            {
                Quarantine(file.FullName);
            }
        }

        return pending;
    }

    public Task MarkAsSentAsync(Guid eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(GetPath(eventId));
        return Task.CompletedTask;
    }

    private string GetPath(Guid eventId) => Path.Combine(_outboxDirectory, $"{eventId:N}.json");

    private void EnsureCapacity()
    {
        var queuedCount = Directory
            .EnumerateFiles(_outboxDirectory, "*.json")
            .Take(_maxQueuedMeasurements)
            .Count();

        if (queuedCount >= _maxQueuedMeasurements)
        {
            throw new OutboxCapacityExceededException(_maxQueuedMeasurements);
        }
    }

    private static async Task<InternetMeasurement> ReadMeasurementAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous);
        var measurement = await JsonSerializer.DeserializeAsync<InternetMeasurement>(
            stream,
            SerializerOptions,
            cancellationToken);

        return measurement ?? throw new JsonException("The queued measurement is empty.");
    }

    private void Quarantine(string path)
    {
        var destinationPath = Path.Combine(_quarantineDirectory, Path.GetFileName(path));
        File.Move(path, destinationPath, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
