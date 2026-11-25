using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Fundus.Controls;
using Fundus.Services;

namespace Fundus;

public partial class MainWindow
{
    Button _cutBtn, _copyBtn, _pasteBtn, _renameBtn, _deleteBtn, _newBtn;

    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    Button Cmd(string glyph, string text, string tip, Action act, bool dropdown = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        if (text != null)
            sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        if (dropdown)
            sp.Children.Add(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 9, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        var b = new Button { Style = (Style)FindResource("CmdButton"), Content = sp, ToolTip = tip };
        b.Click += (s, e) => act();
        CommandBar.Children.Add(b);
        return b;
    }

    void Sep() => CommandBar.Children.Add(new Border
    {
        Width = 1, Height = 22, Margin = new Thickness(8, 0, 8, 0), Background = (Brush)FindResource("Line"),
    });

    void BuildCommandBar()
    {
        CommandBar.Children.Clear();
        _newBtn = Cmd("", "Neu", "Neues Element erstellen", () => ShowNewMenu(_newBtn), dropdown: true);
        Sep();
        _cutBtn = Cmd("", null, "Ausschneiden (Strg+X)", () => SetClipboard(true));
        _copyBtn = Cmd("", null, "Kopieren (Strg+C)", () => SetClipboard(false));
        _pasteBtn = Cmd("", null, "Einfügen (Strg+V)", Paste);
        _renameBtn = Cmd("", null, "Umbenennen (F2)", () => { if (Active != null) Rename(Active); });
        _deleteBtn = Cmd("", null, "Löschen – in den Papierkorb (Entf)", DeleteSelected);
        Sep();
        Button sortBtn = null, viewBtn = null;
        sortBtn = Cmd("", "Sortieren", "Sortierung ändern", () => ShowSortMenu(sortBtn), dropdown: true);
        viewBtn = Cmd("", "Ansicht", "Ansicht wechseln", () => ShowViewMenu(viewBtn), dropdown: true);
        Sep();
        Cmd("", null, "Pfad kopieren (Strg+Umschalt+C)", CopyPath);
        Cmd("", null, "Terminal hier öffnen", () => FileOps.OpenTerminal(TargetDir));
        Cmd("", null, "Im Windows-Explorer öffnen", () => FileOps.ShowInExplorer(SelectedPaths.FirstOrDefault() ?? CurrentDir));
        Cmd("", null, "Eigenschaften (Alt+Enter)", () => FileOps.Properties(SelectedPaths.FirstOrDefault() ?? CurrentDir));
        Sep();
        Button moreBtn = null;
        moreBtn = Cmd("", null, "Weitere Optionen", () => ShowMoreMenu(moreBtn));
        BuildViewSwitch();
    }

    void UpdateCommandStates()
    {
        if (_cutBtn == null) return;
        bool inPc = TargetDir == ColumnView.ThisPc;
        bool sel = SelectedPaths.Count > 0 && Active?.DirPath != ColumnView.ThisPc;
        _cutBtn.IsEnabled = _copyBtn.IsEnabled = _deleteBtn.IsEnabled = sel;
        _renameBtn.IsEnabled = sel && SelectedPaths.Count == 1;
        _newBtn.IsEnabled = !inPc;
        try { _pasteBtn.IsEnabled = !inPc && Clipboard.ContainsFileDropList(); } catch { _pasteBtn.IsEnabled = false; }
    }

    async void DeleteSelected()
    {
        var col = Active;
        var paths = SelectedPaths;
        if (col == null || paths.Count == 0 || col.DirPath == ColumnView.ThisPc) return;
        if (FileOps.Recycle(Hwnd, paths)) { await col.LoadAsync(_showHidden); UpdateChrome(); }
    }

    ContextMenu Menu(Button anchor) => new() { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

    MenuItem Item(ContextMenu m, string header, Action act, string glyph = null, bool check = false)
    {
        var mi = new MenuItem { Header = header, IsCheckable = false, IsChecked = check };
        if (glyph != null) mi.Icon = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14 };
        mi.Click += (s, e) => act();
        m.Items.Add(mi);
        return mi;
    }

    void ShowNewMenu(Button anchor)
    {
        var m = Menu(anchor);
        Item(m, "Ordner", () => NewFolder_Click(null, null), "");
        m.Items.Add(new Separator());
        Item(m, "Textdokument", () => NewFile("Neues Textdokument.txt"), "");
        Item(m, "Markdown-Datei", () => NewFile("Neue Notiz.md"), "");
        Item(m, "HTML-Datei", () => NewFile("index.html"), "");
        Item(m, "Leere Datei …", () =>
        {
            var name = InputDialog.Ask(this, "Neue Datei", "Neue Datei.txt", selectStem: true);
            if (name != null) NewFile(name, ask: false);
        }, "");
        m.IsOpen = true;
    }

    async void NewFile(string name, bool ask = true)
    {
        var dir = TargetDir;
        if (dir == ColumnView.ThisPc) return;
        var path = FileOps.UniqueName(dir, name);
        if (ask)
        {
            var chosen = InputDialog.Ask(this, "Neue Datei", Path.GetFileName(path), selectStem: true);
            if (chosen == null) return;
            path = Path.Combine(dir, chosen);
        }
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            var col = _columns.FirstOrDefault(c => Same(c.DirPath, dir));
            if (col != null) { await col.LoadAsync(_showHidden); col.SelectPath(path); FocusColumn(col); }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
    }

    void ShowSortMenu(Button anchor)
    {
        var m = Menu(anchor);
        foreach (var (key, label) in new[] { (SortKey.Name, "Name"), (SortKey.Date, "Änderungsdatum"), (SortKey.Type, "Typ"), (SortKey.Size, "Größe") })
            Item(m, label, async () => { ColumnView.Sort = key; await ReloadAll(); }, check: ColumnView.Sort == key);
        m.Items.Add(new Separator());
        Item(m, "Aufsteigend", async () => { ColumnView.SortDesc = false; await ReloadAll(); }, check: !ColumnView.SortDesc);
        Item(m, "Absteigend", async () => { ColumnView.SortDesc = true; await ReloadAll(); }, check: ColumnView.SortDesc);
        m.Items.Add(new Separator());
        Item(m, "Ordner zuerst", async () => { ColumnView.FoldersFirst = !ColumnView.FoldersFirst; await ReloadAll(); }, check: ColumnView.FoldersFirst);
        m.IsOpen = true;
    }

    void ShowViewMenu(Button anchor)
    {
        var m = Menu(anchor);
        Item(m, "Spalten", async () => await SetView(ViewMode.Columns), "", _view == ViewMode.Columns).InputGestureText = "Strg+1";
        Item(m, "Details", async () => await SetView(ViewMode.Details), "", _view == ViewMode.Details).InputGestureText = "Strg+2";
        Item(m, "Symbole", async () => await SetView(ViewMode.Icons), "", _view == ViewMode.Icons).InputGestureText = "Strg+3";
        m.Items.Add(new Separator());
        Item(m, "Ausgeblendete Elemente", () => ToggleHidden_Click(null, null), check: _showHidden).InputGestureText = "Strg+H";
        Item(m, "Vorschau-Spalte", async () => { _previewOn = !_previewOn; await NavigateTo(CurrentDir, false); }, check: _previewOn);
        m.IsOpen = true;
    }

    bool _previewOn = true;

    void ShowMoreMenu(Button anchor)
    {
        var m = Menu(anchor);
        bool on = DefaultHandler.IsRegistered;
        Item(m, on ? "Windows-Explorer wieder als Standard" : "Fundus als Standard-Dateimanager", () =>
        {
            try
            {
                if (on) { DefaultHandler.Unregister(); Services.Background.Disable(); }
                else DefaultHandler.Register(App.ExePath);
                MessageBox.Show(this, on
                        ? "Erledigt – Win+E und Ordner öffnen wieder den Windows-Explorer."
                        : "Erledigt – Win+E und Doppelklick auf Ordner öffnen jetzt Fundus.\n\nRückgängig: hier im ⋯-Menü oder mit „Fundus.exe --unregister“.",
                    "Fundus", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fehler"); }
        }, on ? "" : "");

        // Win+E braucht Fundus im Hintergrund (Windows 11 25H2 fragt dafür keine Registry ab)
        bool winE = Services.Background.IsEnabled;
        Item(m, "Win+E öffnet Fundus", () =>
        {
            if (winE) Services.Background.Disable();
            else Services.Background.Enable(() => Present(null), ExitForReal);
        }, check: winE).ToolTip = "Fundus fängt Win+E ab, solange es läuft (Tray-Symbol, X versteckt nur das Fenster). Aus = Win+E öffnet wieder den Explorer.";
        bool auto = Services.Background.IsAutostart;
        var autoItem = Item(m, "Mit Windows starten (Win+E auch nach Neustart)", () =>
            Services.Background.SetAutostart(auto ? null : App.ExePath), check: auto);
        autoItem.IsEnabled = winE;
        autoItem.ToolTip = "Fundus startet bei der Anmeldung unsichtbar mit, damit Win+E sofort funktioniert.";
        Item(m, "Im Windows-Explorer öffnen", () => FileOps.ShowInExplorer(SelectedPaths.FirstOrDefault() ?? CurrentDir), "");
        m.Items.Add(new Separator());
        Item(m, "Über Fundus", () => MessageBox.Show(this,
            "Fundus – Dateimanager mit Spaltenansicht\n\n" + App.ExePath, "Über Fundus"), "");
        m.IsOpen = true;
    }
}
