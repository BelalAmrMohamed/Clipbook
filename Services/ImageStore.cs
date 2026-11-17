using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace AdvancedClipboard.Services;

/// <summary>Stores clipboard images as (optionally encrypted) PNG blobs in the data folder.</summary>
public static class ImageStore
{
    public static string Save(byte[] png)
    {
        AppPaths.EnsureDirs();
        var name = Guid.NewGuid().ToString("N") + ".bin";
        File.WriteAllBytes(Path.Combine(AppPaths.ImagesDir, name),
            Crypto.Protect(png, SettingsService.Current.EncryptHistory));
        return name;
    }

    public static byte[]? LoadBytes(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        try
        {
            var path = Path.Combine(AppPaths.ImagesDir, name);
            return File.Exists(path) ? Crypto.Unprotect(File.ReadAllBytes(path)) : null;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Image load failed: " + ex.Message);
            return null;
        }
    }

    public static BitmapSource? LoadThumbnail(string? name, int decodeWidth = 360)
    {
        var bytes = LoadBytes(name);
        return bytes == null ? null : Decode(bytes, decodeWidth);
    }

    public static BitmapSource? LoadFull(string? name)
    {
        var bytes = LoadBytes(name);
        return bytes == null ? null : Decode(bytes, 0);
    }

    public static BitmapSource? Decode(byte[] bytes, int decodeWidth)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            AppPaths.Log("Image decode failed: " + ex.Message);
            return null;
        }
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static void Delete(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        try { File.Delete(Path.Combine(AppPaths.ImagesDir, name)); }
        catch { /* ignore */ }
    }
}
