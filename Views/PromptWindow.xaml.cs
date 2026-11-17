using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AdvancedClipboard;

public partial class PromptWindow : Window
{
    private readonly bool _multiline;
    public string? Result { get; private set; }

    public PromptWindow(string prompt, string initial, IEnumerable<string>? suggestions, string title, bool multiline)
    {
        InitializeComponent();
        _multiline = multiline;
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;

        if (multiline)
        {
            Input.AcceptsReturn = true;
            Input.TextWrapping = TextWrapping.Wrap;
            Input.Height = 180;
            Input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Width = 520;
        }

        var list = suggestions?.ToList();
        if (list is { Count: > 0 })
        {
            Suggestions.Visibility = Visibility.Visible;
            foreach (var s in list)
            {
                var name = s;
                var b = new Button { Content = name, Style = (Style)FindResource("OutlineButton"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3) };
                b.Click += (_, _) => { Input.Text = name; Input.Focus(); Input.CaretIndex = Input.Text.Length; };
                Suggestions.Children.Add(b);
            }
        }
        Loaded += (_, _) => { Input.Focus(); if (!multiline) Input.SelectAll(); };
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // Single line: Enter confirms. Multi-line: Ctrl+Enter confirms, Enter adds a line.
        if (!_multiline || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Ok_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = _multiline ? Input.Text : Input.Text.Trim();
        DialogResult = true;
    }

    public static string? Ask(string prompt, string initial = "", IEnumerable<string>? suggestions = null,
                              string title = "Advanced Clipboard", bool multiline = false)
    {
        var w = new PromptWindow(prompt, initial, suggestions, title, multiline);
        return w.ShowDialog() == true ? w.Result : null;
    }
}
