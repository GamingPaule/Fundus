using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fundus.Models;
using Fundus.Services;

namespace Fundus.Controls;

/// <summary>"Dieser PC"-Startseite: Begrüßung, Schnellzugriff, Laufwerke, zuletzt verwendet.</summary>
public class HomePage : ScrollViewer
{
    readonly Action<string> _navigate, _open, _reveal;
    readonly StackPanel _root = new() { Margin = new Thickness(28, 20, 28, 28), MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _recent = new();

    static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    static readonly Brush Card = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2A));
    static readonly Brush CardHover = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x35));
    static readonly Brush CardLine = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38));
    static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // Bereiche der Startseite, per Rechtsklick ein-/ausschaltbar
    static readonly (string setting, string label)[] Parts =
    {
        ("HideQuick", "Schnellzugriff anzeigen"),
        ("HideDrives", "Laufwerke anzeigen"),
        ("HideRecent", "Zuletzt verwendet anzeigen"),
    };

    /// <summary>Rechtsklick-Menü für "Dieser PC" (Startseite und Seitenleiste).</summary>
    public static ContextMenu OptionsMenu(Action changed)
    {
        var m = new ContextMenu();
        foreach (var (setting, label) in Parts)
        {
            var mi = new MenuItem { Header = label, IsChecked = !Settings.GetBool(setting) };
            mi.Click += (s, e) => { Settings.SetBool(setting, !Settings.GetBool(setting)); changed(); };
            m.Items.Add(mi);
        }
        // Häkchen beim Öffnen aktualisieren (Menü wird wiederverwendet)
        m.Opened += (s, e) =>
        {
            for (int i = 0; i < Parts.Length; i++) ((MenuItem)m.Items[i]).IsChecked = !Settings.GetBool(Parts[i].setting);
        };
        return m;
    }

    public HomePage(IEnumerable<FsItem> quick, Action<string> navigate, Action<string> open, Action<string> reveal, Action changed)
    {
        _navigate = navigate; _open = open; _reveal = reveal;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Content = _root;
        Focusable = true;
        Background = Brushes.Transparent; // damit Rechtsklick auch auf freier Fläche greift
        ContextMenu = OptionsMenu(changed);

        int h = DateTime.Now.Hour;
        string hello = h < 11 ? "Guten Morgen" : h < 18 ? "Hallo" : "Guten Abend";
        _root.Children.Add(new TextBlock { Text = $"{hello}, {Environment.UserName}", FontSize = 26, FontWeight = FontWeights.SemiBold });
        _root.Children.Add(new TextBlock { Text = DateTime.Now.ToString("dddd, d. MMMM yyyy"), Opacity = 0.55, Margin = new Thickness(0, 2, 0, 8) });

        if (!Settings.GetBool("HideQuick"))
        {
            Section("", "Schnellzugriff");
            var qp = new WrapPanel();
            foreach (var q in quick) qp.Children.Add(QuickTile(q));
            _root.Children.Add(qp);
        }

        if (!Settings.GetBool("HideDrives"))
        {
            Section("", "Laufwerke");
            var dp = new WrapPanel();
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady)) dp.Children.Add(DriveCard(d));
            _root.Children.Add(dp);
        }

        if (Settings.GetBool("HideRecent")) return;
        var header = Section("", "Zuletzt verwendet");
        _recent.Children.Add(new TextBlock { Text = "Wird geladen …", Opacity = 0.5, Margin = new Thickness(4) });
        var box = new Border { Background = Card, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Child = _recent };
        _root.Children.Add(box);
        MakeCollapsible(header, box, "RecentCollapsed");
        _ = LoadRecent();
    }

    StackPanel Section(string glyph, string title)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 10), Background = Brushes.Transparent };
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 15, Foreground = B("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        sp.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        _root.Children.Add(sp);
        return sp;
    }

    /// <summary>Überschrift anklickbar machen: Bereich ein-/ausklappen, Zustand bleibt gespeichert.</summary>
    static void MakeCollapsible(StackPanel header, UIElement content, string setting)
    {
        var chevron = new TextBlock { FontFamily = Icons, FontSize = 10, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 2, 0, 0) };
        header.Children.Add(chevron);
        header.Cursor = Cursors.Hand;
        header.ToolTip = "Ein-/ausklappen";

        void Apply(bool collapsed)
        {
            content.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            chevron.Text = collapsed ? "" : ""; // › zu, ⌄ offen
        }
        Apply(Settings.GetBool(setting));
        header.MouseLeftButtonUp += (s, e) =>
        {
            bool collapsed = content.Visibility == Visibility.Visible;
            Settings.SetBool(setting, collapsed);
            Apply(collapsed);
        };
    }

    /// <summary>Klickbare Karte mit Hover-Effekt.</summary>
    Border CardBox(UIElement child, double w, double h, Action click, string tip)
    {
        var b = new Border
        {
            Child = child, Width = w, Height = h, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 10, 14, 10),
            Background = Card, BorderBrush = CardLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand, ToolTip = tip,
        };
        b.MouseEnter += (s, e) => b.Background = CardHover;
        b.MouseLeave += (s, e) => b.Background = Card;
        Activate(b, click);
        return b;
    }

    UIElement QuickTile(FsItem q)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new Image { Source = ShellIcons.Get(q.Path, true, large: true), Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = q.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = ShortPath(q.Path), FontSize = 11.5, Opacity = 0.5, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        return CardBox(g, 220, 64, () => _navigate(q.Path), q.Path);
    }

    UIElement DriveCard(DriveInfo d)
    {
        var item = FsItem.FromDrive(d);
        double used = item.DriveUsed / 100;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new Image { Source = ShellIcons.Get(item.Path, true, large: true), Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Left });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new ProgressBar
        {
            Value = used * 100, Height = 6, Margin = new Thickness(0, 7, 0, 6), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x46)),
            Foreground = used > 0.9 ? new SolidColorBrush(Color.FromRgb(0xE0, 0x4F, 0x4F)) : B("Accent"),
        });
        text.Children.Add(new TextBlock { Text = item.SizeText + $"  ·  {used:P0} belegt", FontSize = 11.5, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        return CardBox(g, 320, 84, () => _navigate(item.Path), item.TypeText);
    }

    async Task LoadRecent()
    {
        var entries = await Task.Run(() => RecentFiles.Get(15));
        _recent.Children.Clear();
        if (entries.Count == 0)
            _recent.Children.Add(new TextBlock { Text = "Noch keine zuletzt verwendeten Dateien.", Opacity = 0.5, Margin = new Thickness(10) });
        foreach (var e in entries) _recent.Children.Add(RecentRow(e));
    }

    UIElement RecentRow(RecentFiles.Entry e)
    {
        var g = new Grid { Height = 40 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        g.Children.Add(new Image { Source = ShellIcons.Get(e.Path, false), Width = 16, Height = 16 });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Path.GetFileName(e.Path), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = ShortPath(Path.GetDirectoryName(e.Path)), FontSize = 11, Opacity = 0.5, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        var when = new TextBlock { Text = Relative(e.Used), Opacity = 0.55, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 12, 0) };
        Grid.SetColumn(when, 2);
        g.Children.Add(when);

        var folder = new Button { Style = (Style)Application.Current.FindResource("ToolButton"), Content = "", ToolTip = "Im Ordner zeigen" };
        folder.Click += (s, a) => { a.Handled = true; _reveal(e.Path); };
        Grid.SetColumn(folder, 3);
        g.Children.Add(folder);

        var row = new Border { Child = g, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 0, 4, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = e.Path };
        row.MouseEnter += (s, a) => row.Background = CardHover;
        row.MouseLeave += (s, a) => row.Background = Brushes.Transparent;
        Activate(row, () => _open(e.Path));
        return row;
    }

    /// <summary>
    /// Klick = Maus runter UND hoch auf demselben Element. Ausführung erst nach dem Klick,
    /// sonst landet der Mausklick zusätzlich in der neu aufgebauten Ansicht darunter.
    /// </summary>
    void Activate(Border el, Action act)
    {
        bool down = false;
        el.MouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInButton(d)) return;
            down = true;
            el.CaptureMouse();
            e.Handled = true;
        };
        el.MouseLeftButtonUp += (s, e) =>
        {
            if (!down) return;
            down = false;
            el.ReleaseMouseCapture();
            e.Handled = true;
            if (el.IsMouseOver) Dispatcher.InvokeAsync(act, System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    static bool IsInButton(DependencyObject d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : null)
            if (d is Button) return true;
        return false;
    }

    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string ShortPath(string p) =>
        p != null && p.StartsWith(Home, StringComparison.OrdinalIgnoreCase) ? "~" + p[Home.Length..] : p;

    static string Relative(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "gerade eben";
        if (d.TotalHours < 1) return $"vor {(int)d.TotalMinutes} Min.";
        if (t.Date == DateTime.Today) return "heute, " + t.ToString("HH:mm");
        if (t.Date == DateTime.Today.AddDays(-1)) return "gestern, " + t.ToString("HH:mm");
        if (d.TotalDays < 7) return t.ToString("dddd, HH:mm");
        return t.ToString("dd.MM.yyyy");
    }
}
