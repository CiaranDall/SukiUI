using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground.Scenes;

public sealed class TransformReplaceScene : Scene
{
    public override string BugId => "Fix 1";
    public override string Title => "External RenderTransform replacement";
    public override string Steps =>
        "Press \"Shift\" and \"Rotate\": the square moves. Then \"Replace RenderTransform externally\" " +
        "(what a style, a theme or user code does) and press \"Shift\" / \"Rotate\" again.";
    public override string Before =>
        "After the replacement, shift and rotate stopped working: the writes went to transforms detached from the group. " +
        "Channel.Value still \"moved\": the line below showed Value disagreeing with the real matrix. Scale kept working.";
    public override string Expected =>
        "Shift, rotate and scale keep working after the replacement; Value matches the real matrix.";

    protected override Control Build()
    {
        var box = Box("#3F7FBF");
        var s = Motion.For(box);
        bool shifted = false, rotated = false, scaled = false;
        var ease = new CubicEaseOut();
        var readout = new TextBlock { FontFamily = FontFamily.Parse("Consolas, monospace") };

        var stage = Stage(120, box);
        Place(box, 20, 30);

        // Value vs what is really on screen.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var m = box.RenderTransform?.Value;
            readout.Text = $"TranslateX.Value = {s.TranslateX.Value,7:0.0}   on screen (M31) = {(m?.M31 ?? 0),7:0.0}";
        };
        box.AttachedToVisualTree += (_, _) => timer.Start();
        box.DetachedFromVisualTree += (_, _) => timer.Stop();

        return Column(
            Row(
                Btn("Shift", () =>
                {
                    shifted = !shifted;
                    s.TranslateX.Offer(s.TranslateX.To(shifted ? 160 : 0).Over(TimeSpan.FromMilliseconds(350)).Ease(ease));
                }),
                Btn("Rotate", () =>
                {
                    rotated = !rotated;
                    s.Rotate.Offer(s.Rotate.To(rotated ? 45 : 0).Over(TimeSpan.FromMilliseconds(350)).Ease(ease));
                }),
                Btn("Scale", () =>
                {
                    scaled = !scaled;
                    s.Scale.Offer(s.Scale.To(scaled ? 1.3 : 1.0).Over(TimeSpan.FromMilliseconds(350)).Ease(ease));
                }),
                Btn("Replace RenderTransform externally", () => box.RenderTransform = new ScaleTransform(1, 1))),
            stage,
            readout);
    }
}

public sealed class SharedEffectScene : Scene
{
    public override string BugId => "Fix 11";
    public override string Title => "A styled shadow is shared by every control";
    public override string Steps =>
        "Three cards get the same DropShadowEffect from a style setter. Press \"Animate the first card's shadow\".";
    public override string Before =>
        "The engine mutated the effect in place, and a setter value is one instance shared by every control: all three shadows changed.";
    public override string Expected => "Only the first card's shadow changes.";

    protected override Control Build()
    {
        var cards = Enumerable.Range(0, 3).Select(i =>
        {
            var card = Box("#FFFFFF", 90);
            card.Classes.Add("card");
            return card;
        }).ToArray();
        var root = Row(cards);
        root.Spacing = 40;
        root.Margin = new Avalonia.Thickness(30);
        root.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Border>().Class("card"))
        {
            Setters =
            {
                new Avalonia.Styling.Setter(Avalonia.Visual.EffectProperty,
                    new DropShadowEffect { BlurRadius = 10, Opacity = 0.35, Color = Colors.Black }),
            },
        });

        var s = Motion.For(cards[0]);
        bool deep = false;
        return Column(
            Btn("Animate the first card's shadow", () =>
            {
                deep = !deep;
                new Choreography()
                    .And(s.ShadowBlur.To(deep ? 40 : 10).Over(TimeSpan.FromMilliseconds(400)))
                    .And(s.ShadowOpacity.To(deep ? 0.9 : 0.35).Over(TimeSpan.FromMilliseconds(400)))
                    .Start(cards[0]);
            }),
            root);
    }
}
