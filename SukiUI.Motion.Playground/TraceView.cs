using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground;

/// <summary>
/// A scrolling plot of one value over the last few seconds. Either animate its
/// <see cref="ValueProperty"/> directly through a motion channel
/// (<c>Motion.For(trace).Property(TraceView.ValueProperty)</c>), or <see cref="Follow"/>
/// any value (e.g. <c>channel.Value</c>). Out-of-range samples are drawn at the edge and
/// the raw number is printed — NaN and 1e128 stay readable.
/// </summary>
public sealed class TraceView : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<TraceView, double>(nameof(Value), 1.0);

    private const double WindowSeconds = 3.0;
    private readonly List<(double T, double V)> _samples = new();
    private DispatcherTimer? _follow;

    public TraceView()
    {
        Height = 140;
        MinWidth = 360;
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Min { get; init; } = 0.8;
    public double Max { get; init; } = 1.5;
    public string Label { get; init; } = "value";

    /// <summary>Samples <paramref name="read"/> every 15 ms while attached.</summary>
    public TraceView Follow(Func<double> read)
    {
        _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _follow.Tick += (_, _) =>
        {
            double v = read();
            if (!v.Equals(Value))
                Value = v;
        };
        AttachedToVisualTree += (_, _) => _follow.Start();
        DetachedFromVisualTree += (_, _) => _follow.Stop();
        return this;
    }

    public void Clear()
    {
        _samples.Clear();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty)
            return;
        double now = SukiMotionStats.Now.TotalSeconds;
        _samples.Add((now, Value));
        _samples.RemoveAll(s => s.T < now - WindowSeconds);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#FAFAFA")), new Rect(size));
        var grid = new Pen(new SolidColorBrush(Color.Parse("#DDDDDD")), 1);
        context.DrawRectangle(null, grid, new Rect(size));

        double Y(double v)
        {
            double c = double.IsNaN(v) ? Min : Math.Clamp(v, Min, Max);
            return size.Height - (c - Min) / (Max - Min) * size.Height;
        }

        // Reference lines at round values inside the range.
        for (double v = Math.Ceiling(Min * 10) / 10; v <= Max; v += 0.1)
            context.DrawLine(grid, new Point(0, Y(v)), new Point(size.Width, Y(v)));

        if (_samples.Count > 1)
        {
            double right = _samples[^1].T;
            double X(double t) => size.Width - (right - t) / WindowSeconds * size.Width;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(X(_samples[0].T), Y(_samples[0].V)), false);
                for (int i = 1; i < _samples.Count; i++)
                    ctx.LineTo(new Point(X(_samples[i].T), Y(_samples[i].V)));
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(Brushes.SteelBlue, 2), geometry);
        }

        var text = new FormattedText(
            $"{Label} = {Value.ToString("G6", CultureInfo.InvariantCulture)}   [{Min} … {Max}]",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.Black);
        context.DrawText(text, new Point(6, 4));
    }
}
