using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Clipbook.Services;

/// <summary>Recognises CSS-style colour text (#rgb, #rrggbb, #rrggbbaa, rgb()/rgba(), hsl()/hsla()) for swatch previews.</summary>
public static class ColorParser
{
    private static readonly Regex Hex = new(@"^#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Rgb = new(
        @"^rgba?\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*(?:[,/]\s*([\d.]+%?)\s*)?\)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Hsl = new(
        @"^hsla?\(\s*(\d{1,3}(?:\.\d+)?)(?:deg)?\s*[, ]\s*(\d{1,3}(?:\.\d+)?)%\s*[, ]\s*(\d{1,3}(?:\.\d+)?)%\s*(?:[,/]\s*([\d.]+%?)\s*)?\)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length > 48 || t.Contains('\n')) return false;

        var m = Hex.Match(t);
        if (m.Success)
        {
            var h = m.Groups[1].Value;
            if (h.Length is 3 or 4) h = string.Concat(h.Select(c => new string(c, 2)));
            byte r = Convert.ToByte(h.Substring(0, 2), 16);
            byte g = Convert.ToByte(h.Substring(2, 2), 16);
            byte b = Convert.ToByte(h.Substring(4, 2), 16);
            byte a = h.Length == 8 ? Convert.ToByte(h.Substring(6, 2), 16) : (byte)255;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        m = Rgb.Match(t);
        if (m.Success)
        {
            int r = int.Parse(m.Groups[1].Value), g = int.Parse(m.Groups[2].Value), b = int.Parse(m.Groups[3].Value);
            if (r > 255 || g > 255 || b > 255) return false;
            color = Color.FromArgb(Alpha(m.Groups[4].Value), (byte)r, (byte)g, (byte)b);
            return true;
        }

        m = Hsl.Match(t);
        if (m.Success)
        {
            double h = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double s = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double l = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (h > 360 || s > 100 || l > 100) return false;
            var (r, g, b) = HslToRgb(h, s / 100.0, l / 100.0);
            color = Color.FromArgb(Alpha(m.Groups[4].Value), r, g, b);
            return true;
        }
        return false;
    }

    private static byte Alpha(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return 255;
        bool percent = raw.EndsWith("%");
        if (!double.TryParse(raw.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 255;
        if (percent) v /= 100.0;
        return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }

    private static (byte, byte, byte) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
