using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Validation;

public static class IncidentAdministrationValidator
{
    private const int MaximumShortTextLength = 200;
    private const int MaximumDescriptionLength = 4_000;

    public static IReadOnlyDictionary<string, string[]> Validate(
        ManualIncidentCreateRequest request,
        DateTimeOffset nowUtc)
    {
        var errors = new Dictionary<string, string[]>();
        RequireIdentifier(errors, nameof(request.SchoolId), request.SchoolId);
        RequireIdentifier(errors, nameof(request.LineId), request.LineId);
        RequireText(errors, nameof(request.ProblemType), request.ProblemType, MaximumShortTextLength);
        RequireText(errors, nameof(request.Title), request.Title, MaximumShortTextLength);
        RequireText(errors, nameof(request.Description), request.Description, MaximumDescriptionLength);
        RequireText(errors, nameof(request.Actor), request.Actor, MaximumShortTextLength);
        OptionalText(errors, nameof(request.AssignedTo), request.AssignedTo, MaximumShortTextLength);
        OptionalText(errors, nameof(request.Comment), request.Comment, MaximumDescriptionLength);

        if (request.StartedAtUtc > nowUtc.AddMinutes(1))
        {
            errors[nameof(request.StartedAtUtc)] = ["Incident start time cannot be in the future."];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        IncidentStatusChangeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequireText(errors, nameof(request.Actor), request.Actor, MaximumShortTextLength);
        OptionalText(errors, nameof(request.Comment), request.Comment, MaximumDescriptionLength);
        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        IncidentAssignmentRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequireText(errors, nameof(request.Actor), request.Actor, MaximumShortTextLength);
        OptionalText(errors, nameof(request.AssignedTo), request.AssignedTo, MaximumShortTextLength);
        OptionalText(errors, nameof(request.Comment), request.Comment, MaximumDescriptionLength);
        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(
        IncidentCommentCreateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequireText(errors, nameof(request.Actor), request.Actor, MaximumShortTextLength);
        RequireText(errors, nameof(request.Comment), request.Comment, MaximumDescriptionLength);
        return errors;
    }

    private static void RequireIdentifier(
        IDictionary<string, string[]> errors,
        string field,
        Guid value)
    {
        if (value == Guid.Empty)
        {
            errors[field] = ["Identifier is required."];
        }
    }

    private static void RequireText(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            errors[field] = [$"A value between 1 and {maximumLength} characters is required."];
        }
    }

    private static void OptionalText(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int maximumLength)
    {
        if (value?.Length > maximumLength)
        {
            errors[field] = [$"The value cannot exceed {maximumLength} characters."];
        }
    }
}
