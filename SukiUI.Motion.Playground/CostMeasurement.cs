using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SukiUI.Motion.Composition;

namespace SukiUI.Motion.Playground;

/// <summary>
/// <c>--measure-cost &lt;file&gt; [boxes] [seconds]</c> — CPU cost of N springing boxes, UI-thread
/// engine against the composition prototype (PLAN D21 acceptance, rule R3). Same schedule for both:
/// every 400 ms every box springs to a new random target (springs last longer than that, so most
/// starts are interruptions with velocity carry). Phases run back to back in one process:
/// idle baseline → engine → composition. Reported: process CPU (all threads: UI + render), the
/// engine's own UI-thread dispatch time, and the UI-thread time spent issuing the starts.
/// </summary>
internal static class CostMeasurement
{
    private static readonly Spring Spring = new(Omega: 10, Decay: 6);

    public static void Run(Window window, string path, int boxes, double seconds)
    {
        var grid = new Canvas { Background = Brushes.White };
        var items = new List<Border>();
        int perRow = Math.Max(1, (int)Math.Sqrt(boxes * 2));
        for (int i = 0; i < boxes; i++)
        {
            var b = new Border { Width = 14, Height = 14, Background = Brushes.SteelBlue };
            Canvas.SetLeft(b, 10 + (i % perRow) * 40);
            Canvas.SetTop(b, 10 + (i / perRow) * 22);
            grid.Children.Add(b);
            items.Add(b);
        }
        window.Content = grid;
        window.Width = 10 + perRow * 40 + 60;
        window.Height = 10 + (boxes / perRow + 1) * 22 + 40;

        var report = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        report.AppendLine(ci, $"boxes {boxes}, {seconds} s per phase, restart every 400 ms, spring omega=10 decay=6");
        report.AppendLine(ci, $"logical processors {Environment.ProcessorCount}");

        var rng = new Random(42);
        var phases = new (string Name, Action<Border, double>? Start)[]
        {
            ("idle", null),
            ("engine (UI thread)", (b, to) =>
            {
                var x = Animate.For(b).TranslateX;
                x.Track(0);
                x.Track(30);
                x.Offer(x.To(to).Spring(Spring));
            }),
            ("composition prototype", (b, to) => CompositionMotion.For(b).TranslateX.SpringTo(to, Spring)),
        };

        int phase = -1;
        DispatcherTimer? schedule = null;
        TimeSpan cpu0 = default;
        double dispatchMs0 = 0, startMs = 0;
        long dispatches0 = 0;
        var wall = Stopwatch.StartNew();

        void BeginPhase()
        {
            phase++;
            if (phase == phases.Length)
            {
                File.WriteAllText(path, report.ToString());
                window.Close();
                return;
            }
            var (_, start) = phases[phase];
            // Rest everything from the previous phase first (engine poses AND composition poses).
            foreach (var b in items)
            {
                Animate.For(b).TranslateX.Offer(Animate.For(b).TranslateX.Pose(0));
                CompositionMotion.For(b).TranslateX.Pose(0);
            }
            startMs = 0;
            if (start is not null)
            {
                schedule = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                schedule.Tick += (_, _) =>
                {
                    long t = Stopwatch.GetTimestamp();
                    foreach (var b in items)
                        start(b, rng.NextDouble() * 30);
                    startMs += Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                };
                schedule.Start();
            }
            // Measure after a 1 s warm-up within the phase.
            DispatcherTimer.RunOnce(() =>
            {
                cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
                dispatchMs0 = MotionStats.TotalDispatchMs;
                dispatches0 = MotionStats.DispatchCount;
                startMs = 0;
                wall.Restart();
                DispatcherTimer.RunOnce(EndPhase, TimeSpan.FromSeconds(seconds));
            }, TimeSpan.FromSeconds(1));
        }

        void EndPhase()
        {
            double elapsed = wall.Elapsed.TotalSeconds;
            double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalSeconds;
            double dispatchMs = MotionStats.TotalDispatchMs - dispatchMs0;
            long dispatches = MotionStats.DispatchCount - dispatches0;
            schedule?.Stop();
            schedule = null;
            report.AppendLine(ci,
                $"{phases[phase].Name,-22} process CPU {100 * cpu / elapsed,6:0.0}% of one core | " +
                $"engine dispatch {dispatchMs / elapsed,6:0.0} ms/s ({dispatches / elapsed:0} dispatch/s) | " +
                $"starts issued {startMs / elapsed,6:0.0} ms/s");
            BeginPhase();
        }

        DispatcherTimer.RunOnce(BeginPhase, TimeSpan.FromSeconds(1));
    }
}
