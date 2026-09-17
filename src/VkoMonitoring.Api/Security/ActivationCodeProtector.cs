using System.Security.Cryptography;
using System.Text;

namespace VkoMonitoring.Api.Security;

public static class ActivationCodeProtector
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 12;

    public static string Generate()
    {
        Span<char> characters = stackalloc char[CodeLength];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return string.Create(14, characters.ToArray(), static (result, source) =>
        {
            source.AsSpan(0, 4).CopyTo(result);
            result[4] = '-';
            source.AsSpan(4, 4).CopyTo(result[5..]);
            result[9] = '-';
            source.AsSpan(8, 4).CopyTo(result[10..]);
        });
    }

    public static byte[] Hash(string activationCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationCode);
        return SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(activationCode)));
    }

    public static bool IsValidFormat(string activationCode)
    {
        if (string.IsNullOrWhiteSpace(activationCode))
        {
            return false;
        }

        var normalized = Normalize(activationCode);
        return normalized.Length == CodeLength &&
               normalized.All(character => Alphabet.Contains(character, StringComparison.Ordinal));
    }

    private static string Normalize(string activationCode) =>
        new(activationCode
            .Where(character => character is not ('-' or ' '))
            .Select(char.ToUpperInvariant)
            .ToArray());
}
