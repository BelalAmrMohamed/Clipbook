using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clipbook.Models;

namespace Clipbook.Services;

/// <summary>
/// Export / import of the clipboard history as a single file.
/// Plain backups are JSON; password-protected backups are AES-256-GCM (key from PBKDF2-SHA256) with a "CLBK1" header.
/// </summary>
public static class BackupService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CLBK1");
    private const int Pbkdf2Iterations = 250_000;

    public sealed class BackupItem
    {
        public string Type { get; set; } = "Text";
        public string? Text { get; set; }
        public string? Html { get; set; }
        public string? ImageBase64 { get; set; }
        public string? SourceApp { get; set; }
        public string? Collection { get; set; }
        public DateTime CreatedUtc { get; set; }
        public bool Pinned { get; set; }
    }

    public sealed class BackupFile
    {
        public string Format { get; set; } = "clipbook-backup";
        public int Version { get; set; } = 1;
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
        public List<BackupItem> Items { get; set; } = new();
    }

    public sealed record ExportOptions(bool PinnedOnly, bool IncludeImages, bool IncludeSensitive, string? Password);
    public sealed record ImportResult(int Added, int Skipped, int Failed, string? Error);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ---------------- Export ----------------

    public static int Export(IEnumerable<ClipItem> items, string path, ExportOptions options)
    {
        var file = new BackupFile();
        foreach (var item in items.OrderBy(i => i.CreatedUtc))
        {
            if (options.PinnedOnly && !item.Pinned) continue;
            if (item.Sensitive && !options.IncludeSensitive) continue;
            if (item.IsImage && !options.IncludeImages) continue;

            var b = new BackupItem
            {
                Type = item.Type.ToString(),
                Text = item.Text,
                Html = item.Html,
                SourceApp = item.SourceApp,
                Collection = item.Collection,
                CreatedUtc = item.CreatedUtc,
                Pinned = item.Pinned
            };
            if (item.IsImage)
            {
                var png = ImageStore.LoadBytes(item.ImageFile);
                if (png == null) continue;
                b.ImageBase64 = Convert.ToBase64String(png);
            }
            file.Items.Add(b);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(file, Json);
        var bytes = string.IsNullOrEmpty(options.Password) ? json : EncryptWithPassword(json, options.Password!);
        File.WriteAllBytes(path, bytes);
        return file.Items.Count;
    }

    // ---------------- Import ----------------

    public static bool IsEncrypted(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[Magic.Length];
            return fs.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(Magic);
        }
        catch { return false; }
    }

    public static ImportResult Import(string path, string? password, HistoryManager history)
    {
        BackupFile? file;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan().StartsWith(Magic))
            {
                if (string.IsNullOrEmpty(password)) return new ImportResult(0, 0, 0, "This backup is password-protected. Enter the password.");
                var plain = DecryptWithPassword(bytes, password!);
                if (plain == null) return new ImportResult(0, 0, 0, "Wrong password, or the file is damaged.");
                bytes = plain;
            }
            file = JsonSerializer.Deserialize<BackupFile>(bytes, Json);
        }
        catch (Exception ex)
        {
            AppPaths.Log("Backup read failed: " + ex.Message);
            return new ImportResult(0, 0, 0, "This doesn't look like a Clipbook backup: " + ex.Message);
        }

        if (file == null || file.Format != "clipbook-backup")
            return new ImportResult(0, 0, 0, "This doesn't look like a Clipbook backup.");
        if (file.Version > 1)
            return new ImportResult(0, 0, 0, "This backup was made by a newer version of Clipbook.");

        var items = new List<ClipItem>();
        int failed = 0;
        foreach (var b in file.Items)
        {
            var item = ToClipItem(b);
            if (item == null) failed++; else items.Add(item);
        }

        var (added, skipped) = history.ImportItems(items);
        return new ImportResult(added, skipped, failed, null);
    }

    private static ClipItem? ToClipItem(BackupItem b)
    {
        if (!Enum.TryParse<ClipType>(b.Type, out var type)) return null;
        try
        {
            var item = new ClipItem
            {
                Type = type,
                Text = b.Text,
                Html = b.Html,
                SourceApp = b.SourceApp,
                Collection = string.IsNullOrWhiteSpace(b.Collection) ? null : b.Collection,
                CreatedUtc = b.CreatedUtc.Kind == DateTimeKind.Utc ? b.CreatedUtc : b.CreatedUtc.ToUniversalTime(),
                Pinned = b.Pinned || !string.IsNullOrWhiteSpace(b.Collection)
            };

            if (type == ClipType.Image)
            {
                if (string.IsNullOrEmpty(b.ImageBase64)) return null;
                var png = Convert.FromBase64String(b.ImageBase64);
                if (ImageStore.Decode(png, 0) == null) return null;   // not a readable image
                item.ImageFile = ImageStore.Save(png);
                item.Hash = "img:" + Crypto.Hash(png);
                item.SizeBytes = png.Length;
                return item;
            }

            if (b.Text == null) return null;
            var textBytes = Encoding.UTF8.GetBytes(b.Text);
            item.SizeBytes = textBytes.Length + (b.Html == null ? 0 : Encoding.UTF8.GetByteCount(b.Html));
            item.Hash = type switch
            {
                ClipType.Note => "note:" + Crypto.Hash(textBytes) + ":" + Guid.NewGuid().ToString("N"),
                ClipType.Files => "files:" + Crypto.Hash(textBytes),
                _ => "txt:" + Crypto.Hash(textBytes)
            };
            return item;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Backup item skipped: " + ex.Message);
            return null;
        }
    }

    // ---------------- Password encryption ----------------

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] EncryptWithPassword(byte[] plain, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(DeriveKey(password, salt), 16))
            aes.Encrypt(nonce, plain, cipher, tag);

        var result = new byte[Magic.Length + 16 + 12 + 16 + cipher.Length];
        int o = 0;
        Buffer.BlockCopy(Magic, 0, result, o, Magic.Length); o += Magic.Length;
        Buffer.BlockCopy(salt, 0, result, o, 16); o += 16;
        Buffer.BlockCopy(nonce, 0, result, o, 12); o += 12;
        Buffer.BlockCopy(tag, 0, result, o, 16); o += 16;
        Buffer.BlockCopy(cipher, 0, result, o, cipher.Length);
        return result;
    }

    private static byte[]? DecryptWithPassword(byte[] data, string password)
    {
        try
        {
            int o = Magic.Length;
            if (data.Length < o + 16 + 12 + 16) return null;
            var salt = data.AsSpan(o, 16).ToArray(); o += 16;
            var nonce = data.AsSpan(o, 12).ToArray(); o += 12;
            var tag = data.AsSpan(o, 16).ToArray(); o += 16;
            var cipher = data.AsSpan(o).ToArray();
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(DeriveKey(password, salt), 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }
        catch (CryptographicException) { return null; }
    }
}
