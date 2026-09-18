namespace VkoMonitoring.Api.Models;

public sealed record DeviceBlockStateRequest(bool IsBlocked);

public sealed record DeviceBlockStateResult(Guid DeviceId, bool IsBlocked);

public sealed record DeviceTokenRotationResult(Guid DeviceId, string DeviceToken);
