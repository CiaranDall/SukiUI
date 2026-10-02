using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using SukiUI.Motion.Playground.Scenes;

namespace SukiUI.Motion.Playground;

/// <summary>
/// Unattended frame-timing measurement of scene C1 (engine side): records every engine
/// dispatch (SukiTicker time) and every half-cycle start, then writes a plain-text report.
/// The composition side cannot be observed from the UI thread (ENGINEERING_NOTES §7.5);
/// its half-cycle is exactly the configured duration by construction (server clock).
/// </summary>
internal static class FrameMeasurement
{
    private const double HalfCycleMs = 700; // UiThreadStallScene's configured half period

    public static void Run(Window window, string path, double seconds)
    {
        var dispatches = new List<double>();
        var halfCycles = new List<double>();
        IDisposable? probe = null;

        // Warm-up first: JIT, first layout, the loop settling into its rhythm.
        DispatcherTimer.RunOnce(() =>
        {
            UiThreadStallScene.HalfCycleStarted = now => halfCycles.Add(now.TotalMilliseconds);
            // Rides the dispatches the choreography already causes (it writes every frame).
            probe = SukiTicker.Subscribe(UiThreadStallScene.EngineBox!, now => dispatches.Add(now.TotalMilliseconds));

            DispatcherTimer.RunOnce(() =>
            {
                probe.Dispose();
                UiThreadStallScene.HalfCycleStarted = null;
                File.WriteAllText(path, Report(dispatches, halfCycles, seconds));
                window.Close();
            }, TimeSpan.FromSeconds(seconds));
        }, TimeSpan.FromSeconds(1.5));
    }

    private static string Report(List<double> dispatches, List<double> halfCycles, double seconds)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var intervals = dispatches.Zip(dispatches.Skip(1), (a, b) => b - a).OrderBy(x => x).ToArray();
        double P(double q) => intervals.Length == 0 ? double.NaN : intervals[(int)Math.Min(intervals.Length - 1, q * intervals.Length)];

        sb.AppendLine(ci, $"window_s            {seconds}");
        sb.AppendLine(ci, $"engine_dispatches   {dispatches.Count}");
        sb.AppendLine(ci, $"engine_fps          {dispatches.Count / seconds:0.0}");
        if (intervals.Length > 0)
        {
            sb.AppendLine(ci, $"interval_ms_mean    {intervals.Average():0.00}");
            sb.AppendLine(ci, $"interval_ms_p50     {P(0.50):0.00}");
            sb.AppendLine(ci, $"interval_ms_p95     {P(0.95):0.00}");
            sb.AppendLine(ci, $"interval_ms_p99     {P(0.99):0.00}");
            sb.AppendLine(ci, $"interval_ms_min     {intervals[0]:0.00}");
            sb.AppendLine(ci, $"interval_ms_max     {intervals[^1]:0.00}");
            sb.AppendLine(ci, $"intervals_over_20ms {intervals.Count(x => x > 20)}");
            sb.AppendLine(ci, $"intervals_over_33ms {intervals.Count(x => x > 33)}");
        }
        var halves = halfCycles.Zip(halfCycles.Skip(1), (a, b) => b - a).ToArray();
        if (halves.Length > 0)
        {
            sb.AppendLine(ci, $"half_cycle_ms_mean  {halves.Average():0.0}   (composition: {HalfCycleMs} exactly)");
            sb.AppendLine(ci, $"half_cycle_ms_min   {halves.Min():0.0}");
            sb.AppendLine(ci, $"half_cycle_ms_max   {halves.Max():0.0}");
        }
        sb.AppendLine("intervals_ms_raw    " + string.Join(" ", dispatches.Zip(dispatches.Skip(1), (a, b) => (b - a).ToString("0.0", ci))));
        return sb.ToString();
    }
}
