using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// Headless Avalonia on the calling thread, initialized once per process: BenchmarkDotNet runs
/// [GlobalSetup] and the measured iterations on the same thread, which becomes the UI thread.
/// Headless drawing (no Skia): these benchmarks measure UI-thread CPU, not rendering.
/// </summary>
public static class UiSession
{
    private static bool _started;

    public static void Ensure()
    {
        if (_started)
            return;
        _started = true;
        AppBuilder.Configure<BenchApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();
    }

    private sealed class BenchApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
}
