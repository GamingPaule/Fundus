using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fundus.Controls;
using Fundus.Models;

namespace Fundus;

public partial class MainWindow
{
    // ---------- Quick Look ----------

    void ShowQuickLook(FsItem item)
    {
        QuickLookTitle.Text = item.Name;
        QuickLookBody.Content = Preview.BuildVisual(item, 600, big: true);
        QuickLook.Visibility = Visibility.Visible;
    }

    void CloseQuickLook()
    {
        QuickLook.Visibility = Visibility.Collapsed;
        QuickLookBody.Content = null;
        FocusColumn(Active);
    }

    void QuickLook_Click(object s, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == QuickLook) CloseQuickLook();
    }

    // ---------- Adresszeile (Strg+L) ----------

    void ShowAddressBox()
    {
        AddressBox.Text = CurrentDir == ColumnView.ThisPc ? "Dieser PC" : CurrentDir;
        AddressBox.Visibility = Visibility.Visible;
        CrumbPanel.Visibility = Visibility.Hidden;
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    void HideAddressBox()
    {
        AddressBox.Visibility = Visibility.Collapsed;
        CrumbPanel.Visibility = Visibility.Visible;
    }

    async void AddressBox_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { HideAddressBox(); FocusColumn(Active); }
        if (e.Key != Key.Enter) return;
        var raw = AddressBox.Text.Trim().Trim('"');
        HideAddressBox();
        if (raw.Equals("Dieser PC", StringComparison.OrdinalIgnoreCase)) { await NavigateTo(ColumnView.ThisPc); return; }
        var p = Environment.ExpandEnvironmentVariables(raw);
        if (p.StartsWith("~")) p = _home + p[1..];
        if (p.StartsWith(@"\\") || p.Contains("://")) { Services.FileOps.Open(p); return; } // Netzwerk/URLs → Windows
        if (Directory.Exists(p) || File.Exists(p)) await Reveal(Path.GetFullPath(p));
        else StatusText.Text = "Pfad nicht gefunden: " + p;
    }

    void AddressBox_LostFocus(object s, KeyboardFocusChangedEventArgs e) => HideAddressBox();

    // ---------- Suche (rekursiv im aktuellen Ordner) ----------

    CancellationTokenSource _searchCts;
    readonly ObservableCollection<FsItem> _results = new();

    void SearchBox_TextChanged(object s, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (SearchBox.Text.Length == 0) EndSearch();
        else _ = RunSearch(SearchBox.Text.Trim());
    }

    void SearchBox_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SearchBox.Text = ""; FocusColumn(Active); }
        else if (e.Key == Key.Down && _results.Count > 0) { SearchResults.SelectedIndex = 0; SearchResults.Focus(); }
    }

    void EndSearch()
    {
        _searchCts?.Cancel();
        SearchResults.Visibility = Visibility.Collapsed;
        ColumnsScroller.Visibility = Visibility.Visible;
        UpdateChrome();
    }

    async Task RunSearch(string term)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try { await Task.Delay(250, cts.Token); } catch { return; }

        string root = CurrentDir == ColumnView.ThisPc ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : CurrentDir;
        _results.Clear();
        SearchResults.ItemsSource = _results;
        SearchResults.Visibility = Visibility.Visible;
        ColumnsScroller.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Suche in „{Path.GetFileName(root.TrimEnd('\\'))}“ …";

        var batch = new List<FsItem>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        await Task.Run(() =>
        {
            try
            {
                foreach (var fi in new DirectoryInfo(root).EnumerateFileSystemInfos("*" + term + "*", opts))
                {
                    if (cts.IsCancellationRequested) return;
                    lock (batch) batch.Add(FsItem.FromInfo(fi));
                    if (batch.Count >= 50) Flush();
                }
            }
            catch { }
        });
        Flush();
        if (!cts.IsCancellationRequested) StatusText.Text = $"{_results.Count} Treffer für „{term}“";

        void Flush()
        {
            List<FsItem> chunk;
            lock (batch) { chunk = batch.ToList(); batch.Clear(); }
            Dispatcher.Invoke(() =>
            {
                if (cts.IsCancellationRequested) return;
                foreach (var i in chunk) if (_results.Count < 5000) _results.Add(i);
            });
        }
    }

    async void SearchResults_DoubleClick(object s, MouseButtonEventArgs e) => await OpenSearchResult();

    async void SearchResults_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await OpenSearchResult(); }
        else if (e.Key == Key.Space && SearchResults.SelectedItem is FsItem it) { e.Handled = true; ShowQuickLook(it); }
        else if (e.Key == Key.Escape) { SearchBox.Text = ""; }
    }

    /// <summary>Treffer im Spaltenbrowser zeigen – der Pfad ist dann sofort sichtbar.</summary>
    async Task OpenSearchResult()
    {
        if (SearchResults.SelectedItem is not FsItem it) return;
        SearchBox.Text = "";
        await Reveal(it.Path);
    }
}
