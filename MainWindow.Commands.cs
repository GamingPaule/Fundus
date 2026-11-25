using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Fundus.Controls;
using Fundus.Models;
using Fundus.Services;

namespace Fundus;

public partial class MainWindow
{
    IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    /// <summary>Spalte mit Fokus, sonst die tiefste mit Auswahl, sonst die letzte.</summary>
    ColumnView Active =>
        _columns.FirstOrDefault(c => c.List.IsKeyboardFocusWithin)
        ?? _columns.LastOrDefault(c => c.List.SelectedItems.Count > 0) ?? Last;

    List<string> SelectedPaths => Active?.SelectedAll.Select(i => i.Path).ToList() ?? new();

    /// <summary>Zielordner für Einfügen/Neuer Ordner: Ordner der aktiven Spalte.</summary>
    string TargetDir => Active?.DirPath ?? CurrentDir;

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (QuickLook.Visibility == Visibility.Visible)
        {
            if (key is Key.Space or Key.Escape) { CloseQuickLook(); e.Handled = true; }
            else if (key is Key.Up or Key.Down or Key.Left or Key.Right) { CloseQuickLook(); }
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return;

        e.Handled = true;
        if (ctrl && key == Key.T) _ = NewTab(ColumnView.ThisPc);
        else if (ctrl && key == Key.W) _ = CloseTab(_tabIndex);
        else if (ctrl && key == Key.Tab) _ = SwitchTab((_tabIndex + (shift ? _tabs.Count - 1 : 1)) % _tabs.Count);
        else if (ctrl && key is Key.D1 or Key.NumPad1) _ = SetView(ViewMode.Columns);
        else if (ctrl && key is Key.D2 or Key.NumPad2) _ = SetView(ViewMode.Details);
        else if (ctrl && key is Key.D3 or Key.NumPad3) _ = SetView(ViewMode.Icons);
        else if (alt && key == Key.Up) GoUp();
        else if (alt && key == Key.Left) Back_Click(null, null);
        else if (alt && key == Key.Right) Forward_Click(null, null);
        else if (alt && key == Key.Enter) FileOps.Properties(SelectedPaths.FirstOrDefault() ?? CurrentDir);
        else if (ctrl && key == Key.Up || key == Key.Back) GoUp();
        else if (ctrl && shift && key == Key.N) NewFolder_Click(null, null);
        else if (ctrl && shift && key == Key.C) CopyPath();
        else if (ctrl && key == Key.C) SetClipboard(false);
        else if (ctrl && key == Key.X) SetClipboard(true);
        else if (ctrl && key == Key.V) Paste();
        else if (ctrl && key == Key.H) ToggleHidden_Click(null, null);
        else if (ctrl && key == Key.L) ShowAddressBox();
        else if (ctrl && key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); }
        else if (ctrl && key == Key.D) PinSelected();
        else if (key == Key.F5) Refresh();
        else e.Handled = false;
    }

    async void Column_KeyDown(object sender, KeyEventArgs e)
    {
        var col = (ColumnView)((ListBox)sender).Tag;
        int idx = _columns.IndexOf(col);
        if (Keyboard.Modifiers != ModifierKeys.None && Keyboard.Modifiers != ModifierKeys.Shift) return;

        bool cols = _view == ViewMode.Columns;
        switch (e.Key)
        {
            case Key.Right when cols:
                e.Handled = true;
                if (col.Selected is { IsDir: true } && idx + 1 < _columns.Count)
                {
                    var next = _columns[idx + 1];
                    if (next.Items.Count > 0 && next.List.SelectedItem == null) next.List.SelectedIndex = 0;
                    FocusColumn(next);
                }
                break;
            case Key.Left when cols:
                e.Handled = true;
                if (idx > 0)
                {
                    col.List.SelectedItem = null;
                    FocusColumn(_columns[idx - 1]);
                }
                break;
            case Key.Enter:
                e.Handled = true;
                var all = col.SelectedAll;
                if (!cols && all.Count == 1 && all[0].IsDir) { OpenItem(all[0]); break; }
                foreach (var it in all) if (cols || !it.IsDir) OpenItem(it);
                break;
            case Key.Space:
                e.Handled = true;
                if (col.List.SelectedItem is FsItem sel) ShowQuickLook(sel);
                break;
            case Key.F2:
                e.Handled = true;
                Rename(col);
                break;
            case Key.Delete:
                e.Handled = true;
                DeleteSelected();
                break;
        }
    }

    async void OpenItem(FsItem item)
    {
        if (item.IsDir && _view != ViewMode.Columns) await NavigateTo(item.Path);
        else if (item.IsDir)
        {
            int idx = _columns.FindIndex(c => c.List.SelectedItems.Contains(item));
            if (idx >= 0 && idx + 1 < _columns.Count) { var n = _columns[idx + 1]; if (n.Items.Count > 0) n.List.SelectedIndex = 0; FocusColumn(n); }
        }
        else FileOps.Open(item.Path);
    }

    async void GoUp()
    {
        var dir = CurrentDir;
        if (dir == ColumnView.ThisPc) return;
        var parent = Path.GetDirectoryName(dir) ?? ColumnView.ThisPc;
        await NavigateTo(parent, true, dir);
    }

    async void Rename(ColumnView col)
    {
        var it = col.Selected;
        if (it == null || col.DirPath == ColumnView.ThisPc) return;
        var name = InputDialog.Ask(this, "Umbenennen", it.Name, selectStem: !it.IsDir);
        if (name == null || name == it.Name) return;
        var target = Path.Combine(col.DirPath, name);
        try
        {
            if (it.IsDir) Directory.Move(it.Path, target); else File.Move(it.Path, target);
            await col.LoadAsync(_showHidden);
            col.SelectPath(target);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Umbenennen fehlgeschlagen"); }
    }

    async void NewFolder_Click(object s, RoutedEventArgs e)
    {
        var dir = TargetDir;
        if (dir == ColumnView.ThisPc) return;
        var suggestion = Path.GetFileName(FileOps.UniqueName(dir, "Neuer Ordner"));
        var name = InputDialog.Ask(this, "Neuer Ordner", suggestion);
        if (name == null) return;
        try
        {
            var path = Path.Combine(dir, name);
            Directory.CreateDirectory(path);
            var col = _columns.FirstOrDefault(c => Same(c.DirPath, dir));
            if (col != null) { await col.LoadAsync(_showHidden); col.SelectPath(path); FocusColumn(col); }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Fehler"); }
    }

    // ---------- Zwischenablage (kompatibel mit dem Explorer) ----------

    void SetClipboard(bool cut)
    {
        var paths = SelectedPaths;
        if (paths.Count == 0) return;
        var files = new StringCollection();
        files.AddRange(paths.ToArray());
        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        Clipboard.SetDataObject(data, true);
        StatusText.Text = $"{paths.Count} Objekt(e) {(cut ? "ausgeschnitten" : "kopiert")}";
    }

    async void Paste()
    {
        var dir = TargetDir;
        if (dir == ColumnView.ThisPc || !Clipboard.ContainsFileDropList()) return;
        var files = Clipboard.GetFileDropList().Cast<string>().ToList();
        bool cut = Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4
                   && (BitConverter.ToInt32(ms.ToArray(), 0) & 2) != 0;
        bool ok = cut ? FileOps.Move(Hwnd, files, dir) : FileOps.Copy(Hwnd, files, dir);
        if (ok && cut) Clipboard.Clear();
        await RefreshDir(dir);
    }

    void CopyPath()
    {
        var p = SelectedPaths;
        Clipboard.SetText(p.Count > 0 ? string.Join(Environment.NewLine, p) : CurrentDir);
        StatusText.Text = "Pfad kopiert";
    }

    void PinSelected()
    {
        var dir = Active?.Selected is { IsDir: true } d ? d.Path : CurrentDir;
        if (dir != ColumnView.ThisPc) Pin(dir);
    }

    async Task RefreshDir(string dir)
    {
        foreach (var c in _columns.Where(c => Same(c.DirPath, dir))) await c.LoadAsync(_showHidden);
        UpdateChrome();
    }

    async void Refresh()
    {
        if (_columns.Count == 0 && CurrentDir == ColumnView.ThisPc) { ShowHome(false); return; }
        foreach (var c in _columns.ToList()) await c.LoadAsync(_showHidden);
        UpdateChrome();
    }

    void ToggleHidden_Click(object s, RoutedEventArgs e)
    {
        _showHidden = !_showHidden;
        Refresh();
        StatusText.Text = _showHidden ? "Versteckte Dateien werden angezeigt" : "Versteckte Dateien ausgeblendet";
    }

    void Terminal_Click(object s, RoutedEventArgs e) => FileOps.OpenTerminal(TargetDir);
    void Explorer_Click(object s, RoutedEventArgs e) => FileOps.ShowInExplorer(SelectedPaths.FirstOrDefault() ?? CurrentDir);
}
