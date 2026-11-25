using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Fundus.Controls;

/// <summary>Kachel-Raster, das nur die sichtbaren Elemente erzeugt – auch Ordner mit Tausenden Bildern bleiben flüssig.</summary>
public class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    public const double TileWidth = 130, TileHeight = 142;

    readonly TranslateTransform _trans = new();
    Size _extent, _viewport;
    double _offsetY;

    public VirtualizingTilePanel() => RenderTransform = _trans;

    static int Cols(double width) => Math.Max(1, (int)(width / TileWidth));
    int ItemCount => ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

    protected override Size MeasureOverride(Size avail)
    {
        if (double.IsInfinity(avail.Width)) avail.Width = TileWidth * 6;
        if (double.IsInfinity(avail.Height)) avail.Height = TileHeight * 6;
        int count = ItemCount, cols = Cols(avail.Width);
        int rows = (count + cols - 1) / cols;
        UpdateScroll(avail, new Size(cols * TileWidth, rows * TileHeight));

        int first = Math.Max(0, (int)(_offsetY / TileHeight) * cols);
        int last = Math.Min(count - 1, ((int)((_offsetY + avail.Height) / TileHeight) + 2) * cols - 1);

        _ = InternalChildren; // stellt sicher, dass der Generator existiert
        var gen = ItemContainerGenerator;
        if (count > 0 && last >= first)
        {
            var start = gen.GeneratorPositionFromIndex(first);
            int childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
            using (gen.StartAt(start, GeneratorDirection.Forward, true))
            {
                for (int i = first; i <= last; i++, childIndex++)
                {
                    var child = (UIElement)gen.GenerateNext(out bool isNew);
                    if (child == null) break;
                    if (isNew)
                    {
                        if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                        else InsertInternalChild(childIndex, child);
                        gen.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(TileWidth, TileHeight));
                }
            }
        }
        Cleanup(first, last);
        return avail;
    }

    void Cleanup(int first, int last)
    {
        var gen = ItemContainerGenerator;
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var pos = new GeneratorPosition(i, 0);
            int idx = gen.IndexFromGeneratorPosition(pos);
            if (idx < first || idx > last)
            {
                gen.Remove(pos, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    protected override Size ArrangeOverride(Size final)
    {
        int cols = Cols(final.Width);
        var gen = ItemContainerGenerator;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int idx = gen.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect(idx % cols * TileWidth, idx / cols * TileHeight, TileWidth, TileHeight));
        }
        return final;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Move)
            RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
        if (args.Action == NotifyCollectionChangedAction.Reset) SetVerticalOffset(0);
    }

    /// <summary>Wird von ListBox.ScrollIntoView/Tastaturnavigation aufgerufen.</summary>
    protected override void BringIndexIntoView(int index)
    {
        int row = index / Cols(_viewport.Width);
        double top = row * TileHeight;
        if (top < _offsetY) SetVerticalOffset(top);
        else if (top + TileHeight > _offsetY + _viewport.Height) SetVerticalOffset(top + TileHeight - _viewport.Height);
    }

    // ---------- IScrollInfo ----------

    void UpdateScroll(Size viewport, Size extent)
    {
        if (viewport == _viewport && extent == _extent) return;
        _viewport = viewport;
        _extent = extent;
        _offsetY = Math.Max(0, Math.Min(_offsetY, _extent.Height - _viewport.Height));
        _trans.Y = -_offsetY;
        ScrollOwner?.InvalidateScrollInfo();
    }

    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offsetY;
    public ScrollViewer ScrollOwner { get; set; }

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, _extent.Height - _viewport.Height));
        if (double.IsNaN(offset) || offset == _offsetY) return;
        _offsetY = offset;
        _trans.Y = -offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetHorizontalOffset(double offset) { }
    public void LineUp() => SetVerticalOffset(_offsetY - 40);
    public void LineDown() => SetVerticalOffset(_offsetY + 40);
    public void PageUp() => SetVerticalOffset(_offsetY - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offsetY + _viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(_offsetY - 120);
    public void MouseWheelDown() => SetVerticalOffset(_offsetY + 120);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement el && InternalChildren.IndexOf(el) is int i and >= 0)
            BringIndexIntoView(ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0)));
        return rectangle;
    }
}
