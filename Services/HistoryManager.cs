using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using AdvancedClipboard.Models;

namespace AdvancedClipboard.Services;

public enum HistoryTab { History, Pinned }

public sealed class HistoryManager
{
    private readonly Repository _repo;
    private readonly List<ClipItem> _items = new();   // newest first
    private readonly DispatcherTimer _timer;

    public event Action? Changed;

    public HistoryManager(Repository repo)
    {
        _repo = repo;
        _items = _repo.LoadAll();
        EnforceLimits();
        PurgeExpired();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            bool changed = PurgeExpired();
            changed |= EnforceLimits();
            if (changed) Changed?.Invoke();
            else foreach (var i in _items.Take(40)) i.RaiseTimeChanged();
        };
        _timer.Start();
        SettingsService.Changed += () => { if (EnforceLimits()) Changed?.Invoke(); };
    }

    public int Count => _items.Count;

    // ---------------- Adding ----------------

    public void AddCaptured(ClipItem item)
    {
        var s = SettingsService.Current;

        if (item.Type is ClipType.Text or ClipType.Link && SensitiveDetector.IsSensitive(item.Text))
        {
            if (s.SensitiveMode == SensitiveMode.Skip) return;
            if (s.SensitiveMode == SensitiveMode.AutoWipe)
            {
                item.Sensitive = true;
                item.ExpiresUtc = DateTime.UtcNow.AddSeconds(s.SensitiveWipeSeconds);
            }
        }

        if (s.MergeDuplicates)
        {
            var existing = _items.FirstOrDefault(i => i.Hash == item.Hash && i.Type == item.Type);
            if (existing != null)
            {
                if (item.Type == ClipType.Image) ImageStore.Delete(item.ImageFile);
                existing.CreatedUtc = DateTime.UtcNow;
                if (item.Sensitive && !existing.Pinned) existing.ExpiresUtc = item.ExpiresUtc;
                _items.Remove(existing);
                _items.Insert(0, existing);
                _repo.Update(existing);
                EnforceLimits();
                Changed?.Invoke();
                return;
            }
        }

        _repo.Insert(item);
        _items.Insert(0, item);
        EnforceLimits();
        Changed?.Invoke();
    }

    public ClipItem? AddNote(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;

        var bytes = Encoding.UTF8.GetBytes(text);
        var item = new ClipItem
        {
            Type = ClipType.Note,
            Text = text,
            Hash = "note:" + Crypto.Hash(bytes) + ":" + Guid.NewGuid().ToString("N"), // notes are never merged
            SizeBytes = bytes.Length,
            SourceApp = "Quick note",
            Pinned = SettingsService.Current.AutoPinNotes,
            CreatedUtc = DateTime.UtcNow
        };
        _repo.Insert(item);
        _items.Insert(0, item);
        EnforceLimits();
        Changed?.Invoke();
        return item;
    }

    // ---------------- Editing ----------------

    public void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        if (item.Pinned) item.ExpiresUtc = null;   // pinning rescues sensitive items from auto-wipe
        _repo.Update(item);
        EnforceLimits();
        Changed?.Invoke();
    }

    public void SetCollection(ClipItem item, string? name)
    {
        item.Collection = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (item.Collection != null && !item.Pinned) { item.Pinned = true; item.ExpiresUtc = null; }
        _repo.Update(item);
        Changed?.Invoke();
    }

    public void UpdateText(ClipItem item, string newText)
    {
        var bytes = Encoding.UTF8.GetBytes(newText);
        item.Text = newText;
        item.Html = null;
        item.SizeBytes = bytes.Length;
        if (item.Type != ClipType.Note) item.Hash = Crypto.Hash(bytes);
        _repo.Update(item);
        item.RaiseTextChanged();
        Changed?.Invoke();
    }

    public void RevealSensitive(ClipItem item)
    {
        item.Sensitive = false;
        _repo.Update(item);
        item.RaiseSensitiveChanged();
    }

    public void Delete(ClipItem item)
    {
        _items.Remove(item);
        _repo.Delete(item.Id);
        ImageStore.Delete(item.ImageFile);
        Changed?.Invoke();
    }

    public void ClearUnpinned()
    {
        foreach (var i in _items.Where(i => !i.Pinned).ToList())
        {
            ImageStore.Delete(i.ImageFile);
            _items.Remove(i);
        }
        _repo.DeleteAllUnpinned();
        Changed?.Invoke();
    }

    // ---------------- Queries ----------------

    public IReadOnlyList<string> Collections() =>
        _items.Where(i => !string.IsNullOrEmpty(i.Collection))
              .Select(i => i.Collection!)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
              .ToList();

    public IReadOnlyList<string> SourceApps() =>
        _items.Where(i => !string.IsNullOrEmpty(i.SourceApp))
              .Select(i => i.SourceApp!)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase)
              .ToList();

    /// <param name="typeFilter">null = all types</param>
    /// <param name="collection">null = any, "" = (none), otherwise exact name (Pinned tab only)</param>
    public List<ClipItem> Query(HistoryTab tab, string search, ClipType? typeFilter, string? sourceApp, string? collection)
    {
        var placement = SettingsService.Current.PinPlacement;
        IEnumerable<ClipItem> q = _items;

        if (tab == HistoryTab.Pinned)
            q = q.Where(i => i.Pinned);
        else if (placement == PinPlacement.SeparateTab)
            q = q.Where(i => !i.Pinned);

        if (typeFilter.HasValue) q = q.Where(i => i.Type == typeFilter.Value);
        if (!string.IsNullOrEmpty(sourceApp)) q = q.Where(i => string.Equals(i.SourceApp, sourceApp, StringComparison.OrdinalIgnoreCase));
        if (collection != null)
            q = collection.Length == 0 ? q.Where(i => string.IsNullOrEmpty(i.Collection))
                                       : q.Where(i => string.Equals(i.Collection, collection, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(i => !i.Sensitive &&
                ((i.Text?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                 (i.SourceApp?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                 (i.Collection?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)));
        }

        var list = q.OrderByDescending(i => i.CreatedUtc).ToList();

        // Pin placement inside the single History tab
        if (tab == HistoryTab.History && placement != PinPlacement.SeparateTab)
        {
            var pinned = list.Where(i => i.Pinned).ToList();
            var rest = list.Where(i => !i.Pinned).ToList();
            list = placement == PinPlacement.Top ? pinned.Concat(rest).ToList() : rest.Concat(pinned).ToList();
        }
        return list;
    }

    // ---------------- Limits ----------------

    private bool PurgeExpired()
    {
        var now = DateTime.UtcNow;
        var expired = _items.Where(i => !i.Pinned && i.ExpiresUtc.HasValue && i.ExpiresUtc.Value <= now).ToList();
        foreach (var i in expired) RemoveSilently(i);
        return expired.Count > 0;
    }

    /// <summary>Applies the temp-item limits. Pinned items are never counted and never removed.</summary>
    private bool EnforceLimits()
    {
        var s = SettingsService.Current;
        var temp = _items.Where(i => !i.Pinned).OrderByDescending(i => i.CreatedUtc).ToList();
        var remove = new List<ClipItem>();

        if (s.MaxTempAgeHours > 0)
        {
            var cutoff = DateTime.UtcNow.AddHours(-s.MaxTempAgeHours);
            remove.AddRange(temp.Where(i => i.CreatedUtc < cutoff));
            temp = temp.Except(remove).ToList();
        }
        if (s.MaxTempItems > 0 && temp.Count > s.MaxTempItems)
        {
            var extra = temp.Skip(s.MaxTempItems).ToList();
            remove.AddRange(extra);
            temp = temp.Take(s.MaxTempItems).ToList();
        }
        if (s.MaxTempMemoryMb > 0)
        {
            long limit = s.MaxTempMemoryMb * 1024L * 1024L;
            long total = 0;
            foreach (var i in temp)
            {
                total += i.SizeBytes;
                // always keep the newest item even if it alone exceeds the limit
                if (total > limit && i != temp[0]) remove.Add(i);
            }
        }

        foreach (var i in remove.Distinct()) RemoveSilently(i);
        return remove.Count > 0;
    }

    private void RemoveSilently(ClipItem item)
    {
        _items.Remove(item);
        _repo.Delete(item.Id);
        ImageStore.Delete(item.ImageFile);
    }

    /// <summary>Total size in bytes of non-pinned items (for the settings page).</summary>
    public long TempBytes() => _items.Where(i => !i.Pinned).Sum(i => i.SizeBytes);
    public int TempCount() => _items.Count(i => !i.Pinned);
    public int PinnedCount() => _items.Count(i => i.Pinned);
}
