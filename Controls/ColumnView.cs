using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Fundus.Models;

namespace Fundus.Controls;

public enum SortKey { Name, Date, Type, Size }

/// <summary>Ein Ordnerinhalt – als Spalte (Miller Columns), Detailliste oder Symbolraster.</summary>
public class ColumnView : Grid
{
    public const string ThisPc = "::pc";

    public static SortKey Sort = SortKey.Name;
    public static bool SortDesc;
    public static bool FoldersFirst = true;

    public string DirPath { get; }
    public ViewMode Mode { get; }
    public ListView List { get; }
    public ObservableCollection<FsItem> Items { get; } = new();
    public FsItem Selected => List.SelectedItems.Count == 1 ? List.SelectedItem as FsItem : null;
    public List<FsItem> SelectedAll => List.SelectedItems.Cast<FsItem>().ToList();

    FileSystemWatcher _watcher;
    bool _reloadPending;

    public event Action<ColumnView> Changed;
    public event Action<ColumnView> SelectionReset;
    public event Action SortRequested;
    public bool Reloading { get; private set; }
    public Func<bool> ShowHiddenProvider { get; set; }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    public ColumnView(string dirPath, double width, ViewMode mode)
    {
        DirPath = dirPath;
        Mode = mode;
        Width = width;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(mode == ViewMode.Columns ? 1 : 0) });

        List = new ListView
        {
            Style = (Style)Application.Current.FindResource("PlainList"),
            ItemsSource = Items,
            Padding = new Thickness(0, 4, 0, 4),
            SelectionMode = SelectionMode.Extended,
            Tag = this,
        };
        ViewTemplates.Apply(this, mode);
        Children.Add(List);

        if (mode == ViewMode.Columns)
        {
            // Trennlinie, die man zum Breite-Ändern ziehen kann
            var grip = new Thumb { Width = 5, Margin = new Thickness(-2, 0, -2, 0), Cursor = Cursors.SizeWE, Template = GripTemplate() };
            grip.DragDelta += (s, e) => Width = Math.Max(140, Width + e.HorizontalChange);
            SetColumn(grip, 1);
            Children.Add(grip);
        }
    }

    public void RequestSort(SortKey key)
    {
        if (Sort == key) SortDesc = !SortDesc; else { Sort = key; SortDesc = false; }
        SortRequested?.Invoke();
    }

    static ControlTemplate GripTemplate()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var b = new FrameworkElementFactory(typeof(Border));
        b.SetValue(Border.BackgroundProperty, Application.Current.FindResource("Line"));
        b.SetValue(Border.WidthProperty, 1.0);
        t.VisualTree = b;
        return t;
    }

    public void SelectPath(string path)
    {
        var it = Items.FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (it == null) return;
        List.SelectedItem = it;
        List.ScrollIntoView(it);
    }

    public async Task LoadAsync(bool showHidden)
    {
        List<FsItem> items;
        try { items = await Task.Run(() => Read(DirPath, showHidden)); }
        catch (UnauthorizedAccessException) { items = new(); ShowMessage("Kein Zugriff"); }
        catch (IOException ex) { items = new(); ShowMessage(ex.Message); }

        // Auswahl erst NACH dem Lesen merken (sie kann sich während des Ladens geändert haben)
        var keep = new HashSet<string>(SelectedAll.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        bool hadSelection = keep.Count > 0;
        Reloading = true;
        try
        {
            Items.Clear();
            foreach (var i in items) Items.Add(i);
            foreach (var i in Items) if (keep.Contains(i.Path)) List.SelectedItems.Add(i);
        }
        finally { Reloading = false; }
        // Ausgewähltes Element ist verschwunden → Spalten rechts davon aufräumen
        if (hadSelection && List.SelectedItems.Count != keep.Count) SelectionReset?.Invoke(this);
        Watch();
    }

    static List<FsItem> Read(string dir, bool showHidden)
    {
        if (dir == ThisPc)
            return DriveInfo.GetDrives().Where(d => d.IsReady).Select(FsItem.FromDrive).ToList();

        var di = new DirectoryInfo(dir);
        var list = new List<FsItem>();
        foreach (var fi in di.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            bool hidden = fi.Attributes.HasFlag(FileAttributes.Hidden);
            bool system = fi.Attributes.HasFlag(FileAttributes.System);
            if (!showHidden && (hidden || system)) continue;
            if (fi.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(FsItem.FromInfo(fi));
        }
        list.Sort(Compare);
        return list;
    }

    static int Compare(FsItem a, FsItem b)
    {
        if (FoldersFirst && a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
        int r = Sort switch
        {
            SortKey.Date => a.Modified.CompareTo(b.Modified),
            SortKey.Size => a.Size.CompareTo(b.Size),
            SortKey.Type => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
            _ => 0,
        };
        if (r == 0) r = StrCmpLogicalW(a.Name, b.Name);
        return SortDesc ? -r : r;
    }

    void ShowMessage(string msg)
    {
        Children.Add(new TextBlock
        {
            Text = msg, Opacity = 0.6, Margin = new Thickness(12),
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center,
        });
    }

    /// <summary>Ordner live beobachten, damit neue/gelöschte Dateien sofort erscheinen.</summary>
    void Watch()
    {
        if (_watcher != null || DirPath == ThisPc) return;
        try
        {
            _watcher = new FileSystemWatcher(DirPath) { IncludeSubdirectories = false, EnableRaisingEvents = true };
            FileSystemEventHandler h = (s, e) => ScheduleReload();
            _watcher.Created += h; _watcher.Deleted += h; _watcher.Changed += h;
            _watcher.Renamed += (s, e) => ScheduleReload();
        }
        catch { _watcher = null; }
    }

    void ScheduleReload()
    {
        if (_reloadPending) return;
        _reloadPending = true;
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(300);
            _reloadPending = false;
            if (IsLoaded) { await LoadAsync(ShowHiddenProvider?.Invoke() ?? false); Changed?.Invoke(this); }
        });
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
