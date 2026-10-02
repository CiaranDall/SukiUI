using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

namespace SukiUI.Motion.Tests;

/// <summary>
/// How engine writes interact with styles and local values (value priority, PLAN D32): the
/// engine holds its pose at <see cref="BindingPriority.Animation"/> and lets go once the pose
/// is back at the base value, like Avalonia's own transitions and animations.
/// </summary>
public class StylePriorityTests
{
    // Bug 6a: every channel wrote with LocalValue priority and nothing cleared it once the
    // animation was over, so the animated property permanently beat every style — a style
    // trigger applied later (":disabled", a class) was silently ignored.
    [AvaloniaFact]
    public void Style_applied_after_an_animation_still_takes_effect()
    {
        var border = new Border { Width = 50, Height = 50 };
        using var h = new MotionHarness(border);
        h.Window.Styles.Add(DimStyle());
        var opacity = Animate.For(border).Opacity;

        opacity.Offer(opacity.To(0.5).Over(TimeSpan.FromMilliseconds(100)));
        h.Run(TimeSpan.FromMilliseconds(200));
        opacity.Offer(opacity.To(1.0).Over(TimeSpan.FromMilliseconds(100)));
        h.Run(TimeSpan.FromMilliseconds(200)); // back at rest, channel idle
        border.Classes.Add("dim");
        MotionHarness.Flush();

        Assert.Equal(0.4, border.Opacity);
    }

    // A pose held away from the base value is an animation in progress: it wins over a style
    // applied meanwhile, exactly like a held Avalonia animation.
    [AvaloniaFact]
    public void A_held_pose_beats_a_style_applied_meanwhile()
    {
        var border = new Border { Width = 50, Height = 50 };
        using var h = new MotionHarness(border);
        h.Window.Styles.Add(DimStyle());
        var opacity = Animate.For(border).Opacity;

        opacity.Offer(opacity.To(0.5).Over(TimeSpan.FromMilliseconds(100)));
        h.Run(TimeSpan.FromMilliseconds(200));
        border.Classes.Add("dim");
        MotionHarness.Flush();

        Assert.Equal(0.5, border.Opacity);
        Assert.Equal(0.4, border.GetBaseValue(Visual.OpacityProperty).Value);
    }

    // Letting go must never wipe a value the user set themselves before the animation.
    [AvaloniaFact]
    public void A_local_value_set_before_an_animation_survives_it()
    {
        var border = new Border { Width = 50, Height = 50, Opacity = 0.7 };
        using var h = new MotionHarness(border);
        var opacity = Animate.For(border).Opacity;

        opacity.Offer(opacity.To(0.2).Over(TimeSpan.FromMilliseconds(100)));
        h.Run(TimeSpan.FromMilliseconds(200));
        opacity.Offer(opacity.To(0.7).Over(TimeSpan.FromMilliseconds(100)));
        h.Run(TimeSpan.FromMilliseconds(200));
        border.Opacity = 0.3; // a later local write takes effect again

        Assert.Equal(0.3, border.Opacity);
    }

    // Bug 6b: the popup cascade's Reset wrote a local Opacity = 1 and attached a transform
    // block to EVERY item — even when offset/scale were never animated — so styled item
    // opacity (disabled menu items) and styled item transforms were wiped.
    [AvaloniaFact]
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

        var cascade = Cascade(item, offsetY: 0, scale: 1.0);
        var choreography = new Choreography().And(cascade);
        choreography.Start(item);
        h.Frame();
        choreography.Stop();
        cascade.Reset();
        MotionHarness.Flush();

        Assert.Equal(0.4, item.Opacity);
        Assert.Same(styledTransform, item.RenderTransform);
    }

    // The cascade fades an item in toward ITS resting opacity: a styled (disabled) item lands
    // at its style value, not at 1.
    [AvaloniaFact]
    public void Cascade_fades_a_styled_item_in_to_its_style_value()
    {
        var item = new Border { Width = 50, Height = 20, Classes = { "item" } };
        var panel = new StackPanel { Children = { item } };
        using var h = new MotionHarness(panel);
        h.Window.Styles.Add(new Style(x => x.OfType<Border>().Class("item"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.4) },
        });
        MotionHarness.Flush();

        var choreography = new Choreography().And(Cascade(item, offsetY: 6, scale: 0.9));
        choreography.Start(item);
        h.Run(TimeSpan.FromMilliseconds(300));

        Assert.False(choreography.Running);
        Assert.Equal(0.4, item.Opacity);
        Assert.Null(item.RenderTransform);
    }

    // A styled RenderTransform composes under the engine's transforms (no jump when the
    // engine starts) and is the element's transform again once they are back at rest.
    [AvaloniaFact]
    public void Styled_render_transform_composes_and_comes_back_at_rest()
    {
        var border = new Border { Width = 100, Height = 100, Classes = { "tilted" } };
        using var h = new MotionHarness(border);
        var styledTransform = new RotateTransform(5);
        h.Window.Styles.Add(new Style(x => x.OfType<Border>().Class("tilted"))
        {
            Setters = { new Setter(Visual.RenderTransformProperty, styledTransform) },
        });
        MotionHarness.Flush();
        var translate = Animate.For(border).TranslateX;

        translate.Write(10);
        Assert.Equal(styledTransform.Value * Matrix.CreateTranslation(10, 0), border.RenderTransform!.Value);

        translate.Write(0);
        Assert.Same(styledTransform, border.RenderTransform);
    }

    // A blur that dissipates lets go of the Effect slot: a styled effect shows again.
    [AvaloniaFact]
    public void Styled_effect_comes_back_after_a_blur()
    {
        var border = new Border { Width = 50, Height = 50, Classes = { "shadowed" } };
        using var h = new MotionHarness(border);
        var styledShadow = new DropShadowEffect { Opacity = 0.5, BlurRadius = 10 };
        h.Window.Styles.Add(new Style(x => x.OfType<Border>().Class("shadowed"))
        {
            Setters = { new Setter(Visual.EffectProperty, styledShadow) },
        });
        MotionHarness.Flush();
        var blur = Animate.For(border).Blur;

        blur.Write(5);
        Assert.IsType<BlurEffect>(border.Effect);

        blur.Write(0);
        Assert.Same(styledShadow, border.Effect);
    }

    // Fading a styled shadow out is a pose of its own: the shadow stays hidden, it does not
    // snap back to the style value.
    [AvaloniaFact]
    public void A_styled_shadow_faded_out_stays_hidden()
    {
        var border = new Border { Width = 50, Height = 50, Classes = { "shadowed" } };
        using var h = new MotionHarness(border);
        h.Window.Styles.Add(new Style(x => x.OfType<Border>().Class("shadowed"))
        {
            Setters = { new Setter(Visual.EffectProperty, new DropShadowEffect { Opacity = 0.5 }) },
        });
        MotionHarness.Flush();

        Animate.For(border).ShadowOpacity.Write(0.0);

        Assert.Null(border.Effect);
    }

    private static Style DimStyle() => new(x => x.OfType<Border>().Class("dim"))
    {
        Setters = { new Setter(Visual.OpacityProperty, 0.4) },
    };

    private static CascadeProgram Cascade(Control item, double offsetY, double scale) => new(
        collect: () => new[] { item },
        duration: () => TimeSpan.FromMilliseconds(100),
        initialDelayMs: () => 0,
        staggerMs: _ => 0,
        skipAbove: () => 10,
        itemBlur: () => 0,
        itemOffsetY: () => offsetY,
        itemScale: () => scale);
}
