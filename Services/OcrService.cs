using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Clipbook.Services;

/// <summary>Text recognition for images, using the OCR engine built into Windows 10/11 (offline).</summary>
public static class OcrService
{
    public sealed record OcrOutcome(string? Text, string? Error);

    public static async Task<OcrOutcome> RecognizeAsync(byte[] imageBytes)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                return new OcrOutcome(null, "Windows has no OCR language installed. Add one in Settings > Time & language > Language & region.");

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(imageBytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            uint max = OcrEngine.MaxImageDimension;
            SoftwareBitmap bitmap;
            if (decoder.PixelWidth > max || decoder.PixelHeight > max)
            {
                double scale = Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight);
                var transform = new BitmapTransform
                {
                    ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale),
                    ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale)
                };
                bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            }
            else
            {
                bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }

            using (bitmap)
            {
                var result = await engine.RecognizeAsync(bitmap);
                var text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
                return new OcrOutcome(text.Length == 0 ? null : text, null);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("OCR failed: " + ex);
            return new OcrOutcome(null, "Text recognition failed: " + ex.Message);
        }
    }
}
