using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clipbook.Services;

/// <summary>Heuristics for text that probably should not live in a clipboard history.</summary>
public static class SensitiveDetector
{
    private static readonly Regex[] Patterns =
    {
        new(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled),                         // AWS access key
        new(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b", RegexOptions.Compiled),               // GitHub token
        new(@"\bsk-[A-Za-z0-9_\-]{20,}\b", RegexOptions.Compiled),                   // API secret keys
        new(@"\bxox[abprs]-[A-Za-z0-9\-]{10,}\b", RegexOptions.Compiled),            // Slack token
        new(@"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\b", RegexOptions.Compiled), // JWT
        new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled),
        new(@"(?i)\b(password|passwd|pwd|secret|api[_-]?key|token)\s*[:=]\s*\S{6,}", RegexOptions.Compiled),
    };

    private static readonly Regex CardCandidate = new(@"\b(?:\d[ -]?){13,19}\b", RegexOptions.Compiled);

    public static bool IsSensitive(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) return false;
        if (Patterns.Any(p => p.IsMatch(text))) return true;

        foreach (Match m in CardCandidate.Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 13 and <= 19 && Luhn(digits)) return true;
        }
        return false;
    }

    private static bool Luhn(string digits)
    {
        int sum = 0;
        bool alt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int n = digits[i] - '0';
            if (alt) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            alt = !alt;
        }
        return sum % 10 == 0;
    }
}
