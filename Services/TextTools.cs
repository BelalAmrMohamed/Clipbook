using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clipbook.Services;

public static class TextTools
{
    public static readonly IReadOnlyList<(string Name, Func<string, string> Apply)> All = new List<(string, Func<string, string>)>
    {
        ("UPPERCASE", s => s.ToUpperInvariant()),
        ("lowercase", s => s.ToLowerInvariant()),
        ("Title Case", s => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower())),
        ("Trim whitespace", s => s.Trim()),
        ("Remove line breaks", s => Regex.Replace(s, @"\s*[\r\n]+\s*", " ").Trim()),
        ("Collapse extra spaces", s => Regex.Replace(s, @"[ \t]{2,}", " ")),
        ("Sort lines", s => string.Join(Environment.NewLine, s.Split('\n').Select(l => l.TrimEnd('\r')).OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase))),
        ("Remove duplicate lines", s => string.Join(Environment.NewLine, s.Split('\n').Select(l => l.TrimEnd('\r')).Distinct())),
        ("Format JSON", FormatJson),
        ("Minify JSON", MinifyJson),
    };

    public static string FormatJson(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse(s);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return s; }
    }

    public static string MinifyJson(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse(s);
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch { return s; }
    }
}
