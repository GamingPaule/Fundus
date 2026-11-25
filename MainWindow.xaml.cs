using System.IO;
using System.Windows;
using System.Windows.Controls;
using Fundus.Controls;
using Fundus.Models;

namespace Fundus;

public enum ViewMode { Columns, Details, Icons }

public partial class MainWindow : Window
{
    readonly List<ColumnView> _columns = new();
    FrameworkElement _previewCol;
    readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string _base;
    bool _building;
    bool _showHidden;
    double _columnWidth = 240;
    ViewMode _view = ViewMode.Columns;

    List<string> _history = new();
    int _histPos = -1;

    public MainWindow()
    {
        InitializeComponent();
        _base = _home;
        BuildSidebar();
        BuildCommandBar();
        PreviewKeyDown += Window_PreviewKeyDown;
        SourceInitialized += (s, e) =>
        {
            int on = 1; // dunkle Systemrahmen/Snap-Vorschau
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
        };
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Ordner/Datei in neuem Tab öffnen (Startparameter oder weitere Instanz).</summary>
    async Task OpenTarget(string path)
    {
        if (path != null && File.Exists(path))
        {
            await NewTab(Path.GetDirectoryName(path));
            await Reveal(path);
        }
        else await NewTab(path != null && Directory.Exists(path) ? Path.GetFullPath(path) : ColumnView.ThisPc);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);

    /// <summary>
    /// Fenster zeigen und Ordner/Startseite in neuem Tab öffnen –
    /// beim Start, bei Win+E, bei Doppelklick auf einen Ordner (zweite Instanz).
    /// </summary>
    public async void Present(string path)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        ForceForeground();
        await OpenTarget(path);
    }

    /// <summary>Windows erlaubt Hintergrund-Prozessen kein SetForegroundWindow – kurz an den Vordergrund-Thread koppeln.</summary>
    void ForceForeground()
    {
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
        bool attached = fg != me && AttachThreadInput(me, fg, true);
        SetForegroundWindow(h);
        BringWindowToTop(h);
        Activate();
        if (attached) AttachThreadInput(me, fg, false);
        // Fallback: derselbe Weg wie Alt+Tab – wird vom Fokus-Schutz nicht blockiert
        if (GetForegroundWindow() != h) SwitchToThisWindow(h, true);
    }

    bool _exiting;

