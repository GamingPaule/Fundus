using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Fundus.Controls;

/// <summary>Richtet eine ListView als Spalte, Detailliste oder Symbolraster ein.</summary>
public static class ViewTemplates
{
    static object R(string key) => Application.Current.FindResource(key);

    public static void Apply(ColumnView col, ViewMode mode)
    {
        var list = col.List;
        switch (mode)
        {
            case ViewMode.Columns:
                list.ItemTemplate = (DataTemplate)R("ItemRow");
                break;

            case ViewMode.Details:
                list.ItemContainerStyle = (Style)R("DetailRow");
                list.Padding = new Thickness(0);
                ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
                var gv = new GridView { ColumnHeaderContainerStyle = (Style)R("DetailHeader") };
                gv.Columns.Add(Column(col, "Name", SortKey.Name, 360, cell: (DataTemplate)R("NameCell")));
                gv.Columns.Add(Column(col, "Änderungsdatum", SortKey.Date, 150, "ModifiedText"));
                gv.Columns.Add(Column(col, "Typ", SortKey.Type, 190, "TypeText"));
                gv.Columns.Add(Column(col, "Größe", SortKey.Size, 150, "SizeText"));
                list.View = gv;
                break;

            case ViewMode.Icons:
                list.ItemContainerStyle = (Style)R("TileItem");
                list.ItemTemplate = (DataTemplate)R("IconTile");
                list.Padding = new Thickness(8);
                var panel = new FrameworkElementFactory(typeof(VirtualizingTilePanel));
                list.ItemsPanel = new ItemsPanelTemplate(panel);
                ScrollViewer.SetCanContentScroll(list, true);
                VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Standard);
                break;
        }
    }

    static GridViewColumn Column(ColumnView col, string title, SortKey key, double width, string binding = null, DataTemplate cell = null)
    {
        string arrow = ColumnView.Sort == key ? (ColumnView.SortDesc ? "  ˅" : "  ˄") : "";
        var header = new GridViewColumnHeader { Content = title + arrow };
        header.Click += (s, e) => col.RequestSort(key);
        var c = new GridViewColumn { Header = header, Width = width };
        if (cell != null) c.CellTemplate = cell;
        else
        {
            // TextBlock statt DisplayMemberBinding, damit Text bei schmaler Spalte abgeschnitten wird
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding(binding));
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            tb.SetValue(TextBlock.OpacityProperty, 0.75);
            c.CellTemplate = new DataTemplate { VisualTree = tb };
        }
        return c;
    }
}

public class NotNull : IValueConverter
{
    public static readonly NotNull Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c) => v != null;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
