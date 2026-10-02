using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using BenchmarkDotNet.Attributes;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// P7: every popup host with CloseOnOutsidePress (each ComboBox in SukiUI) wires a
/// <c>PointerPressed</c> handler on its window for as long as it is attached, open or not —
/// the wiring of <c>SukiPopupMotion</c>, reproduced here with the same <see cref="Mover"/> calls.
/// Measured: one PointerPressed raised on the window (routing + every handler on it) in a window
/// holding N such hosts — exactly the work those handlers add to each press. (A full headless
/// click through the input pipeline was tried first: the headless input state grows with every
/// click, ~0.5 KB and a few µs per thousand, which makes it useless as a stable benchmark.)
/// Raising PointerPressed on a headless window ALSO retains ~0.8 KB per press inside Avalonia
/// (18 → 29 µs over 8000 presses, with or without pumping the dispatcher — ENGINEERING_NOTES §8.4),
/// so the job is short and fixed: ~2400 presses per case, where that drift stays small next to
/// the effect being measured.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 208)]
public class OutsidePressBenchmarks
{
    private static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<OutsidePressBenchmarks, Control, bool>("IsOpen");

    private Window _window = null!;
    private PointerPressedEventArgs _press = null!;
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
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        _press = new PointerPressedEventArgs(_window, pointer, _window, new Point(700, 500), 0, properties, KeyModifiers.None);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var mover in _movers)
            mover.Dispose();
        _window.Close();
    }

    [Benchmark]
    public void PressOnWindow()
    {
        _press.Handled = false;
        _press.Route = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
        _window.RaiseEvent(_press);
    }
}
