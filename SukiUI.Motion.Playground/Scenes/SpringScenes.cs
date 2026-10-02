using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
// The engine's Program collides with every app's Program entry class.
using MotionProgram = SukiUI.Motion.Program;

namespace SukiUI.Motion.Playground.Scenes;

public sealed class SpringHoverScene : Scene
{
    private static readonly Spring Bouncy = new(Omega: 14, Decay: 9);

    public override string BugId => "Fix 3";
    public override string Title => "A spring during a spring";
    public override string Steps =>
        "Hovering the square springs it to 1.25, leaving springs it back to 1.0. Sweep the pointer across " +
        "the square quickly, without stopping. The chart shows the scale.";
    public override string Before =>
        "The exit spring, arriving while the enter spring ran, was silently dropped: the square stayed enlarged (1.25).";
    public override string Expected =>
        "The exit takes over with the live velocity: the square returns to 1.0, with no kink in the chart.";

    protected override Control Build()
    {
        var box = Box("#E07B39", 90);
        var scale = Motion.For(box).Scale;
        var enter = scale.To(1.25).Spring(Bouncy);
        var exit = scale.To(1.0).Spring(Bouncy);
        // A Mover wires triggers directly: the public, XAML-free entry point.
        _ = new Mover(box)
            .OnEvent(InputElement.PointerEnteredEvent, enter)
            .OnEvent(InputElement.PointerExitedEvent, exit);

        var trace = new TraceView { Label = "Scale", Min = 0.9, Max = 1.4 }.Follow(() => scale.Value);
        var stage = new Border { Padding = new Avalonia.Thickness(40), Child = box, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        return Column(stage, trace);
    }
}

public sealed class LoneSpringScene : Scene
{
    public override string BugId => "Fix 4";
    public override string Title => "A lone spring teleports";
    public override string Steps =>
        "Press \"Spring on a fresh channel\": a new square is created and given a single " +
        "Scale.To(1.4).Spring(...) right away. Next to it, the same for a Chain (MustFinish).";
    public override string Before =>
        "The pose-clamp window was built from targets only: with one target it collapsed to [1.4, 1.4], " +
        "the start was clamped onto the target and the square jumped to 1.4 with no animation. The chart showed a vertical step.";
    public override string Expected => "A smooth spring growth 1.0 → 1.4 with an overshoot; the chart is a curve.";

    protected override Control Build()
    {
        var holder = new Border { Height = 160, Padding = new Avalonia.Thickness(40) };
        var trace = new TraceView { Label = "Scale", Min = 0.6, Max = 1.6 };
        Channel? current = null;
        trace.Follow(() => current?.Value ?? 1.0);

        void Fresh(Func<Channel, MotionProgram> make)
        {
            var box = Box("#2E9E6B", 70);
            holder.Child = box; // attached synchronously: the channel can start right away
            trace.Clear();
            current = Motion.For(box).Scale;
            current.Offer(make(current));
        }

        return Column(
            Row(
                Btn("Spring on a fresh channel", () => Fresh(c => c.To(1.4).Spring(new Spring(12, 8)))),
                Btn("Chain on a fresh channel", () => Fresh(c => c.To(0.7).Over(TimeSpan.FromMilliseconds(500)).MustFinish()))),
            holder,
            trace);
    }
}

public sealed class StiffSpringScene : Scene
{
    public override string BugId => "Fix 9";
    public override string Title => "Stiff springs and springs without damping";
    public override string Steps =>
        "Run the stiff spring (ω=400) and the heavily damped one (ω=60, decay=400). Then \"decay = 0\".";
    public override string Before =>
        "The 8 ms integration step is unstable for such springs: the value blew up to ±1e128 / NaN. " +
        "A spring with decay = 0 was accepted and oscillated forever: the engine never went idle (dispatch/s > 0 forever).";
    public override string Expected =>
        "Both springs settle calmly on 1.2. decay = 0 is rejected: an ArgumentOutOfRangeException is shown.";

