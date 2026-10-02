using Avalonia.Controls;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground.Scenes;

public sealed class ChoreographyRestartScene : Scene
{
    public override string BugId => "Fix 2";
    public override string Title => "Restarting a running choreography";
    public override string Steps =>
        "The knob is driven by one shared choreography, restarted on every toggle (as SukiToggleSwitchMotion does). " +
        "Press \"Toggle\" 2-3 times quickly, then wait and watch the counter and the stats line at the bottom.";
    public override string Before =>
        "Every restart mid-flight leaked a ticker subscription: the settle counter kept growing on its own " +
        "and the engine never went idle (the bottom line stayed at ~60 dispatch/s forever).";
    public override string Expected =>
        "One settle per burst of presses; once the knob stops, the bottom line reads \"IDLE (0 engine frames)\".";

    protected override Control Build()
    {
        var knob = Box("#00897B", 50);
        var track = new Border
        {
            Width = 260,
            Height = 70,
            CornerRadius = new Avalonia.CornerRadius(35),
            Background = Avalonia.Media.Brushes.Gainsboro,
            Padding = new Avalonia.Thickness(10),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Child = new Border { Child = knob, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
        };
        var counter = new TextBlock { FontSize = 16 };
        int settles = 0;
        bool on = false;

        var x = Motion.For(knob).TranslateX;
        var snap = new Choreography()
            .And(x.To(() => on ? 190 : 0).Spring(new Spring(Omega: 18, Decay: 12)))
            .Then(() => counter.Text = $"Settles: {++settles}");
        counter.Text = "Settles: 0";

        return Column(
            Btn("Toggle", () =>
            {
                on = !on;
                snap.Start(knob); // the same instance, restarted mid-flight
            }),
            track,
            counter);
    }
}

public sealed class FrozenChannelScene : Scene
{
    public override string BugId => "Fix 7";
    public override string Title => "A channel after a stopped choreography";
    public override string Steps =>
        "\"Grow with a choreography\" starts a spring to 1.5 and stops the choreography 120 ms later (as if preempted). " +
        "Then \"Return through Offer\": a plain timed animation back to 1.0.";
    public override string Before =>
        "The channel kept a frozen spring nobody advanced, and the rule \"only a press preempts a spring\" " +
        "dropped the return: the square stayed enlarged forever.";
    public override string Expected => "The square returns to 1.0.";

    protected override Control Build()
    {
        var box = Box("#5C6BC0", 80);
        var scale = Motion.For(box).Scale;
        var trace = new TraceView { Label = "Scale", Min = 0.9, Max = 1.6 }.Follow(() => scale.Value);
        var stage = new Border { Padding = new Avalonia.Thickness(50), Child = box, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };

        return Column(
            Row(
                Btn("Grow with a choreography", () =>
                {
                    var grow = new Choreography().And(scale.To(1.5).Spring(new Spring(10, 9)));
                    grow.Start(box);
                    DispatcherTimer.RunOnce(grow.Stop, TimeSpan.FromMilliseconds(120));
                }),
                Btn("Return through Offer", () =>
                    scale.Offer(scale.To(1.0).Over(TimeSpan.FromMilliseconds(300))))),
            stage,
            trace);
    }
}
