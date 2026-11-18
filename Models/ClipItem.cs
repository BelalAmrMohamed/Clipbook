using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipbook.Services;

namespace Clipbook.Models;

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

    private int _queuePosition;
    /// <summary>1-based position in the paste queue, or 0 when not queued.</summary>
    public int QueuePosition
    {
        get => _queuePosition;
        set { _queuePosition = value; OnChanged(); OnChanged(nameof(Meta)); }
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
            if (QueuePosition > 0) parts.Add("⏭ queue #" + QueuePosition);
            return string.Join("  •  ", parts);
        }
    }

    private BitmapSource? _thumb;
    public BitmapSource? Thumbnail => _thumb ??= ImageStore.LoadThumbnail(ImageFile);

    // ---- Colour swatch (for text like #ff8800 or rgb(10, 20, 30)) ----

    private bool _colorChecked;
    private SolidColorBrush? _colorBrush;

    public SolidColorBrush? ColorSwatch
    {
        get
        {
            if (!_colorChecked)
            {
                _colorChecked = true;
                if (!Sensitive && Type is ClipType.Text or ClipType.Note && ColorParser.TryParse(Text, out var c))
                {
                    _colorBrush = new SolidColorBrush(c);
                    _colorBrush.Freeze();
                }
            }
            return _colorBrush;
        }
    }

    public bool HasColor => ColorSwatch != null;

    // ---- Link title (fetched on demand, only if enabled in Settings) ----

    private string? _linkTitle;
    private bool _linkRequested;

    public string? LinkTitle
    {
        get
        {
            if (Type != ClipType.Link || Sensitive || !SettingsService.Current.FetchLinkTitles || string.IsNullOrWhiteSpace(Text))
                return null;
            if (_linkTitle == null && !_linkRequested)
            {
                _linkRequested = true;
                var url = Text.Trim();
                var cached = LinkPreviewService.TryGetCached(url);
                if (cached != null) _linkTitle = cached.Length > 0 ? cached : null;
                else _ = LoadLinkTitleAsync(url);
            }
            return _linkTitle;
        }
    }

    public bool HasLinkTitle => LinkTitle != null;

    private async Task LoadLinkTitleAsync(string url)
    {
        var title = await LinkPreviewService.FetchTitleAsync(url);
        if (string.IsNullOrEmpty(title)) return;
        _linkTitle = title;
        OnChanged(nameof(LinkTitle));
        OnChanged(nameof(HasLinkTitle));
    }

    public void RaiseTimeChanged() => OnChanged(nameof(Meta));

    public void RaiseTextChanged()
    {
        _colorChecked = false;
        _colorBrush = null;
        _linkTitle = null;
        _linkRequested = false;
        OnChanged(nameof(DisplayText));
        OnChanged(nameof(Meta));
        OnChanged(nameof(ColorSwatch));
        OnChanged(nameof(HasColor));
        OnChanged(nameof(LinkTitle));
        OnChanged(nameof(HasLinkTitle));
    }

    public void RaiseSensitiveChanged()
    {
        _colorChecked = false;
        OnChanged(nameof(DisplayText));
        OnChanged(nameof(Meta));
        OnChanged(nameof(ColorSwatch));
        OnChanged(nameof(HasColor));
        OnChanged(nameof(LinkTitle));
        OnChanged(nameof(HasLinkTitle));
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
