using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fundus.Controls;
using Fundus.Services;

namespace Fundus;

/// <summary>Zustand eines Tabs – beim Wechseln wird die Ansicht daraus neu aufgebaut.</summary>
public class TabState
{
    public string Base, Path, Select;
    public List<string> History = new();
    public int HistPos = -1;
    public ViewMode View = ViewMode.Columns;
}

public partial class MainWindow
{
    readonly List<TabState> _tabs = new();
    int _tabIndex = -1;
    TabState Tab => _tabIndex >= 0 ? _tabs[_tabIndex] : null;

    async Task NewTab(string path)
    {
        SaveTab();
        var t = new TabState { Base = PickBaseFor(path), Path = path, View = Tab?.View ?? ViewMode.Columns };
        _tabs.Insert(_tabIndex + 1, t);
        _tabIndex++;
        await LoadTab();
    }

    async Task SwitchTab(int i)
    {
        if (i < 0 || i >= _tabs.Count || i == _tabIndex) return;
        SaveTab();
        _tabIndex = i;
        await LoadTab();
    }

    async Task CloseTab(int i)
    {
        if (_tabs.Count == 1) { Close(); return; }
        _tabs.RemoveAt(i);
        if (i < _tabIndex) { _tabIndex--; RenderTabs(); }
        else if (i > _tabIndex) RenderTabs();
        else { _tabIndex = Math.Min(i, _tabs.Count - 1); await LoadTab(); }
    }

    void SaveTab()
    {
        var t = Tab;
        if (t == null) return;
        t.Base = _base;
        t.Path = CurrentDir;
        t.Select = _columns.LastOrDefault(c => c.Selected is { IsDir: false })?.Selected?.Path;
        t.History = _history;
        t.HistPos = _histPos;
        t.View = _view;
    }

    async Task LoadTab()
    {
        var t = Tab;
        _base = t.Base;
        _history = t.History;
        _histPos = t.HistPos;
        _view = t.View;
        await NavigateTo(t.Path, t.HistPos < 0, t.Select);
        RenderTabs();
    }

    string PickBaseFor(string path) =>
        path == ColumnView.ThisPc ? ColumnView.ThisPc
        : IsUnder(path, _home) ? _home : PickBase(path);

    void RenderTabs()
    {
        TabStrip.Children.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            int idx = i;
            bool active = i == _tabIndex;
            string path = active ? CurrentDir : _tabs[i].Path;

            var close = new Button
            {
                Style = (Style)FindResource("ToolButton"), Content = "", FontSize = 8,
                Width = 22, Height = 22, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Tab schließen (Strg+W)",
            };
            close.Click += async (s, e) => await CloseTab(idx);

            var row = new DockPanel();
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), Source = ShellIcons.Get(path, true) };
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = DisplayName(path), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

            var tab = new Border
            {
                Child = row, Height = 34, MinWidth = 150, MaxWidth = 240, Padding = new Thickness(12, 0, 6, 0),
                CornerRadius = new CornerRadius(8, 8, 0, 0), Margin = new Thickness(0, 0, 2, 0),
                Background = active ? (Brush)FindResource("BgToolbar") : Brushes.Transparent,
                ToolTip = path == ColumnView.ThisPc ? "Dieser PC" : path,
            };
            if (!active)
            {
                tab.MouseEnter += (s, e) => tab.Background = (Brush)FindResource("TabInactiveHover");
                tab.MouseLeave += (s, e) => tab.Background = Brushes.Transparent;
            }
            tab.MouseLeftButtonDown += async (s, e) => await SwitchTab(idx);
            tab.MouseDown += async (s, e) => { if (e.ChangedButton == MouseButton.Middle) await CloseTab(idx); };
            TabStrip.Children.Add(tab);
        }
    }

    async void NewTab_Click(object s, RoutedEventArgs e) => await NewTab(ColumnView.ThisPc);

    // ---------- Fenster-Steuerung (eigene Titelleiste) ----------

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool max = WindowState == WindowState.Maximized;
        Root.Margin = new Thickness(max ? 7 : 0); // maximiertes Fenster ragt sonst über den Bildschirmrand
        MaxBtn.Content = max ? "" : "";
    }
}
