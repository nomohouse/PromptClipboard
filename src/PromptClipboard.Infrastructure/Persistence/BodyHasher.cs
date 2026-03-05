namespace PromptClipboard.Infrastructure.Persistence;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Computes SHA256 hash of normalized body text for duplicate detection.
/// Normalization: Unicode NFKC, ToLowerInvariant, \r\n→\n, trim, collapse spaces.
/// </summary>
public static class BodyHasher
{
    public static string ComputeHash(string body)
    {
        var normalized = Normalize(body);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Unicode NFKC normalization
        var result = text.Normalize(NormalizationForm.FormKC);

        // Case fold
        result = result.ToLowerInvariant();

        // Normalize line endings
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // Trim
        result = result.Trim();

        // Collapse spaces (preserve newlines)
        result = Regex.Replace(result, @"[^\S\n]+", " ");

        return result;
    }
}
