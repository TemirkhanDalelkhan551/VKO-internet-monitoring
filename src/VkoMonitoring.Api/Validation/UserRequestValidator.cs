using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Validation;

public static class UserRequestValidator
{
    public static bool IsPasswordValid(string? value) => value is { Length: >= 12 and <= 128 };
    public static Dictionary<string, string[]> Validate(string? login, string? displayName, UserRole role,
        Guid? schoolId, string? districtCity, string? providerName, string? password = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (login is not null && (login.Length is < 3 or > 64 || login.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))))
            errors["login"] = ["Use 3–64 ASCII letters, digits, dots, underscores or hyphens."];
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 150) errors["displayName"] = ["Display name is required (up to 150 characters)."];
        if (!Enum.IsDefined(role)) errors["role"] = ["Unknown role."];
        if (role == UserRole.School && (schoolId is null || schoolId == Guid.Empty)) errors["schoolId"] = ["A school binding is required."];
        if (role == UserRole.District && (string.IsNullOrWhiteSpace(districtCity) || districtCity.Length > 150)) errors["districtCity"] = ["A district/city binding is required."];
        if (role == UserRole.Provider && (string.IsNullOrWhiteSpace(providerName) || providerName.Length > 150)) errors["providerName"] = ["A provider binding is required."];
        if (password is not null && !IsPasswordValid(password)) errors["password"] = ["Password must contain 12–128 characters."];
        return errors;
    }
}
