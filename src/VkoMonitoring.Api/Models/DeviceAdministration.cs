namespace VkoMonitoring.Api.Models;

public sealed record DeviceBlockStateRequest(bool IsBlocked);

public sealed record DeviceBlockStateResult(Guid DeviceId, bool IsBlocked);

public sealed record DeviceTokenRotationResult(Guid DeviceId, string DeviceToken);

public sealed record DeviceRebindRequest(Guid SchoolId, Guid LineId, string Reason);

public sealed record DeviceReplacementRequest(Guid ReplacementDeviceId, string Reason);

public sealed record DeviceDecommissionRequest(string Reason);

public sealed record DeviceLifecycleResult(
    Guid DeviceId,
    string LifecycleStatus,
    Guid SchoolId,
    Guid LineId,
    Guid? ReplacedByDeviceId,
    DateTimeOffset? RetiredAtUtc,
    string? RetirementReason);

public sealed record DeviceLifecycleEvent(
    long EventId,
    Guid DeviceId,
    string Action,
    Guid? PreviousSchoolId,
    Guid? PreviousLineId,
    Guid? CurrentSchoolId,
    Guid? CurrentLineId,
    Guid? ReplacementDeviceId,
    string Reason,
    string Actor,
    DateTimeOffset OccurredAtUtc);
