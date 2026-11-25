using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fundus.Controls;
using Fundus.Services;

namespace Fundus;

public partial class MainWindow
{
    void UpdateChrome()
    {
        BackBtn.IsEnabled = _histPos > 0;
        FwdBtn.IsEnabled = _histPos < _history.Count - 1;
        string dir = CurrentDir;
        UpBtn.IsEnabled = dir != ColumnView.ThisPc;
        Title = DisplayName(dir) + " – Fundus";
        SearchHint.Text = "Suchen in „" + DisplayName(dir) + "“";

        // Adressleiste: kompletter Pfad bis zum tiefsten ausgewählten Ordner
        BuildPathBar(dir);
        AddressIcon.Source = ShellIcons.Get(dir, true);

        UpdateStatus();
        if (_tabs.Count > 0) RenderTabs();
        UpdateCommandStates();
    }

    void UpdateStatus()
    {
        var last = Last;
        if (last == null) { StatusText.Text = ""; return; }
        var parts = new List<string> { $"{last.Items.Count} Elemente" };

        // Ordner-Auswahl in linken Spalten ist nur Navigation → nur die tiefste Spalte zählt
        var sel = last.SelectedAll;
        if (sel.Count > 0)
        {
            long size = sel.Where(i => !i.IsDir).Sum(i => i.Size);
            parts.Add(sel.Count == 1 ? "1 Element ausgewählt" : $"{sel.Count} Elemente ausgewählt");
            if (size > 0) parts[^1] += ", " + Preview.FormatSize(size);
        }
        try
        {
            if (last.DirPath != ColumnView.ThisPc)
            {
                var drive = new DriveInfo(Path.GetPathRoot(last.DirPath));
                parts.Add($"{Preview.FormatSize(drive.AvailableFreeSpace)} frei auf {drive.Name.TrimEnd('\\')}");
            }
        }
        catch { }
        StatusText.Text = string.Join("     |     ", parts);
    }

    static string DisplayName(string p) =>
        p == ColumnView.ThisPc ? "Dieser PC" : Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p;

    void BuildPathBar(string full)
    {
        PathBar.Children.Clear();
        var parts = new List<string>();
        if (full != ColumnView.ThisPc)
            for (var d = full; d != null; d = Path.GetDirectoryName(d)) parts.Insert(0, d);
        AddCrumb(ColumnView.ThisPc, "Dieser PC", parts.Count > 0);
        for (int i = 0; i < parts.Count; i++)
            AddCrumb(parts[i], DisplayName(parts[i]), i < parts.Count - 1);
        Dispatcher.InvokeAsync(() => CrumbScroller.ScrollToRightEnd(), System.Windows.Threading.DispatcherPriority.Background);
    }

    void AddCrumb(string path, string name, bool arrow)
    {
        var b = new Button { Style = (Style)FindResource("CrumbButton"), Content = name, ToolTip = path == ColumnView.ThisPc ? null : path };
        b.Click += async (s, e) => await Reveal(path);
        PathBar.Children.Add(b);
        if (!arrow) return;

        // Pfeil öffnet wie im Explorer die Unterordner dieses Ordners
        var chevron = new Button
        {
            Style = (Style)FindResource("CrumbButton"), Content = "", Padding = new Thickness(3, 0, 3, 0),
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 8, Opacity = 0.6,
        };
        chevron.Click += (s, e) => ShowCrumbMenu(chevron, path);
        PathBar.Children.Add(chevron);
    }

    void ShowCrumbMenu(Button anchor, string path)
    {
        var m = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        IEnumerable<(string name, string p)> dirs;
        try
        {
            dirs = path == ColumnView.ThisPc
                ? DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => (d.Name, d.RootDirectory.FullName))
                : new DirectoryInfo(path).EnumerateDirectories()
                    .Where(d => _showHidden || !d.Attributes.HasFlag(FileAttributes.Hidden))
                    .OrderBy(d => d.Name).Take(60).Select(d => (d.Name, d.FullName)).ToList();
        }
        catch { return; }
        foreach (var (name, p) in dirs)
        {
            var mi = new MenuItem { Header = name, Icon = new Image { Source = ShellIcons.Get(p, true), Width = 16, Height = 16 } };
            mi.Click += async (s, e) => await Reveal(p);
            m.Items.Add(mi);
        }
        m.IsOpen = true;
    }

    // Klick in die freie Fläche der Adressleiste → Pfad eintippen
    void AddressFrame_Click(object s, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
        ShowAddressBox();
    }

    static T FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    async Task ReloadAll()
    {
        foreach (var c in _columns.ToList()) await c.LoadAsync(_showHidden);
        UpdateChrome();
    }

    // ---------- Ansicht-Umschalter unten rechts ----------

    void BuildViewSwitch()
    {
        ViewSwitch.Children.Clear();
        foreach (var (mode, glyph, tip) in new[]
                 {
                     (ViewMode.Columns, "", "Spalten (Strg+1)"),
                     (ViewMode.Details, "", "Details (Strg+2)"),
                     (ViewMode.Icons, "", "Symbole (Strg+3)"),
                 })
        {
            var b = new Button
            {
                Style = (Style)FindResource("ToolButton"), Content = glyph, ToolTip = tip, FontSize = 12, Width = 28, Height = 22,
                Background = Brushes.Transparent, Opacity = _view == mode ? 1 : 0.45,
            };
            b.Click += async (s, e) => await SetView(mode);
            ViewSwitch.Children.Add(b);
        }
    }
}
