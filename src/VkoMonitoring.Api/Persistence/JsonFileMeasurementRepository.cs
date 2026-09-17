using System.Text.Json;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;

namespace VkoMonitoring.Api.Persistence;

public sealed class JsonFileMeasurementRepository : IMeasurementRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _storageDirectory;

    public JsonFileMeasurementRepository(MonitoringApiOptions options)
    {
        _storageDirectory = Path.GetFullPath(Path.Combine(options.DataDirectory, "measurements"));
        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task<bool> AddIfNotExistsAsync(
        InternetMeasurement measurement,
        CancellationToken cancellationToken)
    {
        var destinationPath = GetPath(measurement.EventId);

        try
        {
            await using var stream = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await JsonSerializer.SerializeAsync(stream, measurement, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return true;
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<InternetMeasurement>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var recentFiles = Directory
            .EnumerateFiles(_storageDirectory, "*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Take(limit);
        var measurements = new List<InternetMeasurement>(limit);

        foreach (var file in recentFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous);
            var measurement = await JsonSerializer.DeserializeAsync<InternetMeasurement>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (measurement is not null)
            {
                measurements.Add(measurement);
            }
        }

        return measurements;
    }

    private string GetPath(Guid eventId) => Path.Combine(_storageDirectory, $"{eventId:N}.json");
}
