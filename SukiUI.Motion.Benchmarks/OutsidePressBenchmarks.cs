using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using BenchmarkDotNet.Attributes;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// P7: every popup host with CloseOnOutsidePress (each ComboBox in SukiUI) wires a
/// <c>PointerPressed</c> handler on its window for as long as it is attached, open or not —
/// the wiring of <c>SukiPopupMotion</c>, reproduced here with the same <see cref="Mover"/> calls.
/// Measured: one full click (press + release through the headless input pipeline: hit test,
/// routing, class handlers) in a window holding N such hosts.
/// </summary>
[MemoryDiagnoser]
public class OutsidePressBenchmarks
{
    private static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<OutsidePressBenchmarks, Control, bool>("IsOpen");

    private Window _window = null!;
    private readonly List<Mover> _movers = new();
    private int _closed;

    [Params(0, 10, 100, 1000)]
    public int Hosts;

    [GlobalSetup]
    public void Setup()
    {
        UiSession.Ensure();
        var panel = new WrapPanel();
        _window = new Window { Width = 800, Height = 600, Content = panel };
        for (int i = 0; i < Hosts; i++)
        {
            var host = new Border { Width = 4, Height = 4 };
            panel.Children.Add(host);
        }
        _window.Show();
        foreach (var child in panel.Children)
        {
            var host = (Control)child;
            _movers.Add(new Mover(host).OnEvent(InputElement.PointerPressedEvent,
                () =>
                {
                    if (host.GetValue(IsOpenProperty)) // the guard's HostIsOpen()
                    {
                        host.SetValue(IsOpenProperty, false);
                        _closed++;
                    }
                },
                handledEventsToo: true,
                source: Mover.TopLevelOf(host)));
        }
        Dispatcher.UIThread.RunJobs();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var mover in _movers)
            mover.Dispose();
        _window.Close();
    }

    [Benchmark]
    public void Click()
    {
        var point = new Point(700, 500); // empty area of the window
        _window.MouseDown(point, MouseButton.Left);
        _window.MouseUp(point, MouseButton.Left);
    }
}
