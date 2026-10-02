using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

namespace SukiUI.Motion.Tests;

/// <summary>How engine writes interact with styles (value priority).</summary>
public class StylePriorityTests
{
    private const string Bug6 = "Bug 6 (confirmed red): engine writes are LocalValue and never cleared. " +
                                "Fix is a design change — write priority and end-of-animation semantics — scheduled for the API phase.";

    // Bug 6a: every channel writes with LocalValue priority and nothing clears it once the
    // animation is over, so the animated property permanently beats every style — a style
    // trigger applied later (":disabled", a class) is silently ignored.
    [AvaloniaFact(Skip = Bug6)]
    public void Style_applied_after_an_animation_still_takes_effect()
    {
        var border = new Border { Width = 50, Height = 50 };
        using var h = new MotionHarness(border);
        h.Window.Styles.Add(new Style(x => x.OfType<Border>().Class("dim"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.4) },
        });
        var opacity = Animate.For(border).Opacity;

        opacity.Offer(opacity.To(1.0).Over(TimeSpan.FromMilliseconds(100)));
        h.Run(TimeSpan.FromMilliseconds(200)); // played out, channel idle
        border.Classes.Add("dim");
        MotionHarness.Flush();

        Assert.Equal(0.4, border.Opacity);
    }

    // Bug 6b: the popup cascade's Reset writes a local Opacity = 1 and attaches a transform
    // block to EVERY item — even when offset/scale were never animated — so styled item
    // opacity (disabled menu items) and styled item transforms are wiped.
    [AvaloniaFact(Skip = Bug6)]
    public void Cascade_reset_leaves_styled_items_untouched()
    {
        var item = new Border { Width = 50, Height = 20, Classes = { "item" } };
        var panel = new StackPanel { Children = { item } };
        using var h = new MotionHarness(panel);
        var styledTransform = new RotateTransform(5);
        h.Window.Styles.Add(new Style(x => x.OfType<Border>().Class("item"))
        {
            Setters =
            {
                new Setter(Visual.OpacityProperty, 0.4),
                new Setter(Visual.RenderTransformProperty, styledTransform),
            },
        });
        MotionHarness.Flush();

        var cascade = new CascadeProgram(
            collect: () => new Control[] { item },
            duration: () => TimeSpan.FromMilliseconds(100),
            initialDelayMs: () => 0,
            staggerMs: _ => 0,
            skipAbove: () => 10,
            itemBlur: () => 0,
            itemOffsetY: () => 0,
            itemScale: () => 1.0);
        var choreography = new Choreography().And(cascade);
        choreography.Start(item);
        h.Frame();
        choreography.Stop();
        cascade.Reset();
        MotionHarness.Flush();

        Assert.Equal(0.4, item.Opacity);
        Assert.Same(styledTransform, item.RenderTransform);
    }
}
