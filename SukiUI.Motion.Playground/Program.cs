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

    [STAThread]
    public static int Main(string[] args)
    {
        int i = Array.IndexOf(args, "--measure-c1");
        if (i >= 0 && i + 1 < args.Length)
            MeasureC1 = (args[i + 1], i + 2 < args.Length && double.TryParse(args[i + 2],
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 6.0);
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
