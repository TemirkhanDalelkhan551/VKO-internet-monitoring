namespace VkoMonitoring.Api.Models;

public sealed record SchoolSaveRequest(string Name, string? DistrictCity, string? Address,
    string? ResponsibleName, string? ResponsiblePosition, string? ResponsiblePhone, string? ResponsibleEmail);

public sealed record LineSaveRequest(string Name, string? ProviderName, string? ConnectionType,
    decimal? ContractedDownloadMbps, decimal? ContractedUploadMbps, string? ContractNumber,
    DateOnly? ContractDate, string LineStatus);
