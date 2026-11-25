using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fundus.Controls;
using Fundus.Models;
using Fundus.Services;

namespace Fundus;

public partial class MainWindow
{
    readonly List<FsItem> _favorites = new();
    readonly List<ListBox> _sideLists = new();

    static readonly string FavFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fundus", "favoriten.txt");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out string path);

    static string Downloads()
    {
        var g = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        return SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out var p) == 0 ? p : null;
    }

    void BuildSidebar()
    {
        Sidebar.Children.Clear();
        _sideLists.Clear();
        _favorites.Clear();

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var std = new (string name, string path)[]
        {
            (Environment.UserName, home),
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Downloads", Downloads()),
            ("Dokumente", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Bilder", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Musik", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
        };
        foreach (var (name, path) in std)
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                _favorites.Add(new FsItem { Name = name, Path = path, IsDir = true });

        foreach (var p in LoadPinned())
            _favorites.Add(new FsItem { Name = Path.GetFileName(p.TrimEnd('\\')), Path = p, IsDir = true });

        AddSection("Favoriten", _favorites, removable: true);

        var places = new List<FsItem> { new() { Name = "Dieser PC", Path = ColumnView.ThisPc, IsDir = true } };
        AddSection("Orte", places, false);
        AddDrives();
    }

    void AddSection(string title, List<FsItem> items, bool removable)
    {
        Sidebar.Children.Add(new TextBlock
        {
            Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.5, Margin = new Thickness(14, 10, 0, 4),
        });
        var lb = new ListBox
        {
            Style = (Style)FindResource("PlainList"),
            ItemTemplate = (DataTemplate)FindResource("SideRow"),
            ItemsSource = items,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
        lb.SelectionChanged += async (s, e) =>
        {
            if (lb.SelectedItem is not FsItem it) return;
            foreach (var other in _sideLists) if (other != lb) other.SelectedItem = null;
            _base = it.Path;
            await NavigateTo(it.Path);
        };
        if (removable)
        {
            lb.ContextMenu = new ContextMenu();
            var rm = new MenuItem { Header = "Aus Favoriten entfernen" };
            rm.Click += (s, e) => { if (lb.SelectedItem is FsItem it) Unpin(it.Path); };
            lb.ContextMenu.Items.Add(rm);
            lb.AllowDrop = true;
            lb.Drop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                    foreach (var f in files.Where(Directory.Exists)) Pin(f);
                e.Handled = true;
            };
        }
        // Rechtsklick auf "Dieser PC": Bereiche der Startseite an/aus
        if (items.Any(i => i.Path == ColumnView.ThisPc)) lb.ContextMenu = HomePage.OptionsMenu(RefreshHome);
        _sideLists.Add(lb);
        Sidebar.Children.Add(lb);
    }

    void AddDrives()
    {
        Sidebar.Children.Add(new TextBlock
        {
            Text = "Laufwerke", FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.5, Margin = new Thickness(14, 10, 0, 4),
        });
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        var lb = new ListBox { Style = (Style)FindResource("PlainList") };
        ScrollViewer.SetVerticalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
        foreach (var d in drives)
        {
            var item = FsItem.FromDrive(d);
            double used = d.TotalSize > 0 ? 1 - (double)d.AvailableFreeSpace / d.TotalSize : 0;
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 3) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Image { Source = item.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(row);
            sp.Children.Add(new ProgressBar
            {
                Value = used * 100, Height = 3, Margin = new Thickness(22, 4, 4, 0), BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x48)),
                Foreground = used > 0.9 ? Brushes.IndianRed : (Brush)FindResource("Accent"),
            });
            sp.ToolTip = $"{Preview.FormatSize(d.AvailableFreeSpace)} frei von {Preview.FormatSize(d.TotalSize)}";
            lb.Items.Add(new ListBoxItem { Content = sp, Tag = item, Style = (Style)FindResource("RowItem") });
        }
        lb.SelectionChanged += async (s, e) =>
        {
            if ((lb.SelectedItem as ListBoxItem)?.Tag is not FsItem it) return;
            foreach (var other in _sideLists) if (other != lb) other.SelectedItem = null;
            _base = ColumnView.ThisPc;
            await NavigateTo(it.Path);
        };
        _sideLists.Add(lb);
        Sidebar.Children.Add(lb);
    }

    static List<string> LoadPinned()
    {
        try { return File.Exists(FavFile) ? File.ReadAllLines(FavFile).Where(Directory.Exists).ToList() : new(); }
        catch { return new(); }
    }

    void SavePinned(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FavFile));
        File.WriteAllLines(FavFile, paths);
    }

    public void Pin(string path)
    {
        var list = LoadPinned();
        if (list.Any(p => Same(p, path))) return;
        list.Add(path);
        SavePinned(list);
        BuildSidebar();
    }

    void Unpin(string path)
    {
        SavePinned(LoadPinned().Where(p => !Same(p, path)));
        BuildSidebar();
    }
}
