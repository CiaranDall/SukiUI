using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SukiUI.Motion.Playground.Scenes;

namespace SukiUI.Motion.Playground;

/// <summary>Scene list on the left, the selected scene on the right, engine stats below.</summary>
public sealed class MainWindow : Window
{
    private static readonly Scene[] AllScenes =
    {
        new TransformReplaceScene(),
        new SpringHoverScene(),
        new LoneSpringScene(),
        new ChoreographyRestartScene(),
        new FrozenChannelScene(),
        new SharedEffectScene(),
        new StiffSpringScene(),
        new OverdampedEaseScene(),
        new StylePriorityScene(),
        // Phase 2 research: Avalonia's Composition API observed live.
        new UiThreadStallScene(),
        new OpacitySyncScene(),
        new NoReadbackScene(),
        new HitTestScene(),
        // Prototype of the composition backend (PLAN D21).
        new PrototypeSpringScene(),
    };

    private readonly ContentControl _host = new() { Margin = new Thickness(24) };
    private readonly TextBlock _stats = new() { Margin = new Thickness(12, 6), FontFamily = FontFamily.Parse("Consolas, monospace") };
    private long _lastDispatches;
    private double _lastDispatchMs;

    public MainWindow()
    {
        Title = "SukiUI.Motion Playground";
        Width = 1200;
        Height = 760;

        var list = new ListBox
        {
            ItemsSource = AllScenes,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Scene>((s, _) =>
                new TextBlock { Text = s is null ? "" : $"{s.BugId}  {s.Title}", TextWrapping = TextWrapping.Wrap }), // null while a virtualized container recycles
            Width = 300,
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is Scene scene)
                _host.Content = scene.BuildPage(); // fresh controls each time: fresh channels
        };

        var statsBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F0F0F0")),
            Child = _stats,
        };
        DockPanel.SetDock(statsBar, Dock.Bottom);
        DockPanel.SetDock(list, Dock.Left);

        Content = new DockPanel
        {
            Children = { statsBar, list, new ScrollViewer { Content = _host } },
        };

        // Engine instrumentation, sampled twice a second (a DispatcherTimer never triggers
        // ticker dispatches itself: an idle engine must read 0 dispatch/s).
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => UpdateStats();
        timer.Start();
        list.SelectedIndex = 0;

        if (Program.MeasureCost is { } cost)
            CostMeasurement.Run(this, cost.Path, cost.Boxes, cost.Seconds);
        else if (Program.MeasureC1 is { } measure)
        {
            list.SelectedIndex = Array.FindIndex(AllScenes, sc => sc is UiThreadStallScene);
            FrameMeasurement.Run(this, measure.Path, measure.Seconds);
        }
    }

    private void UpdateStats()
    {
        long dispatches = SukiMotionStats.DispatchCount;
        double ms = SukiMotionStats.TotalDispatchMs;
        double perSecond = (dispatches - _lastDispatches) * 2.0;
        double avg = dispatches > _lastDispatches ? (ms - _lastDispatchMs) / (dispatches - _lastDispatches) : 0;
        _lastDispatches = dispatches;
        _lastDispatchMs = ms;
        string state = perSecond == 0 ? "IDLE (0 кадров движка)" : "АНИМАЦИЯ";
        _stats.Text = $"Движок: {perSecond,5:0} dispatch/s   {avg,6:0.000} ms/dispatch   всего {dispatches}   — {state}";
        _stats.Foreground = perSecond == 0 ? Brushes.DarkGreen : Brushes.DarkOrange;
    }
}
