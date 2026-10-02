using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using BenchmarkDotNet.Attributes;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// P4: the popup's motion blur and the item cascade put a <see cref="BlurEffect"/> (one offscreen
/// layer + blur filter each) on the popup root and on every cascading item at once. Measured: one
/// full frame (render + readback) of a 300×400 popup-like panel with N items, without effects and
/// with every item blurred, on Skia's CPU rasterizer — the software-rendering worst case (no GPU:
/// VMs, RDP, some Linux setups). The readback is paid equally by every case.
/// </summary>
[MemoryDiagnoser]
public class BlurBenchmarks
{
    private Window _window = null!;
    private readonly List<Border> _items = new();
    private Border _root = null!;
    private bool _flip;

    [Params(1, 10)]
    public int Items;

    /// <summary>none / items (the cascade) / root (the popup's motion blur) / both.</summary>
    [Params("none", "items", "root", "both")]
    public string Blur = "none";

    [GlobalSetup]
    public void Setup()
    {
        UiSession.EnsureSkia();
        var list = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        for (int i = 0; i < Items; i++)
        {
            var item = new Border
            {
                Height = 32,
                Background = Brushes.WhiteSmoke,
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock { Text = $"Item {i}", Margin = new Thickness(8, 6) },
            };
            _items.Add(item);
            list.Children.Add(item);
        }
        _root = new Border { Width = 300, Height = 400, Background = Brushes.White, Child = list };
        _window = new Window { Width = 320, Height = 420, Background = Brushes.LightGray, Content = _root };
        _window.Show();
        Apply(6.0);
        _window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
    }

    // Like the engine (OwnedEffects.Blur): one BlurEffect per visual, its Radius mutated per frame.
    private void Apply(double radius)
    {
        if (Blur is "items" or "both")
            foreach (var item in _items)
                Set(item, radius);
        if (Blur is "root" or "both")
            Set(_root, radius);

        static void Set(Visual v, double radius)
        {
            if (v.Effect is BlurEffect blur)
                blur.Radius = radius;
            else
                v.Effect = new BlurEffect { Radius = radius };
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _window.Close();

    /// <summary>One animation frame: the blur radius (or, without blur, the opacity) changes slightly,
    /// so the whole panel is redrawn — as it is on every frame of the animation.</summary>
    [Benchmark]
    public void Frame()
    {
        _flip = !_flip;
        if (Blur == "none")
            _root.Opacity = _flip ? 1.0 : 0.999;
        else
            Apply(_flip ? 6.0 : 6.01);
        _window.CaptureRenderedFrame()?.Dispose();
    }
}
