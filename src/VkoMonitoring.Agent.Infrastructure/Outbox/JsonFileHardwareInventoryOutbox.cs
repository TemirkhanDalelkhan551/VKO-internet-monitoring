using System.Text.Json;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Outbox;

public sealed class JsonFileHardwareInventoryOutbox : IHardwareInventoryOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileHardwareInventoryOutbox(AgentOptions options)
    {
        var directory = Path.GetFullPath(Path.Combine(options.DataDirectory, "inventory-outbox"));
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "latest-inventory.json");
    }

    public async Task StoreAsync(HardwareInventory inventory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var temporaryPath = _path + ".tmp";
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, inventory, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _gate.Release();
        }
    }

    public async Task<HardwareInventory?> GetPendingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<HardwareInventory>(stream, SerializerOptions, cancellationToken)
                ?? throw new JsonException("The queued hardware inventory is empty.");
        }
        finally { _gate.Release(); }
    }

    public async Task MarkAsSentAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { if (File.Exists(_path)) File.Delete(_path); }
        finally { _gate.Release(); }
    }
}
