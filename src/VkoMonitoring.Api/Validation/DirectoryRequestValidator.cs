using System.Net.Mail;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Validation;

public static class DirectoryRequestValidator
{
    public static Dictionary<string, string[]> Validate(SchoolSaveRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Text(errors, "name", request.Name, 200, true);
        Text(errors, "districtCity", request.DistrictCity, 200);
        Text(errors, "address", request.Address, 1000);
        Text(errors, "responsibleName", request.ResponsibleName, 200);
        Text(errors, "responsiblePosition", request.ResponsiblePosition, 200);
        Text(errors, "responsiblePhone", request.ResponsiblePhone, 100);
        Text(errors, "responsibleEmail", request.ResponsibleEmail, 254);
        if (!string.IsNullOrWhiteSpace(request.ResponsibleEmail) &&
            (!MailAddress.TryCreate(request.ResponsibleEmail.Trim(), out var email) || email.Address != request.ResponsibleEmail.Trim()))
            errors["responsibleEmail"] = ["Введите корректный адрес электронной почты."];
        return errors;
    }

    public static Dictionary<string, string[]> Validate(LineSaveRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Text(errors, "name", request.Name, 200, true);
        Text(errors, "providerName", request.ProviderName, 200);
        Text(errors, "connectionType", request.ConnectionType, 100);
        Text(errors, "contractNumber", request.ContractNumber, 200);
        foreach (var (field, speed) in new[] { ("contractedDownloadMbps", request.ContractedDownloadMbps), ("contractedUploadMbps", request.ContractedUploadMbps) })
            if (speed is { } value && (value <= 0 || value > 999999999.999m || decimal.Round(value, 3) != value))
                errors[field] = ["Скорость должна быть положительной, не более 999999999,999 Мбит/с, до трёх знаков после запятой."];
        if (request.LineStatus is not ("Primary" or "Backup" or "Disabled"))
            errors["lineStatus"] = ["Выберите основную, резервную или отключённую линию."];
        return errors;
    }

    private static void Text(Dictionary<string, string[]> errors, string field, string? value, int maximum, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value) || value?.Trim().Length > maximum || value?.Any(char.IsControl) == true)
            errors[field] = [$"Поле должно содержать {(required ? "от 1 до" : "не более")} {maximum} символов без управляющих символов."];
    }
}
