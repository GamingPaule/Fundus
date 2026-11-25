using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fundus.Models;
using Fundus.Services;

namespace Fundus.Controls;

/// <summary>Baut Vorschau-Inhalte für Dateien (Vorschau-Spalte und Quick Look).</summary>
public static class Preview
{
    static readonly HashSet<string> ImageExt = new() { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp", ".jfif" };
    static readonly HashSet<string> TextExt = new()
    {
        ".txt", ".md", ".log", ".json", ".xml", ".yml", ".yaml", ".ini", ".cfg", ".conf", ".csv", ".cs", ".xaml", ".csproj",
        ".js", ".ts", ".tsx", ".jsx", ".html", ".htm", ".css", ".scss", ".py", ".java", ".c", ".cpp", ".h", ".hpp", ".rs",
        ".go", ".lua", ".sh", ".ps1", ".bat", ".cmd", ".sql", ".toml", ".gitignore", ".env", ".dart", ".kt", ".php", ".rb", ".gcode",
    };

    public static bool IsImage(FsItem i) => ImageExt.Contains(i.Extension);
    public static bool IsText(FsItem i) => TextExt.Contains(i.Extension) || (i.Extension == "" && !i.IsDir && i.Size < 200_000);

    /// <summary>Vorschau-Spalte rechts neben der letzten Spalte.</summary>
    public static FrameworkElement BuildColumn(FsItem item)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(BuildVisual(item, 200, false));
        panel.Children.Add(new TextBlock
        {
            Text = item.Name, FontSize = 15, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 2),
        });
        panel.Children.Add(new TextBlock
        {
            Text = ShellIcons.TypeName(item.Path, item.IsDir) + (item.IsDir ? "" : " – " + FormatSize(item.Size)),
            Opacity = 0.6, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 14),
        });

        panel.Children.Add(Header("Informationen"));
        try
        {
            var fi = new FileInfo(item.Path);
            AddRow(panel, "Erstellt", fi.CreationTime.ToString("dd.MM.yyyy, HH:mm"));
            AddRow(panel, "Geändert", fi.LastWriteTime.ToString("dd.MM.yyyy, HH:mm"));
            AddRow(panel, "Geöffnet", fi.LastAccessTime.ToString("dd.MM.yyyy, HH:mm"));
            if (IsImage(item)) AddImageSize(panel, item.Path);
            AddRow(panel, "Ort", Path.GetDirectoryName(item.Path));
        }
        catch { }

        return new ScrollViewer
        {
            Content = panel, Width = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>Bild, Text-Auszug oder großes Icon.</summary>
    public static FrameworkElement BuildVisual(FsItem item, double max, bool big)
    {
        if (IsImage(item))
        {
            var img = LoadImage(item.Path, big ? 0 : (int)max * 2);
            if (img != null)
                return new Image { Source = img, MaxHeight = big ? double.PositiveInfinity : max, MaxWidth = big ? double.PositiveInfinity : max * 1.3, Stretch = Stretch.Uniform };
        }
        if (IsText(item))
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x19)), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10), MaxHeight = big ? double.PositiveInfinity : max * 1.4,
                Child = new TextBox
                {
                    Text = ReadText(item.Path, big ? 200_000 : 3_000), IsReadOnly = true, BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = big ? 13 : 10.5,
                    TextWrapping = big ? TextWrapping.NoWrap : TextWrapping.Wrap,
                    VerticalScrollBarVisibility = big ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = big ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                },
            };
        }
        return new Image { Source = ShellIcons.Get(item.Path, item.IsDir, large: true), Width = 96, Height = 96 };
    }

    static BitmapImage LoadImage(string path, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // Datei nicht sperren
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    static string ReadText(string path, int maxChars)
    {
        try
        {
            using var r = new StreamReader(path, Encoding.UTF8, true);
            var buf = new char[maxChars];
            int n = r.ReadBlock(buf, 0, maxChars);
            var s = new string(buf, 0, n);
            return n == maxChars ? s + "\n…" : s;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static void AddImageSize(StackPanel p, string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            AddRow(p, "Abmessungen", $"{frame.PixelWidth} × {frame.PixelHeight}");
        }
        catch { }
    }

    static TextBlock Header(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

    static void AddRow(StackPanel p, string key, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = key, Opacity = 0.55 });
        var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        p.Children.Add(g);
    }

    public static string FormatSize(long b)
    {
        string[] u = { "Byte", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return i == 0 ? $"{b} Byte" : $"{v:0.#} {u[i]}";
    }
}
