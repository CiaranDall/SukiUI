using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using BenchmarkDotNet.Attributes;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// P6: <see cref="Mover.OnPropertyChanged(Avalonia.AvaloniaProperty, System.Action, object?)"/>
/// subscribes to the element's catch-all <c>PropertyChanged</c>, so its handler runs on EVERY
/// property change of the element, not only the watched one. A popup host wires two such
/// triggers (open / close). Measured: the cost of one unrelated property change, with 0 / 2 / 8
/// triggers on the element, and the cost of the watched change itself.
/// </summary>
[MemoryDiagnoser]
public class PropertyChangedBenchmarks
{
    private const int Changes = 1000;
    private ToggleButton _element = null!;
    private Mover? _mover;
    private int _fired;

    [Params(0, 2, 8)]
    public int Triggers;

    [GlobalSetup]
    public void Setup()
    {
        UiSession.Ensure();
        _element = new ToggleButton();
        if (Triggers > 0)
        {
            _mover = new Mover(_element);
            for (int i = 0; i < Triggers; i++)
                _mover.OnPropertyChanged(ToggleButton.IsCheckedProperty, () => _fired++, when: i % 2 == 0);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _mover?.Dispose();

    /// <summary>A property nobody watches (the common case: layout, styles, bindings...).</summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = Changes)]
    public void UnrelatedChange()
    {
        for (int i = 0; i < Changes; i++)
            _element.Width = (i & 1) == 0 ? 10 : 20;
    }

    /// <summary>The watched property: the trigger's filter runs and fires.</summary>
    [Benchmark(OperationsPerInvoke = Changes)]
    public void WatchedChange()
    {
        for (int i = 0; i < Changes; i++)
            _element.IsChecked = (i & 1) == 0;
    }
}
