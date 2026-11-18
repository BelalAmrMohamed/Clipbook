using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Clipbook.Services;

/// <summary>
/// Fetches page titles for copied links (only when the user enabled it in Settings).
/// Titles are cached on disk, encrypted with the same key as the history.
/// Only http/https links to public-looking hosts are requested, and only the first 128 KB are read.
/// All members are meant to be called from the UI thread.
/// </summary>
public static class LinkPreviewService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, string> Cache = LoadCache();
    private static readonly HashSet<string> InFlight = new();

    private static readonly Regex TitleTag = new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex OgTitle = new(@"<meta[^>]+property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""'](.*?)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 4,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Clipbook/2.0)");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return client;
    }

    /// <summary>Returns the cached title, an empty string if the page is known to have none, or null if unknown.</summary>
    public static string? TryGetCached(string url) => Cache.TryGetValue(url, out var t) ? t : null;

    public static async Task<string?> FetchTitleAsync(string url)
    {
        if (Cache.TryGetValue(url, out var cached)) return cached.Length == 0 ? null : cached;
        if (!IsFetchable(url) || !InFlight.Add(url)) return null;

        string title = "";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (response.IsSuccessStatusCode && (type.Contains("html") || type.Length == 0))
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[128 * 1024];
                int total = 0, read;
                while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total))) > 0)
                    total += read;

                var encoding = Encoding.UTF8;
                try
                {
                    var charset = response.Content.Headers.ContentType?.CharSet;
                    if (!string.IsNullOrWhiteSpace(charset)) encoding = Encoding.GetEncoding(charset.Trim('"'));
                }
                catch { /* unknown charset: stay with UTF-8 */ }

                title = ExtractTitle(encoding.GetString(buffer, 0, total));
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("Link title fetch failed for a link: " + ex.GetType().Name);
            InFlight.Remove(url);
            return null;   // network problem: don't cache, so it can be retried later
        }

        InFlight.Remove(url);
        if (Cache.Count > 2000) Cache.Clear();
        Cache[url] = title;
        SaveCache();
        return title.Length == 0 ? null : title;
    }

    private static string ExtractTitle(string html)
    {
        var m = TitleTag.Match(html);
        var raw = m.Success ? m.Groups[1].Value : "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            var og = OgTitle.Match(html);
            raw = og.Success ? og.Groups[1].Value : "";
        }
        var text = Regex.Replace(WebUtility.HtmlDecode(raw), @"\s+", " ").Trim();
        return text.Length > 160 ? text.Substring(0, 160) + "…" : text;
    }

    private static bool IsFetchable(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host == "localhost" || !host.Contains('.') || host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".lan")) return false;
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return false;
            var b = ip.GetAddressBytes();
            if (b.Length == 4)
            {
                if (b[0] == 10 || b[0] == 127 || b[0] == 0) return false;
                if (b[0] == 192 && b[1] == 168) return false;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
                if (b[0] == 169 && b[1] == 254) return false;
            }
            else return false;   // IPv6 literals: skip
        }
        return true;
    }

    // ---------- encrypted on-disk cache ----------

    private static string CachePath => Path.Combine(AppPaths.DataDir, "linkcache.bin");

    private static Dictionary<string, string> LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var plain = Crypto.Unprotect(File.ReadAllBytes(CachePath));
                if (plain != null)
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
            }
        }
        catch (Exception ex) { AppPaths.Log("Link cache load failed: " + ex.Message); }
        return new Dictionary<string, string>();
    }

    private static void SaveCache()
    {
        try
        {
            AppPaths.EnsureDirs();
            var json = JsonSerializer.SerializeToUtf8Bytes(Cache);
            File.WriteAllBytes(CachePath, Crypto.Protect(json, SettingsService.Current.EncryptHistory));
        }
        catch (Exception ex) { AppPaths.Log("Link cache save failed: " + ex.Message); }
    }
}
