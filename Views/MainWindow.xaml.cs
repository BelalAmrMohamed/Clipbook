using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AdvancedClipboard.Models;
using AdvancedClipboard.Native;
using AdvancedClipboard.Services;

namespace AdvancedClipboard;

public partial class MainWindow : Window
{
    private readonly HistoryManager _history;
    private readonly PasteService _paste;
    private readonly ClipboardMonitor _monitor;
    private readonly Action _openSettings;

    private bool _ready;
    private bool _busy;                 // true while a dialog/menu is open, so we don't auto-hide
    private HistoryTab _tab = HistoryTab.History;
    private ClipType? _typeFilter;
    private string? _appFilter;
    private string? _collectionFilter;   // null = any, "" = none

    public MainWindow(HistoryManager history, PasteService paste, ClipboardMonitor monitor, Action openSettings)
    {
        InitializeComponent();
        _history = history;
        _paste = paste;
        _monitor = monitor;
        _openSettings = openSettings;
        _ready = true;

        _history.Changed += () => { if (IsVisible) Refresh(); };
        SettingsService.Changed += ApplyPlacementSettings;
        ApplyPlacementSettings();
        Refresh();
    }

    // ---------------- Show / hide ----------------

    public void ShowPanel()
    {
        ApplyPlacementSettings();
        Refresh();
        PositionNearCursor();
        Show();
        Activate();
        Focus();
        NoteBox.Focus();
        if (ItemsList.Items.Count > 0 && ItemsList.SelectedIndex < 0) ItemsList.SelectedIndex = 0;
    }

    public void HidePanel()
    {
        if (!IsVisible) return;
        SearchBox.Clear();
        Hide();
    }

