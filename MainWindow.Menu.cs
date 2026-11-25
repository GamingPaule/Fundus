using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fundus.Controls;
using Fundus.Models;
using Fundus.Services;

namespace Fundus;

public partial class MainWindow
{
    static FsItem ItemAt(ListBox list, DependencyObject src)
    {
        while (src != null && src is not ListBoxItem && src != list) src = VisualTreeHelper.GetParent(src);
        return (src as ListBoxItem)?.DataContext as FsItem;
    }

    void Column_RightClick(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        var col = (ColumnView)list.Tag;
        var item = ItemAt(list, e.OriginalSource as DependencyObject);
        e.Handled = true;

        if (item != null && !list.SelectedItems.Contains(item)) list.SelectedItem = item;
        var paths = item != null ? col.SelectedAll.Select(i => i.Path).ToList() : new List<string>();

        // Umschalt+Rechtsklick → direkt das Windows-Menü (wie im Explorer, mit erweiterten Befehlen)
        if (paths.Count > 0 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ShellContextMenu.Show(Hwnd, paths, extended: true);
            return;
        }

        var m = new ContextMenu();
        void Add(string header, string gesture, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            mi.Click += (s, a) => act();
            m.Items.Add(mi);
        }
        bool inPc = col.DirPath == ColumnView.ThisPc;

        if (item != null)
        {
            Add("Öffnen", "Enter", () => { foreach (var i in col.SelectedAll) if (!i.IsDir) FileOps.Open(i.Path); else OpenItem(i); });
            if (!item.IsDir) Add("Öffnen mit …", "", () => FileOps.OpenWith(item.Path));
            Add("Übersicht (Quick Look)", "Leertaste", () => ShowQuickLook(item));
            m.Items.Add(new Separator());
            Add("Umbenennen", "F2", () => Rename(col), !inPc && paths.Count == 1);
            Add("Kopieren", "Strg+C", () => SetClipboard(false), !inPc);
            Add("Ausschneiden", "Strg+X", () => SetClipboard(true), !inPc);
        }
        Add("Einfügen", "Strg+V", () => Paste(), !inPc && Clipboard.ContainsFileDropList());
        if (item == null) Add("Neuer Ordner", "Strg+Umschalt+N", () => NewFolder_Click(null, null), !inPc);
        m.Items.Add(new Separator());
        Add("Pfad kopieren", "Strg+Umschalt+C", () =>
            Clipboard.SetText(paths.Count > 0 ? string.Join(Environment.NewLine, paths) : col.DirPath));
        if (item?.IsDir == true) Add("Zu Favoriten hinzufügen", "Strg+D", () => Pin(item.Path));
        string dirForTools = item?.IsDir == true ? item.Path : col.DirPath;
        Add("Im Terminal öffnen", "", () => FileOps.OpenTerminal(dirForTools));
        Add("Im Explorer zeigen", "", () => FileOps.ShowInExplorer(item?.Path ?? col.DirPath));
        if (item != null && !inPc)
        {
            m.Items.Add(new Separator());
            Add("In den Papierkorb legen", "Entf", async () =>
            {
                if (FileOps.Recycle(Hwnd, paths)) await col.LoadAsync(_showHidden);
            });
        }
        m.Items.Add(new Separator());
        Add("Eigenschaften", "Alt+Enter", () => FileOps.Properties(item?.Path ?? col.DirPath), !(item == null && inPc));
        if (item != null)
            Add("Weitere Optionen (Windows-Menü)", "Umschalt+Rechtsklick", () => ShellContextMenu.Show(Hwnd, paths, false));

        m.PlacementTarget = list;
        m.IsOpen = true;
    }

    // ---------- Drag & Drop (zwischen Spalten, mit Explorer, Desktop, anderen Apps) ----------

    Point _dragStart;
    bool _dragArmed;

    void AttachDragDrop(ColumnView col)
    {
        var list = col.List;
        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _dragStart = e.GetPosition(null);
            _dragArmed = ItemAt(list, e.OriginalSource as DependencyObject) != null;
        };
        list.PreviewMouseMove += (s, e) =>
        {
            if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(null) - _dragStart;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragArmed = false;
            if (col.DirPath == ColumnView.ThisPc) return;
            var paths = col.SelectedAll.Select(i => i.Path).ToArray();
            if (paths.Length == 0) return;
            DragDrop.DoDragDrop(list, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        };
        list.DragOver += (s, e) =>
        {
            e.Effects = DropEffect(e, DropTarget(col, e));
            e.Handled = true;
        };
        list.Drop += async (s, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            var target = DropTarget(col, e);
            var effect = DropEffect(e, target);
            if (effect == DragDropEffects.None) return;
            if (effect == DragDropEffects.Move) FileOps.Move(Hwnd, files, target); else FileOps.Copy(Hwnd, files, target);
            await RefreshDir(target);
            foreach (var f in files) await RefreshDir(Path.GetDirectoryName(f));
        };
    }

    string DropTarget(ColumnView col, DragEventArgs e)
    {
        var item = ItemAt(col.List, e.OriginalSource as DependencyObject);
        return item is { IsDir: true } ? item.Path : col.DirPath;
    }

    static DragDropEffects DropEffect(DragEventArgs e, string target)
    {
        if (target == ColumnView.ThisPc || e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return DragDropEffects.None;
        // Nicht in sich selbst oder den eigenen Ordner ziehen
        if (files.Any(f => Same(f, target) || Same(Path.GetDirectoryName(f), target))) return DragDropEffects.None;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)) return DragDropEffects.Copy;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey)) return DragDropEffects.Move;
        // Windows-Logik: gleiches Laufwerk = verschieben, anderes = kopieren
        bool sameDrive = string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
        return sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
    }
}
