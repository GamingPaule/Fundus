using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using Fundus.Services;

namespace Fundus.Models;

/// <summary>Ein Eintrag in einer Spalte: Ordner, Datei oder Laufwerk.</summary>
public class FsItem : INotifyPropertyChanged
{
    public string Path { get; init; }
    public string Name { get; init; }
    public bool IsDir { get; init; }
    public bool IsHidden { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public DriveInfo Drive { get; init; }

    ImageSource _icon;
    public ImageSource Icon => _icon ??= ShellIcons.Get(Path, IsDir);

    public double Opacity => IsHidden ? 0.5 : 1.0;
    public string Chevron => IsDir ? "" : "";

    public string Extension => IsDir ? "" : System.IO.Path.GetExtension(Path).ToLowerInvariant();

    // ---------- Texte für die Detailansicht ----------
    public string ModifiedText => Modified == default ? "" : Modified.ToString("dd.MM.yyyy HH:mm");
    public string TypeText => Drive != null ? DriveTypeText : ShellIcons.TypeName(Path, IsDir);
    public string SizeText => Drive != null ? DriveSizeText : IsDir ? "" : SizeKb(Size);

    static string SizeKb(long b) => b == 0 ? "0 KB" : $"{Math.Max(1, (b + 1023) / 1024):N0} KB";

    string DriveTypeText => Drive.DriveType switch
    {
        DriveType.Network => "Netzlaufwerk",
        DriveType.Removable => "Wechseldatenträger",
        DriveType.CDRom => "CD/DVD-Laufwerk",
        _ => "Lokaler Datenträger",
    };

    string DriveSizeText
    {
        get
        {
            try { return $"{Controls.Preview.FormatSize(Drive.AvailableFreeSpace)} frei von {Controls.Preview.FormatSize(Drive.TotalSize)}"; }
            catch { return ""; }
        }
    }

    public double DriveUsed
    {
        get
        {
            try { return Drive is { IsReady: true, TotalSize: > 0 } ? 100.0 * (1 - (double)Drive.AvailableFreeSpace / Drive.TotalSize) : 0; }
            catch { return 0; }
        }
    }

    // ---------- Thumbnail (Symbolansicht) – wird im Hintergrund nachgeladen ----------
    ImageSource _thumb;
    bool _thumbRequested;
    public ImageSource Thumb
    {
        get
        {
            if (!_thumbRequested)
            {
                _thumbRequested = true;
                Thumbnails.Request(Path, 96, img => { _thumb = img; PropertyChanged?.Invoke(this, new(nameof(Thumb))); });
            }
            return _thumb ?? ShellIcons.Get(Path, IsDir, large: true);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public static FsItem FromInfo(FileSystemInfo fi) => new()
    {
        Path = fi.FullName,
        Name = fi.Name,
        IsDir = fi is DirectoryInfo,
        IsHidden = fi.Attributes.HasFlag(FileAttributes.Hidden),
        Size = fi is FileInfo f ? f.Length : 0,
        Modified = fi.LastWriteTime,
    };

    public static FsItem FromDrive(DriveInfo d)
    {
        string label = "";
        try { label = d.IsReady ? d.VolumeLabel : ""; } catch { }
        if (string.IsNullOrEmpty(label))
            label = d.DriveType == DriveType.Network ? "Netzlaufwerk" : "Lokaler Datenträger";
        return new FsItem
        {
            Path = d.RootDirectory.FullName,
            Name = $"{label} ({d.Name.TrimEnd('\\')})",
            IsDir = true,
            Drive = d,
        };
    }

    public override string ToString() => Name;
}
