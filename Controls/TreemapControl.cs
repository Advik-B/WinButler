using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WinButler.Services;
using WinButler.Services.Mft;

namespace WinButler.Controls;

/// <summary>
/// WizTree-style treemap: every file/folder under <see cref="Root"/> is a rectangle whose area
/// is proportional to its size, packed with the squarified algorithm (rectangles kept as close
/// to square as possible) and nested to show the folder hierarchy. Hovering shows the path and
/// size; clicking a rectangle raises <see cref="NodeInvoked"/> so the host can drill in.
/// </summary>
public sealed class TreemapControl : Control
{
    private const int MaxDepth = 8;
    private const double MinCellForChildren = 28; // px — below this we stop nesting
    private const double LabelMinWidth = 46;
    private const double LabelMinHeight = 16;

    /// <summary>Leaf rectangles actually drawn this frame, smallest-last for hit testing.</summary>
    private readonly List<(DiskNode node, Rect rect)> _hitRects = new();

    // Theme brushes, resolved once per render pass from this control's position in the
    // visual tree (which cascades into the Styles-merged token dictionaries).
    private IBrush _labelBrush = Brushes.White;
    private Typeface _labelTypeface = Typeface.Default;

    public static readonly StyledProperty<DiskNode?> RootProperty =
        AvaloniaProperty.Register<TreemapControl, DiskNode?>(nameof(Root));

    public DiskNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    /// <summary>Raised when the user clicks a rectangle.</summary>
    public event EventHandler<DiskNode>? NodeInvoked;

    static TreemapControl()
    {
        AffectsRender<TreemapControl>(RootProperty);
        AffectsMeasure<TreemapControl>(RootProperty);
    }

    public TreemapControl()
    {
        ClipToBounds = true;
        Focusable = true; // keyboard: arrows cycle cells, Enter/Space drills in
    }

    /// <summary>Index into <see cref="_hitRects"/> of the keyboard-selected cell; -1 = none.</summary>
    private int _keyboardIndex = -1;

