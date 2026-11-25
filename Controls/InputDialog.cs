using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Fundus.Controls;

/// <summary>Kleiner dunkler Eingabedialog (Umbenennen, Neuer Ordner).</summary>
public class InputDialog : Window
{
    readonly TextBox _box;
    public string Value => _box.Text.Trim();

    InputDialog(Window owner, string title, string value, bool selectStem)
    {
        Owner = owner;
        Title = title;
        Width = 380; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgToolbar");
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Fg");
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("UiFont");

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _box = new TextBox { Text = value, Style = (Style)Application.Current.FindResource("SearchBox"), Height = 30 };
        sp.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Abbrechen", Width = 80, IsCancel = true };
        ok.Click += (s, e) => { DialogResult = Value.Length > 0 && Value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        sp.Children.Add(buttons);
        Content = sp;

        Loaded += (s, e) =>
        {
            _box.Focus();
            // Wie im Finder: nur den Namen ohne Endung markieren
            int dot = value.LastIndexOf('.');
            if (selectStem && dot > 0) _box.Select(0, dot); else _box.SelectAll();
        };
        PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    public static string Ask(Window owner, string title, string value, bool selectStem = false)
    {
        var d = new InputDialog(owner, title, value, selectStem);
        return d.ShowDialog() == true ? d.Value : null;
    }
}
