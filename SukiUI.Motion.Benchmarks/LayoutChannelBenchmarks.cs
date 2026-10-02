using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BenchmarkDotNet.Attributes;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// P5: the toast animates <c>MaxHeight</c> — a LAYOUT property — every frame, on purpose: the growing
/// slot pushes the stack (a render transform cannot move siblings). Measured: the UI-thread layout
/// work of one such frame in a toast-host-like tree (an overlay stack of cards over an app body of
/// 200 text blocks), against the same frame moved by a render transform instead (no layout).
/// Headless drawing: rendering itself is not included — only what the property write costs the
/// UI thread before the frame is committed.
/// </summary>
[MemoryDiagnoser]
public class LayoutChannelBenchmarks
{
    private Window _window = null!;
    private Border _card = null!;
    private TranslateTransform _shift = null!;
    private int _i;

    [Params(4, 20)]
    public int Toasts;

    [GlobalSetup]
    public void Setup()
    {
        UiSession.Ensure();
        var body = new WrapPanel();
        for (int i = 0; i < 200; i++)
            body.Children.Add(new TextBlock { Text = $"Item {i}", Margin = new Thickness(4) });

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Width = 300,
            Spacing = 8,
        };
        for (int i = 0; i < Toasts; i++)
            stack.Children.Add(Card(i));
        _card = (Border)stack.Children[^1];
        _shift = new TranslateTransform();
        _card.RenderTransform = _shift;

        _window = new Window { Width = 1000, Height = 800, Content = new Grid { Children = { body, stack } } };
        _window.Show();
        _window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Card(int i) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        ClipToBounds = true,
        MaxHeight = 500,
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = $"Toast {i}", FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Something happened and here is a longer description of it.", TextWrapping = TextWrapping.Wrap },
            },
        },
    };

    [GlobalCleanup]
    public void Cleanup() => _window.Close();

    /// <summary>One frame of the toast growth: MaxHeight changes, the stack re-lays out.</summary>
    [Benchmark(Baseline = true)]
    public void MaxHeightFrame()
    {
        _card.MaxHeight = (_i++ & 1) == 0 ? 40 : 41;
        _window.UpdateLayout();
    }

    /// <summary>The same frame moved by a render transform: no layout at all.</summary>
    [Benchmark]
    public void TransformFrame()
    {
        _shift.Y = (_i++ & 1) == 0 ? 40 : 41;
        _window.UpdateLayout();
    }
}
