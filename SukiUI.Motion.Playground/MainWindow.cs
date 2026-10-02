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
        new TransformReplaceScene(),    // Fix 1
        new ChoreographyRestartScene(), // Fix 2
        new SpringHoverScene(),         // Fix 3
        new LoneSpringScene(),          // Fix 4
        new FrozenChannelScene(),       // Fix 7
        new StiffSpringScene(),         // Fix 9
        new OverdampedEaseScene(),      // Fix 10
        new SharedEffectScene(),        // Fix 11
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
                new TextBlock { Text = $"{s.BugId}  {s.Title}", TextWrapping = TextWrapping.Wrap }),
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
    }

    private void UpdateStats()
    {
        long dispatches = SukiMotionStats.DispatchCount;
        double ms = SukiMotionStats.TotalDispatchMs;
        double perSecond = (dispatches - _lastDispatches) * 2.0;
        double avg = dispatches > _lastDispatches ? (ms - _lastDispatchMs) / (dispatches - _lastDispatches) : 0;
        _lastDispatches = dispatches;
        _lastDispatchMs = ms;
        string state = perSecond == 0 ? "IDLE (0 engine frames)" : "ANIMATING";
        _stats.Text = $"Engine: {perSecond,5:0} dispatch/s   {avg,6:0.000} ms/dispatch   total {dispatches}   - {state}";
        _stats.Foreground = perSecond == 0 ? Brushes.DarkGreen : Brushes.DarkOrange;
    }
}
