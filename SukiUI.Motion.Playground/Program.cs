using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace SukiUI.Motion.Playground;

internal static class Program
{
    /// <summary><c>--measure-c1 &lt;report path&gt; [seconds]</c>: run scene C1 unattended, write a
    /// frame-timing report and exit (see <see cref="FrameMeasurement"/>).</summary>
    internal static (string Path, double Seconds)? MeasureC1;

    /// <summary><c>--measure-cost &lt;report path&gt; [boxes] [seconds]</c>: engine vs composition
    /// prototype CPU cost (see <see cref="CostMeasurement"/>).</summary>
    internal static (string Path, int Boxes, double Seconds)? MeasureCost;

    /// <summary><c>--measure-stall &lt;report path&gt;</c>: drawn vs model position after a UI-thread
    /// stall right after a composition start (see <see cref="StallMeasurement"/>).</summary>
    internal static string? MeasureStall;

    /// <summary><c>--measure-latency &lt;report path&gt; [seconds]</c>: engine pose time vs the render
    /// thread's frames (see <see cref="LatencyMeasurement"/>).</summary>
    internal static (string Path, double Seconds)? MeasureLatency;

    [STAThread]
    public static int Main(string[] args)
    {
        int i = Array.IndexOf(args, "--measure-c1");
        if (i >= 0 && i + 1 < args.Length)
            MeasureC1 = (args[i + 1], i + 2 < args.Length && double.TryParse(args[i + 2],
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 6.0);
        int c = Array.IndexOf(args, "--measure-cost");
        if (c >= 0 && c + 1 < args.Length)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int boxes = c + 2 < args.Length && int.TryParse(args[c + 2], inv, out var n) ? n : 200;
            double secs = c + 3 < args.Length && double.TryParse(args[c + 3], inv, out var d) ? d : 6.0;
            MeasureCost = (args[c + 1], boxes, secs);
        }
        int st = Array.IndexOf(args, "--measure-stall");
        if (st >= 0 && st + 1 < args.Length)
            MeasureStall = args[st + 1];
        int la = Array.IndexOf(args, "--measure-latency");
        if (la >= 0 && la + 1 < args.Length)
            MeasureLatency = (args[la + 1], la + 2 < args.Length && double.TryParse(args[la + 2],
                System.Globalization.CultureInfo.InvariantCulture, out var ls) ? ls : 6.0);
        return BuildApp(args);
    }

    private static int BuildApp(string[] args) =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light; // the charts use fixed light-theme colors
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
