using System.Windows;
using System.Windows.Media;

namespace LivewireBrowser.App;

/// <summary>
/// Classic stereo phase scope (goniometer): plots each L/R sample pair rotated 45° (mid/side
/// axes) so a correlated, mono-ish signal draws a vertical band — the same convention as
/// hardware phase scopes and the reference screenshot this was built against — while a fully
/// decorrelated/out-of-phase signal spreads into a round blob or a horizontal band.
///
/// Custom-drawn (FrameworkElement.OnRender) rather than an ItemsControl/Polyline of WPF
/// shapes: there's no existing scatter/point-cloud rendering pattern in this codebase (the
/// closest precedent, MainWindow.xaml.cs's BuildLufsScale, is also procedural code-behind
/// rather than data-bound XAML, for the same reason — a UI element built from a stream of
/// values doesn't map cleanly onto a static visual tree) and hundreds of points per frame
/// would be far more elements than this app's UI otherwise creates.
/// </summary>
public class PhasescopeControl : FrameworkElement
{
    // Points persist across several Plot() calls (≈ a few render frames) instead of being
    // replaced every call — otherwise, since each batch only carries a subsampled fraction of
    // a single WASAPI Read() (see PhaseScopeMeter.MaxPointsPerRead), the dots would flicker in
    // and out instead of looking like the continuous trace a real phase scope shows.
    private const int MaxBufferedPoints = 3000;

    private readonly (float Left, float Right)[] _ringBuffer = new (float, float)[MaxBufferedPoints];
    private int _head;
    private int _count;

    private static readonly Pen GridPen = CreatePen(Color.FromRgb(0x4A, 0x51, 0x62));
    private static readonly Brush DotBrush = CreateBrush(Color.FromRgb(0x98, 0xC3, 0x79));

    /// <summary>
    /// Multiplies how far points are plotted from center — a quiet signal otherwise draws a
    /// tiny dot/sliver in the middle of the scope instead of a readable trace. Bound from the
    /// gain Slider in MainWindow.xaml; AffectsRender so moving the slider redraws immediately
    /// without an explicit InvalidateVisual() call. Points are clipped to the control bounds
    /// (ClipToBounds="True" in XAML) so a high gain can't draw outside the scope's square.
    /// </summary>
    public static readonly DependencyProperty GainProperty = DependencyProperty.Register(
        nameof(Gain), typeof(double), typeof(PhasescopeControl),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Gain
    {
        get => (double)GetValue(GainProperty);
        set => SetValue(GainProperty, value);
    }

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        pen.Freeze();
        return pen;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Plot(IReadOnlyList<(float Left, float Right)> pairs)
    {
        foreach (var pair in pairs)
        {
            _ringBuffer[_head] = pair;
            _head = (_head + 1) % MaxBufferedPoints;
            if (_count < MaxBufferedPoints)
                _count++;
        }

        InvalidateVisual();
    }

    /// <summary>Drops every buffered point so the scope goes blank — called when playback
    /// stops, otherwise the last trace before Stop would just sit there frozen forever,
    /// looking like a still-live signal.</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
            return;

        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var radius = size / 2 - 4;

        DrawGuides(dc, cx, cy, radius);
        DrawPoints(dc, cx, cy, radius);
    }

    private static void DrawGuides(DrawingContext dc, double cx, double cy, double radius)
    {
        dc.DrawLine(GridPen, new Point(cx, cy - radius), new Point(cx, cy + radius));
        dc.DrawLine(GridPen, new Point(cx - radius, cy), new Point(cx + radius, cy));

        var diag = radius * 0.92;
        dc.DrawLine(GridPen, new Point(cx - diag, cy - diag), new Point(cx + diag, cy + diag));
        dc.DrawLine(GridPen, new Point(cx - diag, cy + diag), new Point(cx + diag, cy - diag));
    }

    private void DrawPoints(DrawingContext dc, double cx, double cy, double radius)
    {
        // Mid/side rotation: x = (R-L), y = -(R+L) — a mono-correlated signal (L≈R) collapses
        // x toward 0 and spreads along y, drawing the vertical band a real phase scope shows.
        var gain = Gain;
        for (var i = 0; i < _count; i++)
        {
            var (left, right) = _ringBuffer[i];
            var x = cx + (right - left) * 0.5 * radius * gain;
            var y = cy - (right + left) * 0.5 * radius * gain;
            dc.DrawRectangle(DotBrush, null, new Rect(x - 0.6, y - 0.6, 1.2, 1.2));
        }
    }
}
