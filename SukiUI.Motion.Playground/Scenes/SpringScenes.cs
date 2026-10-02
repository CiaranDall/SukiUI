using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground.Scenes;

public sealed class SpringHoverScene : Scene
{
    private static readonly Spring Bouncy = new(Omega: 14, Decay: 9);

    public override string BugId => "Баг 2";
    public override string Title => "Пружина во время пружины";
    public override string Steps =>
        "Hover на квадрате — пружина к 1.25, уход — пружина к 1.0. Быстро проведите курсором через квадрат, " +
        "не задерживаясь. График показывает масштаб.";
    public override string Before =>
        "Пружина ухода, пришедшая пока шла пружина входа, молча отбрасывалась: квадрат оставался увеличенным (1.25).";
    public override string Expected =>
        "Уход перехватывает пружину с сохранением скорости — квадрат возвращается к 1.0, на графике нет излома.";

    protected override Control Build()
    {
        var box = Box("#E07B39", 90);
        var scale = Animate.For(box).Scale;
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
    public override string BugId => "Баг 12";
    public override string Title => "Одиночная пружина телепортируется";
    public override string Steps =>
        "Нажмите «Пружина на свежем канале»: создаётся новый квадрат, и ему сразу даётся одна-единственная " +
        "Scale.To(1.4).Spring(...). Рядом то же для Chain (MustFinish).";
    public override string Before =>
        "Окно зажима позы собиралось только из целей: при одной цели оно вырождалось в [1.4, 1.4], " +
        "старт зажимался на цель — квадрат мгновенно оказывался в 1.4 без анимации. График — вертикальная ступенька.";
    public override string Expected => "Плавный пружинный рост 1.0 → 1.4 с перелётом; график — кривая.";

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
            current = Animate.For(box).Scale;
            current.Offer(make(current));
        }

        return Column(
            Row(
                Btn("Пружина на свежем канале", () => Fresh(c => c.To(1.4).Spring(new Spring(12, 8)))),
                Btn("Chain на свежем канале", () => Fresh(c => c.To(0.7).Over(TimeSpan.FromMilliseconds(500)).MustFinish()))),
            holder,
            trace);
    }
}

public sealed class StiffSpringScene : Scene
{
    public override string BugId => "Баг 10";
    public override string Title => "Жёсткие пружины и пружины без затухания";
    public override string Steps =>
        "Запустите жёсткую пружину (ω=400) и сильно демпфированную (ω=60, decay=400). Затем «decay = 0».";
    public override string Before =>
        "Шаг интегрирования 8 мс неустойчив для таких пружин: значение улетало в ±1e128 / NaN. " +
        "Пружина с decay = 0 принималась и колебалась вечно — движок не засыпал (dispatch/s внизу > 0 навсегда).";
    public override string Expected =>
        "Обе пружины спокойно приходят к 1.2. decay = 0 отклоняется: показывается ArgumentOutOfRangeException.";

    protected override Control Build()
    {
        var trace = new TraceView { Label = "probe", Min = 0.8, Max = 1.4 };
        var channel = Animate.For(trace).Property(TraceView.ValueProperty);
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
                Btn("Жёсткая ω=400, decay=560", () => Run(400, 560)),
                Btn("Демпфированная ω=60, decay=400", () => Run(60, 400)),
                Btn("decay = 0", () => Run(10, 0)),
                Btn("Остановить (поза 1.0)", () => channel.Offer(channel.Pose(1.0)))),
            trace,
            error);
    }
}

public sealed class OverdampedEaseScene : Scene
{
    public override string BugId => "Баг 11";
    public override string Title => "SpringEaseOut при ζ > 1";
    public override string Steps =>
        "Сплошные линии — SpringEaseOut движка, пунктир — точное решение уравнения пружины (считается здесь же). " +
        "«Проиграть» анимирует четыре полоски теми же easing за 900 мс.";
    public override string Before =>
        "При ζ > 1 вместо передемпфированного решения считалась критически демпфированная кривая: " +
        "сплошные линии для ζ = 1.3 и 2.5 сильно уходили вверх от пунктира (слишком быстрый старт).";
    public override string Expected => "Сплошные линии совпадают с пунктиром для всех ζ.";

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
            Btn("Проиграть", () =>
            {
                for (int i = 0; i < bars.Length; i++)
                {
                    var x = Animate.For(bars[i]).TranslateX;
                    var (_, omega, decay) = Curves[i];
                    x.Offer(x.Pose(0));
                    x.Offer(x.To(500).Over(TimeSpan.FromMilliseconds(900))
                        .Ease(new SpringEaseOut { Omega = omega, Decay = decay }));
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
            var engine = new SpringEaseOut { Omega = omega, Decay = decay };
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
