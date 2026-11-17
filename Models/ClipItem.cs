using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using AdvancedClipboard.Services;

namespace AdvancedClipboard.Models;

public enum ClipType { Text = 0, Link = 1, Image = 2, Files = 3, Note = 4 }

public sealed class ClipItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public long Id { get; set; }
    public ClipType Type { get; set; }
    public string? Text { get; set; }
    public string? Html { get; set; }
    public string? ImageFile { get; set; }
    public string? SourceApp { get; set; }
    public string Hash { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool Sensitive { get; set; }
    public DateTime? ExpiresUtc { get; set; }

    private DateTime _created = DateTime.UtcNow;
    public DateTime CreatedUtc
    {
        get => _created;
        set { _created = value; OnChanged(); OnChanged(nameof(Meta)); }
    }

    private bool _pinned;
    public bool Pinned
    {
        get => _pinned;
        set { _pinned = value; OnChanged(); OnChanged(nameof(PinTooltip)); OnChanged(nameof(Meta)); }
    }

    private string? _collection;
    public string? Collection
    {
        get => _collection;
        set { _collection = value; OnChanged(); OnChanged(nameof(Meta)); }
    }

    // ---- UI helpers ----

    public bool IsImage => Type == ClipType.Image;
    public bool IsTextual => Type != ClipType.Image;
    public string PinTooltip => Pinned ? "Unpin" : "Pin";

    public string DisplayText
    {
        get
        {
            if (Sensitive) return "••••••••   (sensitive data hidden)";
            var t = Text ?? "";
            return t.Length > 500 ? t.Substring(0, 500) + "…" : t;
        }
    }

    public string Meta
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            switch (Type)
            {
                case ClipType.Note: parts.Add("📝 Note"); break;
                case ClipType.Link: parts.Add("🔗 Link"); break;
                case ClipType.Files: parts.Add("📁 Files"); break;
                case ClipType.Image: parts.Add("🖼 Image"); break;
            }
            if (!string.IsNullOrEmpty(SourceApp)) parts.Add(SourceApp!);
            parts.Add(TimeAgo(CreatedUtc));
            if (!string.IsNullOrEmpty(Collection)) parts.Add("📂 " + Collection);
            if (Sensitive) parts.Add("🛡 sensitive");
            return string.Join("  •  ", parts);
        }
    }

    private BitmapSource? _thumb;
    public BitmapSource? Thumbnail => _thumb ??= ImageStore.LoadThumbnail(ImageFile);

    public void RaiseTimeChanged() => OnChanged(nameof(Meta));

    public void RaiseTextChanged()
    {
        OnChanged(nameof(DisplayText));
        OnChanged(nameof(Meta));
    }

    public void RaiseSensitiveChanged()
    {
        OnChanged(nameof(DisplayText));
        OnChanged(nameof(Meta));
    }

    public static string TimeAgo(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 10) return "just now";
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s ago";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return utc.ToLocalTime().ToString("MMM d, yyyy");
    }
}