    protected override Control Build()
    {
        var trace = new TraceView { Label = "probe", Min = 0.8, Max = 1.4 };
        var channel = Motion.For(trace).Property(TraceView.ValueProperty);
        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.DarkRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        void Run(double omega, double decay)
        {
            error.Text = "";
            try
            {
                var spring = new Spring(omega, decay);
                channel.Offer(channel.Pose(1.0));
                trace.Clear();
                channel.Offer(channel.To(1.2).Spring(spring));
            }
            catch (Exception ex)
            {
                error.Text = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        return Column(
            Row(
                Btn("Stiff ω=400, decay=560", () => Run(400, 560)),
                Btn("Damped ω=60, decay=400", () => Run(60, 400)),
                Btn("decay = 0", () => Run(10, 0)),
                Btn("Stop (pose 1.0)", () => channel.Offer(channel.Pose(1.0)))),
            trace,
            error);
    }
}

public sealed class OverdampedEaseScene : Scene
{
    public override string BugId => "Fix 10";
    public override string Title => "SukiSpringEaseOut for ζ > 1";
    public override string Steps =>
        "Solid lines: the engine's SukiSpringEaseOut; dashed: the exact solution of the spring equation (computed here). " +
        "\"Play\" animates four bars with the same easings over 900 ms.";
    public override string Before =>
        "For ζ > 1 the critically damped curve was evaluated instead of the overdamped solution: " +
        "the solid lines for ζ = 1.3 and 2.5 ran far above the dashed ones (a start much too fast).";
    public override string Expected => "The solid lines match the dashed ones for every ζ.";

    private static readonly (string Color, double Omega, double Decay)[] Curves =
    {
        ("#1565C0", 6.43, 9.0),   // zeta 0.70
        ("#2E7D32", 5.8, 12.18),  // zeta 1.05 — large-dialog default profile
        ("#EF6C00", 5.8, 15.08),  // zeta 1.30 — large-dialog alternate profile
        ("#C62828", 6.0, 30.0),   // zeta 2.50
    };

    protected override Control Build()
    {
        var chart = new EaseChart(Curves);
        var bars = Curves.Select(c => Box(c.Color, 24)).ToArray();
        var lanes = new StackPanel { Spacing = 6 };
        foreach (var bar in bars)
            lanes.Children.Add(new Border { Child = bar, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });

        return Column(
            chart,
            Btn("Play", () =>
            {
                for (int i = 0; i < bars.Length; i++)
                {
                    var x = Motion.For(bars[i]).TranslateX;
                    var (_, omega, decay) = Curves[i];
                    x.Offer(x.Pose(0));
                    x.Offer(x.To(500).Over(TimeSpan.FromMilliseconds(900))
                        .Ease(new SukiSpringEaseOut { Omega = omega, Decay = decay }));
                }
            }),
            lanes);
    }
}

/// <summary>Engine ease (solid) vs exact damped-spring step response (dashed).</summary>
internal sealed class EaseChart : Control
{
    private readonly (string Color, double Omega, double Decay)[] _curves;

    public EaseChart((string, double, double)[] curves)
    {
        _curves = curves;
        Height = 260;
        Width = 520;
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        var size = Bounds.Size;
        double Px(double t) => t * size.Width;
        double Py(double v) => size.Height - Math.Clamp(v, 0, 1.15) / 1.15 * size.Height;
        var grid = new Avalonia.Media.Pen(Avalonia.Media.Brushes.LightGray, 1);
        context.DrawRectangle(null, grid, new Avalonia.Rect(size));
        context.DrawLine(grid, new Avalonia.Point(0, Py(1)), new Avalonia.Point(size.Width, Py(1)));

        foreach (var (color, omega, decay) in _curves)
        {
            var brush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color));
            var engine = new SukiSpringEaseOut { Omega = omega, Decay = decay };
            double end = Exact(omega, decay, 1.0);
            Draw(context, new Avalonia.Media.Pen(brush, 2), t => engine.Ease(t), Px, Py);
            Draw(context, new Avalonia.Media.Pen(brush, 1.5) { DashStyle = Avalonia.Media.DashStyle.Dash },
                t => Exact(omega, decay, t) / end, Px, Py);
            double zeta = decay / 2 / omega;
            var label = new Avalonia.Media.FormattedText($"ζ={zeta:0.00}", System.Globalization.CultureInfo.InvariantCulture,
                Avalonia.Media.FlowDirection.LeftToRight, Avalonia.Media.Typeface.Default, 12, brush);
            context.DrawText(label, new Avalonia.Point(Px(0.12) + 2, Py(engine.Ease(0.12)) - 16));
        }
    }

    private static void Draw(Avalonia.Media.DrawingContext ctx, Avalonia.Media.Pen pen, Func<double, double> f,
        Func<double, double> px, Func<double, double> py)
    {
        var g = new Avalonia.Media.StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Avalonia.Point(px(0), py(f(0))), false);
            for (int i = 1; i <= 200; i++)
            {
                double t = i / 200.0;
                c.LineTo(new Avalonia.Point(px(t), py(f(t))));
            }
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }

    /// <summary>Step response of x'' = -omega²(x - 1) - decay·x' from rest at 0.</summary>
    private static double Exact(double omega, double decay, double t)
    {
        double a = decay / 2.0, zeta = a / omega;
        if (Math.Abs(zeta - 1.0) < 1e-9)
            return 1.0 - Math.Exp(-a * t) * (1.0 + a * t);
        if (zeta < 1.0)
        {
            double wd = omega * Math.Sqrt(1.0 - zeta * zeta);
            return 1.0 - Math.Exp(-a * t) * (Math.Cos(wd * t) + a / wd * Math.Sin(wd * t));
        }
        double wo = omega * Math.Sqrt(zeta * zeta - 1.0);
        return 1.0 - Math.Exp(-a * t) * (Math.Cosh(wo * t) + a / wo * Math.Sinh(wo * t));
    }
}
