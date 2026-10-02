using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BenchmarkDotNet.Attributes;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// Baseline (phase 3): what one channel write costs the UI thread — the per-frame, per-channel
/// work of the engine besides the physics (CWT lookup of the transform block or effect, property
/// change, invalidation). The element is shown in a window, so invalidation reaches the renderer.
/// Rendering itself is not included (headless drawing).
/// </summary>
[MemoryDiagnoser]
public class ChannelWriteBenchmarks
{
    private const int Writes = 1000;
    private Window _window = null!;
    private Channel _translate = null!, _scale = null!, _opacity = null!, _blur = null!;

    [GlobalSetup]
    public void Setup()
    {
        UiSession.Ensure();
        var box = new Border { Width = 40, Height = 40, Background = Brushes.Black };
        _window = new Window { Width = 200, Height = 200, Content = box };
        _window.Show();
        var s = Motion.For(box);
        (_translate, _scale, _opacity, _blur) = (s.TranslateX, s.Scale, s.Opacity, s.Blur);
        _translate.Write(1);
        _scale.Write(1);
        _opacity.Write(1);
        _blur.Write(2);
        Dispatcher.UIThread.RunJobs();
    }

    [GlobalCleanup]
    public void Cleanup() => _window.Close();

    [Benchmark(OperationsPerInvoke = Writes)]
    public void TranslateX() => Sweep(_translate, 0, 100);

    [Benchmark(OperationsPerInvoke = Writes)]
    public void Scale() => Sweep(_scale, 0.9, 1.1);

    [Benchmark(OperationsPerInvoke = Writes)]
    public void Opacity() => Sweep(_opacity, 0.2, 1.0);

    [Benchmark(OperationsPerInvoke = Writes)]
    public void Blur() => Sweep(_blur, 1.0, 8.0);

    private static void Sweep(Channel channel, double from, double to)
    {
        for (int i = 0; i < Writes; i++)
            channel.Write(from + (to - from) * (i / (double)Writes));
    }
}
