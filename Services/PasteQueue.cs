using System;
using System.Collections.Generic;
using Clipbook.Models;

namespace Clipbook.Services;

/// <summary>
/// Multi-paste queue: items are pasted one after another (first in, first out).
/// While "collecting" is on, everything you copy is appended to the queue automatically.
/// </summary>
public sealed class PasteQueue
{
    private readonly List<ClipItem> _items = new();

    public bool Collecting { get; private set; }
    public IReadOnlyList<ClipItem> Items => _items;
    public int Count => _items.Count;
    public event Action? Changed;

    public PasteQueue(HistoryManager history)
    {
        history.ItemCaptured += item => { if (Collecting) Add(item); };
        history.ItemRemoved += Remove;
    }

    public void StartCollecting() { Collecting = true; Changed?.Invoke(); }
    public void StopCollecting() { Collecting = false; Changed?.Invoke(); }
    public void ToggleCollecting() { if (Collecting) StopCollecting(); else StartCollecting(); }

    public void Add(ClipItem item)
    {
        if (_items.Contains(item)) return;
        _items.Add(item);
        Renumber();
    }

    public void Remove(ClipItem item)
    {
        if (!_items.Remove(item)) return;
        item.QueuePosition = 0;
        Renumber();
    }

    public ClipItem? Peek() => _items.Count > 0 ? _items[0] : null;

    public ClipItem? Dequeue()
    {
        if (_items.Count == 0) return null;
        var item = _items[0];
        _items.RemoveAt(0);
        item.QueuePosition = 0;
        Renumber();
        return item;
    }

    /// <summary>Takes every queued item out of the queue, in order.</summary>
    public List<ClipItem> DequeueAll()
    {
        var all = new List<ClipItem>(_items);
        foreach (var i in all) i.QueuePosition = 0;
        _items.Clear();
        Renumber();
        return all;
    }

    public void Clear()
    {
        foreach (var i in _items) i.QueuePosition = 0;
        _items.Clear();
        Renumber();
    }

    private void Renumber()
    {
        for (int i = 0; i < _items.Count; i++) _items[i].QueuePosition = i + 1;
        Changed?.Invoke();
    }

    public static string Preview(ClipItem item)
    {
        if (item.IsImage) return "[image]";
        var t = (item.Sensitive ? "(hidden)" : item.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length > 28 ? t.Substring(0, 28) + "…" : t;
    }
}