    /// <summary>Im Hintergrundmodus versteckt das X nur das Fenster (wie ein geschlossenes Explorer-Fenster).</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && Services.Background.IsEnabled)
        {
            e.Cancel = true;
            Hide();
            ResetTabs();
        }
        base.OnClosing(e);
    }

    /// <summary>Wirklich beenden (Tray → Beenden).</summary>
    public void ExitForReal()
    {
        _exiting = true;
        Services.Background.Stop();
        Close();
    }

    void ResetTabs()
    {
        _tabs.Clear();
        _tabIndex = -1;
        ClearColumns(0);
        SingleHost.Child = null;
        TabStrip.Children.Clear();
        _history = new();
        _histPos = -1;
    }

    ColumnView Last => _columns.Count > 0 ? _columns[^1] : null;

    /// <summary>Aktueller Ordner = tiefste Spalte.</summary>
    public string CurrentDir => Last?.DirPath ?? _base;

    // ---------- Navigation ----------

    public async Task NavigateTo(string target, bool addHistory = true, string selectPath = null)
    {
        if (target == ColumnView.ThisPc) { ShowHome(addHistory); return; }
        if (!Directory.Exists(target)) return;
        _building = true;
        try
        {
            if (!IsUnder(target, _base)) _base = PickBase(target);
            ClearColumns(0);
            SingleHost.Child = null;
            bool cols = _view == ViewMode.Columns;
            ColumnsScroller.Visibility = cols ? Visibility.Visible : Visibility.Collapsed;
            SingleHost.Visibility = cols ? Visibility.Collapsed : Visibility.Visible;

            var chain = cols ? BuildChain(_base, target) : new List<string> { target };
            for (int i = 0; i < chain.Count; i++)
            {
                var col = await AddColumn(chain[i]);
                string next = i + 1 < chain.Count ? chain[i + 1] : selectPath;
                if (next != null) col.SelectPath(next);
            }
            if (cols && selectPath != null && Last?.Selected is { } sel)
            {
                if (sel.IsDir) await AddColumn(sel.Path); else ShowPreview(sel);
            }
        }
        finally { _building = false; }

        if (addHistory) PushHistory(target);
        FocusColumn(_view == ViewMode.Columns && selectPath == null ? Last : _columns.LastOrDefault(c => c.List.SelectedItem != null) ?? Last);
        UpdateChrome();
        ScrollToEnd();
    }

    /// <summary>"Dieser PC"-Startseite statt Laufwerksliste.</summary>
    void ShowHome(bool addHistory)
    {
        ClearColumns(0);
        _base = ColumnView.ThisPc;
        ColumnsScroller.Visibility = Visibility.Collapsed;
        SingleHost.Visibility = Visibility.Visible;
        var home = new HomePage(_favorites,
            navigate: async p => { _base = PickBaseFor(p); await NavigateTo(p); },
            open: Services.FileOps.Open,
            reveal: async p => { _base = PickBaseFor(p); await Reveal(p); },
            changed: RefreshHome);
        SingleHost.Child = home;
        if (addHistory) PushHistory(ColumnView.ThisPc);
        UpdateChrome();
        StatusText.Text = $"{DriveInfo.GetDrives().Count(d => d.IsReady)} Laufwerke     |     {_favorites.Count} Schnellzugriffe";
        home.Focus();
    }

    /// <summary>Startseite neu aufbauen, falls sie gerade offen ist (nach An/Aus im Rechtsklick-Menü).</summary>
    void RefreshHome()
    {
        if (_columns.Count == 0 && CurrentDir == ColumnView.ThisPc) ShowHome(false);
    }

    public Task Reveal(string path) =>
        path == ColumnView.ThisPc || Directory.Exists(path) ? NavigateTo(path) : NavigateTo(Path.GetDirectoryName(path), true, path);

    static List<string> BuildChain(string root, string target)
    {
        var chain = new List<string>();
        if (target == ColumnView.ThisPc) { chain.Add(target); return chain; }
        for (var d = target; d != null; d = Path.GetDirectoryName(d))
        {
            chain.Insert(0, d);
            if (Same(d, root)) return chain;
        }
        if (root == ColumnView.ThisPc) chain.Insert(0, root);
        return chain;
    }

    string PickBase(string target)
    {
        // Längster passender Favorit, sonst "Dieser PC"
        var fav = _favorites.Select(f => f.Path).Where(p => IsUnder(target, p)).OrderByDescending(p => p.Length).FirstOrDefault();
        return fav ?? ColumnView.ThisPc;
    }

    static bool IsUnder(string path, string root)
    {
        if (root == ColumnView.ThisPc) return true;
        if (path == ColumnView.ThisPc) return false;
        var r = root.TrimEnd('\\') + "\\";
        return Same(path, root) || path.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    static bool Same(string a, string b) =>
        string.Equals(a?.TrimEnd('\\'), b?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    async Task<ColumnView> AddColumn(string dir)
    {
        bool cols = _view == ViewMode.Columns;
        var col = new ColumnView(dir, cols ? _columnWidth : double.NaN, _view) { ShowHiddenProvider = () => _showHidden };
        col.List.SelectionChanged += (s, e) => OnColumnSelection(col);
        col.List.PreviewKeyDown += Column_KeyDown;
        col.List.MouseDoubleClick += (s, e) =>
        {
            if (MainWindow.ItemAt(col.List, e.OriginalSource as DependencyObject) is { } it) OpenItem(it);
        };
        col.List.PreviewMouseRightButtonUp += Column_RightClick;
        col.Changed += c => UpdateChrome();
        col.SelectionReset += OnColumnSelection;
        col.SortRequested += async () => await ReloadAll();
        AttachDragDrop(col);
        _columns.Add(col);
        if (cols) ColumnsHost.Children.Add(col); else SingleHost.Child = col;
        await col.LoadAsync(_showHidden);
        return col;
    }

    void ClearColumns(int fromIndex)
    {
        if (_previewCol != null) { ColumnsHost.Children.Remove(_previewCol); _previewCol = null; }
        for (int i = _columns.Count - 1; i >= fromIndex; i--)
        {
            _columns[i].Dispose();
            ColumnsHost.Children.Remove(_columns[i]);
            _columns.RemoveAt(i);
        }
    }

    async void OnColumnSelection(ColumnView col)
    {
        if (_building || col.Reloading) return;
        if (_view != ViewMode.Columns) { UpdateChrome(); return; }
        int idx = _columns.IndexOf(col);
        if (idx < 0) return;
        ClearColumns(idx + 1);
        var item = col.Selected;
        if (item == null) { UpdateChrome(); return; }

        if (item.IsDir)
        {
            await AddColumn(item.Path);
            PushHistory(item.Path);
        }
        else ShowPreview(item);
        UpdateChrome();
        ScrollToEnd();
    }

    void ShowPreview(FsItem item)
    {
        if (!_previewOn) return;
        _previewCol = Preview.BuildColumn(item);
        ColumnsHost.Children.Add(_previewCol);
    }

    void ScrollToEnd() => Dispatcher.InvokeAsync(() => ColumnsScroller.ScrollToRightEnd(), System.Windows.Threading.DispatcherPriority.Background);

    void FocusColumn(ColumnView col)
    {
        if (col == null) return;
        Dispatcher.InvokeAsync(() =>
        {
            col.List.Focus();
            var c = col.List.ItemContainerGenerator.ContainerFromItem(col.List.SelectedItem) as ListBoxItem;
            c?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    async Task SetView(ViewMode mode)
    {
        if (_view == mode) return;
        // Aktueller Ordner = tiefste Spalte; Auswahl nur aus dieser übernehmen
        var dir = CurrentDir;
        var select = Last?.SelectedAll.FirstOrDefault()?.Path;
        _view = mode;
        await NavigateTo(dir, false, select);
        BuildViewSwitch();
    }

    // ---------- Historie ----------

    void PushHistory(string p)
    {
        if (_histPos >= 0 && Same(_history[_histPos], p)) return;
        _history.RemoveRange(_histPos + 1, _history.Count - _histPos - 1);
        _history.Add(p);
        _histPos = _history.Count - 1;
    }

    async void Back_Click(object s, RoutedEventArgs e) { if (_histPos > 0) await NavigateTo(_history[--_histPos], false); }
    async void Forward_Click(object s, RoutedEventArgs e) { if (_histPos < _history.Count - 1) await NavigateTo(_history[++_histPos], false); }
    void Up_Click(object s, RoutedEventArgs e) => GoUp();
    void Refresh_Click(object s, RoutedEventArgs e) => Refresh();
}
