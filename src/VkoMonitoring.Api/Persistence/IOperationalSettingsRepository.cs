using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public interface IOperationalSettingsRepository
{
    Task<OperationalSettings> GetAsync(CancellationToken cancellationToken);
    Task<OperationalSettings> UpdateAsync(OperationalSettings settings, CancellationToken cancellationToken);
}
