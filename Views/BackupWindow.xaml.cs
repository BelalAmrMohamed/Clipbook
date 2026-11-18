using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Clipbook.Services;

namespace Clipbook;

public partial class BackupWindow : Window
{
    private readonly HistoryManager _history;
    private readonly bool _export;
    private string? _importPath;

    public BackupWindow(HistoryManager history, bool export)
    {
        InitializeComponent();
        _history = history;
        _export = export;

        HeadingText.Text = export ? "Export history" : "Import history";
        ActionBtn.Content = export ? "Export…" : "Import";
        ExportPanel.Visibility = export ? Visibility.Visible : Visibility.Collapsed;
        ImportPanel.Visibility = export ? Visibility.Collapsed : Visibility.Visible;
        ActionBtn.IsEnabled = export;   // import needs a file first
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (!_export) return;
        bool mismatch = PasswordBox1.Password != PasswordBox2.Password && PasswordBox2.Password.Length > 0;
        PasswordError.Text = mismatch ? "The two passwords don't match." : "";
        PasswordError.Visibility = mismatch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Clipbook backups (*.json;*.clipbook)|*.json;*.clipbook|All files (*.*)|*.*",
            Title = "Choose a Clipbook backup"
        };
        if (dlg.ShowDialog(this) != true) return;

        _importPath = dlg.FileName;
        PathBox.Text = _importPath;
        ImportPasswordPanel.Visibility = BackupService.IsEncrypted(_importPath) ? Visibility.Visible : Visibility.Collapsed;
        ActionBtn.IsEnabled = true;
        ShowResult("", false);
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_export) DoExport(); else DoImport();
    }

    private void DoExport()
    {
        var pw = PasswordBox1.Password;
        if (pw != PasswordBox2.Password)
        {
            ShowResult("The two passwords don't match.", true);
            return;
        }

        bool encrypted = pw.Length > 0;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Clipbook history",
            FileName = $"Clipbook-backup-{DateTime.Now:yyyy-MM-dd}",
            Filter = encrypted ? "Clipbook encrypted backup (*.clipbook)|*.clipbook" : "JSON file (*.json)|*.json",
            DefaultExt = encrypted ? ".clipbook" : ".json"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var options = new BackupService.ExportOptions(
                PinnedOnly: PinnedOnlyBox.IsChecked == true,
                IncludeImages: ImagesBox.IsChecked == true,
                IncludeSensitive: SensitiveBox.IsChecked == true,
                Password: encrypted ? pw : null);
            int count = BackupService.Export(_history.Snapshot(), dlg.FileName, options);
            ShowResult($"Exported {count} item(s) to {Path.GetFileName(dlg.FileName)}.", false);
            Finish();
        }
        catch (Exception ex)
        {
            AppPaths.Log("Export failed: " + ex);
            ShowResult("Export failed: " + ex.Message, true);
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private void DoImport()
    {
        if (string.IsNullOrEmpty(_importPath)) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var pw = ImportPasswordPanel.Visibility == Visibility.Visible ? ImportPasswordBox.Password : null;
            var result = BackupService.Import(_importPath, pw, _history);
            if (result.Error != null)
            {
                ShowResult(result.Error, true);
                return;
            }
            var msg = $"Imported {result.Added} item(s); skipped {result.Skipped} you already had.";
            if (result.Failed > 0) msg += $" {result.Failed} item(s) could not be read.";
            ShowResult(msg, false);
            Finish();
        }
        catch (Exception ex)
        {
            AppPaths.Log("Import failed: " + ex);
            ShowResult("Import failed: " + ex.Message, true);
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private void Finish()
    {
        ActionBtn.IsEnabled = false;
        CloseBtn.Content = "Close";
    }

    private void ShowResult(string text, bool error)
    {
        ResultText.Text = text;
        ResultText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResultText.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "AccentBrush");
    }
}
