using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground;

/// <summary>
/// <c>--measure-latency &lt;file&gt; [seconds]</c> — P2: how far the engine's poses are from the frames
/// that show them. A box ping-pongs linearly (300 DIP/s) on the engine; every ticker dispatch sends
/// the pose time to a probe on the RENDER thread (a <see cref="CompositionCustomVisualHandler"/>),
/// which stamps every server frame with the latest pose it received. Measured:
///  D — dispatch time minus the RAF frame time: the jitter P2 ("use the RAF time") would remove;
///  L — server frame time minus the pose time it shows: the pose-to-frame latency, whose spread is
///      the visible jitter (spread × velocity = position error);
///  repeats — server frames that showed the previous pose again while moving (a held frame).
/// Two phases: idle UI thread, then 0–8 ms of random UI-thread work per frame.
/// </summary>
internal static class LatencyMeasurement
{
    private const double Speed = 300; // DIP/s: 300 DIP in 1 s, ping-pong

    public static void Run(Window window, string path, double seconds)
    {
        var canvas = new Canvas { Background = Brushes.White };
        var box = new Border { Width = 40, Height = 40, Background = Brushes.SteelBlue };
        Canvas.SetTop(box, 40);
        var host = new Border { Width = 1, Height = 1 };
        canvas.Children.Add(box);
        canvas.Children.Add(host);
        window.Content = canvas;
        window.Width = 420;
        window.Height = 140;

        var report = new StringBuilder();
        var x = Animate.For(box).TranslateX;
        x.Track(0);
        x.Track(300);

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(500);
            var compositor = ElementComposition.GetElementVisual(host)!.Compositor;
            var handler = new ProbeHandler();
            var probe = compositor.CreateCustomVisual(handler);
            probe.Size = new Vector(1, 1);
            ElementComposition.SetElementChildVisual(host, probe);
            probe.SendHandlerMessage(ProbeHandler.Start);

            double target = 300;
            bool legs = true;
            void Leg()
            {
                if (!legs)
                    return;
                new Choreography()
                    .And(x.To(target).Over(TimeSpan.FromSeconds(Math.Abs(target - x.Value) / Speed)))
                    .Then(() => { target = 300 - target; Leg(); })
                    .Start(box);
            }
            Leg();
            await Task.Delay(1000); // warm-up

            await Phase("A: idle UI thread", busyMs: 0);
            await Phase("B: 0-8 ms of random UI-thread work per frame", busyMs: 8);

            legs = false;
            File.WriteAllText(path, report.ToString());
            window.Close();

            async Task Phase(string name, double busyMs)
            {
                var dispatches = new List<double>();
                var dispatchMinusRaf = new List<double>();
                double lastDispatch = double.NaN;
                var rng = new Random(42);
                var raf = TopLevel.GetTopLevel(box)!;
                bool running = true;

                // Our own RAF chain, registered AFTER the ticker's: every Pulse runs the ticker's
                // dispatch first, then this callback with the same frame time t — so the pair
                // (this frame's dispatch time, this frame's RAF time) is exact.
                void OnRaf(TimeSpan t)
                {
                    if (!double.IsNaN(lastDispatch))
                        dispatchMinusRaf.Add(lastDispatch - t.TotalMilliseconds);
                    lastDispatch = double.NaN;
                    if (running)
                        raf.RequestAnimationFrame(OnRaf);
                }
                raf.RequestAnimationFrame(OnRaf);

                handler.Records.Clear();
                using var sub = MotionTicker.Subscribe(box, now =>
                {
                    double ms = now.TotalMilliseconds;
                    dispatches.Add(ms);
                    probe.SendHandlerMessage(ms); // the pose time of this frame
                    lastDispatch = ms;
                    if (busyMs > 0)
                    {
                        var until = Stopwatch.GetTimestamp() + (long)(rng.NextDouble() * busyMs * Stopwatch.Frequency / 1000);
                        while (Stopwatch.GetTimestamp() < until) { }
                    }
                });
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                running = false;
                Write(name, dispatches, dispatchMinusRaf, handler.Records.ToArray());
            }
        });

        void Write(string name, List<double> dispatches, List<double> d, (double Frame, double Pose)[] frames)
        {
            var ci = CultureInfo.InvariantCulture;
            report.AppendLine(ci, $"--- {name}");
            var ui = Intervals(dispatches);
            var server = Intervals(frames.Select(f => f.Frame).ToList());
            report.AppendLine(ci, $"ui_frames      {dispatches.Count,6}   interval {Stats(ui)}");
            report.AppendLine(ci, $"server_frames  {frames.Length,6}   interval {Stats(server)}");
            // D relative to its own mean: only its spread matters (both clocks have their own epoch).
            double dm = d.Count > 0 ? d.Average() : 0;
            report.AppendLine(ci, $"D dispatch-RAF (centered)        {Stats(d.Select(v => v - dm).ToList())}");

            var shown = frames.Where(f => !double.IsNaN(f.Pose)).ToArray();
            var latency = new List<double>();
            int repeats = 0, skipped = 0;
            for (int i = 1; i < shown.Length; i++)
            {
                if (shown[i].Pose == shown[i - 1].Pose)
                {
                    repeats++;
                    continue;
                }
                latency.Add(shown[i].Frame - shown[i].Pose);
                // Poses committed between two server frames that never reached the screen.
                skipped += dispatches.Count(p => p > shown[i - 1].Pose && p < shown[i].Pose);
            }
            report.AppendLine(ci, $"L frame-pose latency             {Stats(latency)}");
            if (latency.Count > 1)
            {
                double sd = StdDev(latency);
                report.AppendLine(ci, $"  -> position jitter at {Speed} DIP/s: sd {sd * Speed / 1000:0.00} DIP");
            }
            report.AppendLine(ci, $"repeated frames (pose shown twice) {repeats} of {shown.Length} ({100.0 * repeats / Math.Max(1, shown.Length):0.0} %)");
            report.AppendLine(ci, $"poses never shown                  {skipped}");
            report.AppendLine();
        }
    }

    private static List<double> Intervals(List<double> t) => t.Zip(t.Skip(1), (a, b) => b - a).ToList();

    private static double StdDev(List<double> v)
    {
        double m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
    }

    private static string Stats(List<double> v)
    {
        if (v.Count < 2)
            return "n/a";
        var s = v.OrderBy(x => x).ToArray();
        double P(double q) => s[(int)Math.Min(s.Length - 1, q * s.Length)];
        return string.Create(CultureInfo.InvariantCulture,
            $"mean {v.Average(),6:0.00} ms  sd {StdDev(v),5:0.00}  p5 {P(0.05),6:0.00}  p50 {P(0.5),6:0.00}  p95 {P(0.95),6:0.00}  max {s[^1],6:0.00}");
    }

    /// <summary>Render-thread side: stamps every server frame with the latest pose time received.</summary>
    private sealed class ProbeHandler : CompositionCustomVisualHandler
    {
        public static readonly object Start = new();
        public readonly ConcurrentQueue<(double Frame, double Pose)> Records = new();
        private double _lastPose = double.NaN;

        public override void OnMessage(object message)
        {
            if (message is double pose)
                _lastPose = pose;
            if (ReferenceEquals(message, Start) || message is double)
                RegisterForNextAnimationFrameUpdate();
        }

        public override void OnAnimationFrameUpdate()
        {
            // The server frame's start, on the same clock as the ticker (MotionTicker.Now is a Stopwatch read).
            Records.Enqueue((MotionTicker.Now.TotalMilliseconds, _lastPose));
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext) { }
    }
}