    public override void Render(DrawingContext context)
    {
        _hitRects.Clear();

        _labelBrush = ResolveBrush("WbTextBrush", Brushes.White);
        _labelTypeface = this.TryFindResource("WbFontUi", out var font) && font is FontFamily family
            ? new Typeface(family, weight: FontWeight.Bold)
            : Typeface.Default;

        var full = new Rect(Bounds.Size);
        context.FillRectangle(ResolveBrush("WbSurfaceBrush", new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B))), full);

        var root = Root;
        if (root is null || full.Width < 2 || full.Height < 2)
            return;

        if (root.Children.Count == 0)
        {
            DrawCell(context, root, full, depth: 0, hue: 210);
            DrawFocusIndicators(context, full);
            return;
        }

        foreach (var (child, rect) in Squarify(root.Children, full.Deflate(1)))
            DrawNode(context, child, rect, depth: 0);

        DrawFocusIndicators(context, full);
    }

    private void DrawFocusIndicators(DrawingContext ctx, Rect full)
    {
        if (!IsFocused)
            return;
        ctx.DrawRectangle(null, new Pen(ResolveBrush("WbAccentBrush", Brushes.White), 1), full.Deflate(0.5));
        if (_keyboardIndex >= 0 && _keyboardIndex < _hitRects.Count)
            ctx.DrawRectangle(null, new Pen(ResolveBrush("WbAccentBrightBrush", Brushes.White), 2),
                _hitRects[_keyboardIndex].rect);
    }

    private void DrawNode(DrawingContext ctx, DiskNode node, Rect rect, int depth)
    {
        if (rect.Width < 1 || rect.Height < 1)
            return;

        double hue = HueFor(node);

        bool nested = false;
        if (depth < MaxDepth && node.HasChildren &&
            rect.Width > MinCellForChildren && rect.Height > MinCellForChildren)
        {
            // Reserve a thin header strip for the folder's own label, nest children below it.
            var inner = new Rect(rect.X + 2, rect.Y + LabelMinHeight, rect.Width - 4, rect.Height - LabelMinHeight - 2);
            if (inner.Width > 6 && inner.Height > 6)
            {
                ctx.FillRectangle(new SolidColorBrush(HsvToColor(hue, 0.45, 0.30)), rect);
                foreach (var (child, crect) in Squarify(node.Children, inner))
                    DrawNode(ctx, child, crect, depth + 1);
                DrawLabel(ctx, node, new Rect(rect.X, rect.Y, rect.Width, LabelMinHeight));
                ctx.DrawRectangle(null, new Pen(Brushes.Black, 1), rect);
                nested = true;
            }
        }

        if (!nested)
            DrawCell(ctx, node, rect, depth, hue);
    }

    private void DrawCell(DrawingContext ctx, DiskNode node, Rect rect, int depth, double hue)
    {
        double value = Math.Clamp(0.55 + depth * 0.05, 0.45, 0.9);
        ctx.FillRectangle(new SolidColorBrush(HsvToColor(hue, node.IsDirectory ? 0.35 : 0.6, value)), rect);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), 1), rect);

        DrawLabel(ctx, node, rect);
        _hitRects.Add((node, rect));
    }

    private IBrush ResolveBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

    private void DrawLabel(DrawingContext ctx, DiskNode node, Rect rect)
    {
        if (rect.Width < LabelMinWidth || rect.Height < LabelMinHeight)
            return;

        var text = new FormattedText(
            node.Name,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            11,
            _labelBrush)
        {
            MaxTextWidth = Math.Max(0, rect.Width - 6),
            MaxTextHeight = rect.Height,
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1,
        };

        using (ctx.PushClip(rect))
            ctx.DrawText(text, new Point(rect.X + 3, rect.Y + 2));
    }

    // ---- Interaction ---------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var node = HitTest(e.GetPosition(this));
        if (node is not null)
            NodeInvoked?.Invoke(this, node);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_hitRects.Count == 0)
            return;

        switch (e.Key)
        {
            case Key.Right or Key.Down:
                _keyboardIndex = (_keyboardIndex + 1) % _hitRects.Count;
                ShowKeyboardTip();
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Left or Key.Up:
                _keyboardIndex = _keyboardIndex <= 0 ? _hitRects.Count - 1 : _keyboardIndex - 1;
                ShowKeyboardTip();
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Enter or Key.Space:
                if (_keyboardIndex >= 0 && _keyboardIndex < _hitRects.Count)
                {
                    NodeInvoked?.Invoke(this, _hitRects[_keyboardIndex].node);
                    e.Handled = true;
                }
                break;
        }
    }

    private void ShowKeyboardTip()
    {
        if (_keyboardIndex < 0 || _keyboardIndex >= _hitRects.Count)
            return;
        var node = _hitRects[_keyboardIndex].node;
        ToolTip.SetTip(this, $"{node.FullPath}\n{SizeFormatter.Format(node.SizeBytes)}");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RootProperty)
            _keyboardIndex = -1; // the cells this indexed no longer exist
        if (change.Property == IsFocusedProperty)
            InvalidateVisual(); // show/hide the focus indicators
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var node = HitTest(e.GetPosition(this));
        ToolTip.SetTip(this, node is null ? null : $"{node.FullPath}\n{SizeFormatter.Format(node.SizeBytes)}");
    }

    private DiskNode? HitTest(Point p)
    {
        // Smallest-last: iterate in reverse so the deepest/innermost rectangle wins.
        for (int i = _hitRects.Count - 1; i >= 0; i--)
            if (_hitRects[i].rect.Contains(p))
                return _hitRects[i].node;
        return null;
    }

    // ---- Squarified treemap layout -------------------------------------------------------

    private static List<(DiskNode node, Rect rect)> Squarify(IReadOnlyList<DiskNode> nodes, Rect area)
    {
        var result = new List<(DiskNode, Rect)>();
        var items = new List<DiskNode>();
        double totalValue = 0;
        foreach (var n in nodes)
        {
            if (n.SizeBytes <= 0) continue;
            items.Add(n);
            totalValue += n.SizeBytes;
        }

        double totalArea = area.Width * area.Height;
        if (items.Count == 0 || totalArea <= 0 || totalValue <= 0)
            return result;

        double scale = totalArea / totalValue;
        double x = area.X, y = area.Y, w = area.Width, h = area.Height;

        var rowNodes = new List<DiskNode>();
        var rowAreas = new List<double>();

        int i = 0;
        while (i < items.Count)
        {
            double shortSide = Math.Min(w, h);
            double nextArea = items[i].SizeBytes * scale;

            if (rowAreas.Count == 0 || Worst(rowAreas, shortSide) >= WorstWith(rowAreas, nextArea, shortSide))
            {
                rowNodes.Add(items[i]);
                rowAreas.Add(nextArea);
                i++;
            }
            else
            {
                LayoutRow(result, rowNodes, rowAreas, ref x, ref y, ref w, ref h);
                rowNodes.Clear();
                rowAreas.Clear();
            }
        }
        if (rowAreas.Count > 0)
            LayoutRow(result, rowNodes, rowAreas, ref x, ref y, ref w, ref h);

        return result;
    }

    private static void LayoutRow(
        List<(DiskNode, Rect)> result, List<DiskNode> rowNodes, List<double> rowAreas,
        ref double x, ref double y, ref double w, ref double h)
    {
        double rowSum = 0;
        foreach (var a in rowAreas) rowSum += a;
        if (rowSum <= 0) return;

        if (w >= h)
        {
            // Vertical strip on the left, items stacked top-to-bottom.
            double stripW = rowSum / h;
            double offY = y;
            foreach (var (node, area) in Pairs(rowNodes, rowAreas))
            {
                double cellH = area / stripW;
                result.Add((node, new Rect(x, offY, stripW, cellH)));
                offY += cellH;
            }
            x += stripW;
            w -= stripW;
        }
        else
        {
            // Horizontal strip on the top, items left-to-right.
            double stripH = rowSum / w;
            double offX = x;
            foreach (var (node, area) in Pairs(rowNodes, rowAreas))
            {
                double cellW = area / stripH;
                result.Add((node, new Rect(offX, y, cellW, stripH)));
                offX += cellW;
            }
            y += stripH;
            h -= stripH;
        }
    }

    private static IEnumerable<(DiskNode node, double area)> Pairs(List<DiskNode> nodes, List<double> areas)
    {
        for (int i = 0; i < nodes.Count; i++)
            yield return (nodes[i], areas[i]);
    }

    /// <summary>Worst (largest) aspect ratio among a row of areas laid along a side of given length.</summary>
    private static double Worst(List<double> areas, double length)
    {
        double sum = 0, max = double.MinValue, min = double.MaxValue;
        foreach (var a in areas)
        {
            sum += a;
            if (a > max) max = a;
            if (a < min) min = a;
        }
        if (sum <= 0 || length <= 0) return double.MaxValue;
        double l2 = length * length, s2 = sum * sum;
        return Math.Max(l2 * max / s2, s2 / (l2 * min));
    }

    private static double WorstWith(List<double> areas, double extra, double length)
    {
        double sum = extra, max = extra, min = extra;
        foreach (var a in areas)
        {
            sum += a;
            if (a > max) max = a;
            if (a < min) min = a;
        }
        if (sum <= 0 || length <= 0) return double.MaxValue;
        double l2 = length * length, s2 = sum * sum;
        return Math.Max(l2 * max / s2, s2 / (l2 * min));
    }

    // ---- Colour --------------------------------------------------------------------------

    private static double HueFor(DiskNode node)
    {
        // Stable FNV-1a hash of the name (or extension for files) → hue. No RNG (unavailable).
        string key = node.IsDirectory ? node.Name : ExtensionOf(node.Name);
        uint hash = 2166136261;
        foreach (char c in key)
            hash = (hash ^ char.ToLowerInvariant(c)) * 16777619;
        return hash % 360u;
    }

    private static string ExtensionOf(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot) : name;
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double xx = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = xx; b = 0; }
        else if (h < 120) { r = xx; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = xx; }
        else if (h < 240) { r = 0; g = xx; b = c; }
        else if (h < 300) { r = xx; g = 0; b = c; }
        else { r = c; g = 0; b = xx; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