    private void PositionNearCursor()
    {
        NativeMethods.GetCursorPos(out var p);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
        var wa = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

        double w = Width * sx, h = Height * sy;
        double left = p.X - w / 2;
        double top = p.Y - h - 16 * sy;
        if (top < wa.Top) top = p.Y + 16 * sy;
        left = Math.Max(wa.Left + 8, Math.Min(left, wa.Right - w - 8));
        top = Math.Max(wa.Top + 8, Math.Min(top, wa.Bottom - h - 8));
        Left = left / sx;
        Top = top / sy;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_busy) return;
        Dispatcher.BeginInvoke(new Action(() => { if (!_busy && !IsActive) HidePanel(); }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text) && SearchBox.IsKeyboardFocused) SearchBox.Clear();
            else HidePanel();
            e.Handled = true;
        }
    }

    private void ApplyPlacementSettings()
    {
        bool separate = SettingsService.Current.PinPlacement == PinPlacement.SeparateTab;
        PinnedTabBtn.Visibility = separate ? Visibility.Visible : Visibility.Collapsed;
        if (!separate && _tab == HistoryTab.Pinned)
        {
            _tab = HistoryTab.History;
            HistoryTabBtn.IsChecked = true;
        }
    }

    // ---------------- Data ----------------

    private void Refresh()
    {
        if (!_ready) return;
        var selectedId = (ItemsList.SelectedItem as ClipItem)?.Id;

        var list = _history.Query(_tab, SearchBox.Text, _typeFilter, _appFilter, _collectionFilter);
        ItemsList.ItemsSource = list;
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selectedId.HasValue)
        {
            var again = list.FirstOrDefault(i => i.Id == selectedId.Value);
            if (again != null) ItemsList.SelectedItem = again;
        }

        AppFilterBtn.Content = _appFilter == null ? "App ▾" : $"App: {_appFilter} ▾";
        CollectionFilterBtn.Content = _collectionFilter == null ? "Collection ▾"
            : _collectionFilter.Length == 0 ? "Collection: none ▾" : $"Collection: {_collectionFilter} ▾";
        CollectionFilterBtn.Visibility = _history.Collections().Count > 0 || _collectionFilter != null
            ? Visibility.Visible : Visibility.Collapsed;

        UpdateFooter(list.Count);
    }

    private void UpdateFooter(int shown)
    {
        if (_paste.TargetFolder != null)
            FooterText.Text = $"🖼  Clicking an image saves it as a file in:  {_paste.TargetFolder}";
        else
            FooterText.Text = $"{shown} item{(shown == 1 ? "" : "s")}  •  {_history.PinnedCount()} pinned";
    }

    // ---------------- Quick note ----------------

    private void SaveNote_Click(object sender, RoutedEventArgs e) => SaveNote();

    private void NoteBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SaveNote();
            e.Handled = true;
        }
    }

    private void SaveNote()
    {
        var text = NoteBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        _history.AddNote(text);
        NoteBox.Clear();
        if (ItemsList.Items.Count > 0) ItemsList.ScrollIntoView(ItemsList.Items[0]);
        NoteBox.Focus();
    }

    // ---------------- Search / filters / tabs ----------------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && ItemsList.Items.Count > 0)
        {
            if (ItemsList.SelectedIndex < 0) ItemsList.SelectedIndex = 0;
            FocusSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ItemsList.Items.Count > 0)
        {
            var item = ItemsList.SelectedItem as ClipItem ?? ItemsList.Items[0] as ClipItem;
            if (item != null) ActivateItem(item);
            e.Handled = true;
        }
    }

    private void TypeChip_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton rb) return;
        var tag = rb.Tag as string ?? "";
        _typeFilter = tag.Length == 0 ? null : Enum.Parse<ClipType>(tag);
        Refresh();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _tab = ReferenceEquals(sender, PinnedTabBtn) ? HistoryTab.Pinned : HistoryTab.History;
        Refresh();
    }

    private void AppFilter_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu();
        menu.Items.Add(MakeItem("All apps", () => { _appFilter = null; Refresh(); }, _appFilter == null));
        menu.Items.Add(new Separator());
        foreach (var app in _history.SourceApps())
        {
            var a = app;
            menu.Items.Add(MakeItem(a, () => { _appFilter = a; Refresh(); }, string.Equals(_appFilter, a, StringComparison.OrdinalIgnoreCase)));
        }
        OpenMenu(menu, (FrameworkElement)sender);
    }

    private void CollectionFilter_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu();
        menu.Items.Add(MakeItem("All collections", () => { _collectionFilter = null; Refresh(); }, _collectionFilter == null));
        menu.Items.Add(MakeItem("(No collection)", () => { _collectionFilter = ""; Refresh(); }, _collectionFilter == ""));
        menu.Items.Add(new Separator());
        foreach (var c in _history.Collections())
        {
            var name = c;
            menu.Items.Add(MakeItem("📂 " + name, () => { _collectionFilter = name; Refresh(); },
                string.Equals(_collectionFilter, name, StringComparison.OrdinalIgnoreCase)));
        }
        OpenMenu(menu, (FrameworkElement)sender);
    }

    // ---------------- Item interaction ----------------

    private void ItemsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemFromEvent(e.OriginalSource as DependencyObject);
        if (item != null) ActivateItem(item);
    }

    private void ItemsList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemFromEvent(e.OriginalSource as DependencyObject);
        if (item == null) return;
        ItemsList.SelectedItem = item;
        ShowItemMenu(item, ItemsList);
        e.Handled = true;
    }

    private void ItemsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ItemsList.SelectedItem is not ClipItem item) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (e.Key == Key.Enter) { ActivateItem(item, plain: shift); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteAndKeepSelection(item); e.Handled = true; }
        else if (e.Key == Key.P && ctrl) { _history.TogglePin(item); e.Handled = true; }
        else if (e.Key == Key.C && ctrl) { _paste.CopyOnly(item); e.Handled = true; }
        else if (e.Key == Key.Up && ItemsList.SelectedIndex == 0) { SearchBox.Focus(); e.Handled = true; }
    }

    private static ClipItem? ItemFromEvent(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ListBoxItem lbi) return lbi.DataContext as ClipItem;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private void FocusSelected()
    {
        if (ItemsList.SelectedItem == null) return;
        ItemsList.ScrollIntoView(ItemsList.SelectedItem);
        (ItemsList.ItemContainerGenerator.ContainerFromItem(ItemsList.SelectedItem) as ListBoxItem)?.Focus();
    }

    private void DeleteAndKeepSelection(ClipItem item)
    {
        int index = ItemsList.SelectedIndex;
        _history.Delete(item);
        if (ItemsList.Items.Count > 0)
        {
            ItemsList.SelectedIndex = Math.Min(index, ItemsList.Items.Count - 1);
            FocusSelected();
        }
    }

    private async void ActivateItem(ClipItem item, bool plain = false)
    {
        HidePanel();
        try { await _paste.ActivateAsync(item, plain); }
        catch (Exception ex) { AppPaths.Log("Activate failed: " + ex); }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item }) _history.TogglePin(item);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item } fe) ShowItemMenu(item, fe);
    }

    // ---------------- Buttons ----------------

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        HidePanel();
        _openSettings();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _busy = true;
        try
        {
            var r = System.Windows.MessageBox.Show(this,
                "Delete all history that is not pinned?\n\nPinned items are kept.",
                "Clear clipboard history", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) _history.ClearUnpinned();
        }
        finally { _busy = false; Activate(); }
    }

    // ---------------- Item context menu ----------------

    private void ShowItemMenu(ClipItem item, FrameworkElement target)
    {
        var menu = NewMenu();

        menu.Items.Add(MakeItem("Paste", () => ActivateItem(item)));
        if (item.IsTextual && item.Type != ClipType.Files)
            menu.Items.Add(MakeItem("Paste as plain text", () => ActivateItem(item, plain: true)));
        menu.Items.Add(MakeItem("Copy", () => _paste.CopyOnly(item)));

        if (item.IsImage && _paste.TargetFolder != null)
            menu.Items.Add(MakeItem("Save as file in current folder", () =>
            {
                var saved = _paste.SaveImageToFolder(item, _paste.TargetFolder!);
                if (saved != null) FooterText.Text = "Saved " + Path.GetFileName(saved);
            }));
        if (item.IsImage)
            menu.Items.Add(MakeItem("Save image as…", () => SaveImageAs(item)));
        if (item.Type == ClipType.Link)
            menu.Items.Add(MakeItem("Open in browser", () => OpenLink(item)));

        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(item.Pinned ? "Unpin" : "Pin", () => _history.TogglePin(item)));
        menu.Items.Add(MakeItem("Add to collection…", () => AssignCollection(item)));
        if (!string.IsNullOrEmpty(item.Collection))
            menu.Items.Add(MakeItem("Remove from collection", () => _history.SetCollection(item, null)));

        if (item.IsTextual && item.Type != ClipType.Files)
        {
            menu.Items.Add(MakeItem("Edit…", () => EditItem(item)));

            var tools = new MenuItem { Header = "Text tools (creates a new item)" };
            foreach (var (name, apply) in TextTools.All)
            {
                var n = name; var f = apply;
                tools.Items.Add(MakeItem(n, () => RunTextTool(item, n, f)));
            }
            menu.Items.Add(tools);
        }

        if (item.Sensitive)
            menu.Items.Add(MakeItem("Show content", () => _history.RevealSensitive(item)));

        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Delete", () => DeleteAndKeepSelection(item)));

        OpenMenu(menu, target);
    }

    private ContextMenu NewMenu() => new() { StaysOpen = false };

    private static MenuItem MakeItem(string header, Action action, bool isChecked = false)
    {
        var mi = new MenuItem { Header = header, IsChecked = isChecked };
        mi.Click += (_, _) => action();
        return mi;
    }

    private void OpenMenu(ContextMenu menu, FrameworkElement target)
    {
        _busy = true;
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(new Action(() => _busy = false));
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---------------- Menu actions ----------------

    private void AssignCollection(ClipItem item)
    {
        _busy = true;
        try
        {
            var name = PromptWindow.Ask("Collection name (items in a collection are pinned automatically):",
                item.Collection ?? "", _history.Collections(), "Add to collection");
            if (name != null) _history.SetCollection(item, name);
        }
        finally { _busy = false; Activate(); }
    }

    private void EditItem(ClipItem item)
    {
        _busy = true;
        try
        {
            var text = PromptWindow.Ask("Edit text (Ctrl+Enter to save):", item.Text ?? "", null, "Edit item", multiline: true);
            if (text != null && text != item.Text) _history.UpdateText(item, text);
        }
        finally { _busy = false; Activate(); }
    }

    private void RunTextTool(ClipItem item, string name, Func<string, string> apply)
    {
        var source = item.Text ?? "";
        var result = apply(source);
        if (result == source) { FooterText.Text = $"“{name}” changed nothing."; return; }

        var bytes = System.Text.Encoding.UTF8.GetBytes(result);
        _history.AddCaptured(new ClipItem
        {
            Type = ClipType.Text,
            Text = result,
            SourceApp = "Text tool",
            Hash = "txt:" + Crypto.Hash(bytes),
            SizeBytes = bytes.Length,
            CreatedUtc = DateTime.UtcNow
        });
    }

    private void SaveImageAs(ClipItem item)
    {
        _busy = true;
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG image (*.png)|*.png",
                FileName = $"Clipboard_{DateTime.Now:yyyy-MM-dd_HHmmss}.png"
            };
            if (dlg.ShowDialog(this) == true)
            {
                var png = ImageStore.LoadBytes(item.ImageFile);
                if (png != null) File.WriteAllBytes(dlg.FileName, png);
            }
        }
        catch (Exception ex) { AppPaths.Log("Save image failed: " + ex.Message); }
        finally { _busy = false; Activate(); }
    }

    private static void OpenLink(ClipItem item)
    {
        try
        {
            var url = (item.Text ?? "").Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https" or "ftp"))
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex) { AppPaths.Log("Open link failed: " + ex.Message); }
    }
}
