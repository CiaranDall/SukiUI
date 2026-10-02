using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// Headless Avalonia on the calling thread, initialized once per process: BenchmarkDotNet runs
/// [GlobalSetup] and the measured iterations on the same thread, which becomes the UI thread.
/// Headless drawing by default (UI-thread CPU only); <see cref="EnsureSkia"/> renders for real with
/// Skia's CPU rasterizer — the software-rendering worst case. One mode per process: BenchmarkDotNet
/// runs every case in its own process.
/// </summary>
public static class UiSession
{
    private static bool _started;

    public static void Ensure() => Start(skia: false);

    public static void EnsureSkia() => Start(skia: true);

    private static void Start(bool skia)
    {
        if (_started)
            return;
        _started = true;
        var builder = AppBuilder.Configure<BenchApp>();
        if (skia)
            builder = builder.UseSkia();
        builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !skia })
            .SetupWithoutStarting();
    }

    private sealed class BenchApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
}
