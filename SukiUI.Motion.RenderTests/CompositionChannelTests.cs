using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using SukiUI.Motion.Composition;

namespace SukiUI.Motion.RenderTests;

/// <summary>
/// The composition backend prototype (PLAN D21) on real pixels: a 40×40 black square on white,
/// moved by composition channels. Pixel row y = 20 crosses the square at rest.
/// </summary>
public class CompositionChannelTests(ITestOutputHelper output)
{
    private const int RowY = 20;

    private static (Window Window, Border Box) Scene(Thickness margin = default)
    {
        var box = new Border
        {
            Width = 40, Height = 40, Background = Brushes.Black, Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window { Width = 400, Height = 120, Background = Brushes.White, Content = box };
        window.Show();
        Pixels.Capture(window);
        return (window, box);
    }

    private int Left(Window w, string label)
    {
        int x = Pixels.LeftEdge(Pixels.Capture(w), RowY);
        output.WriteLine($"{label}: left edge = {x}");
        return x;
    }

    [AvaloniaFact]
    public void EaseTo_reaches_its_target()
    {
        var (w, box) = Scene();
        var x = CompositionMotion.For(box).TranslateX;

        x.EaseTo(100, TimeSpan.FromMilliseconds(300), new CubicEaseOut());
        Pixels.Wait(TimeSpan.FromMilliseconds(550));

        Assert.InRange(Left(w, "after 550 ms"), 99, 101);
        Assert.Equal(100, x.Value);
        Assert.False(x.IsAnimating);
        w.Close();
    }

    [AvaloniaFact]
    public void SpringTo_settles_on_its_target_and_raises_Settled()
    {
        var (w, box) = Scene();
        var x = CompositionMotion.For(box).TranslateX;
        int settled = 0;
        x.Settled += () => settled++;

        x.SpringTo(120, new Spring(Omega: 18, Decay: 20));
        Pixels.Wait(TimeSpan.FromMilliseconds(150));
        int mid = Left(w, "mid-flight");
        Pixels.Wait(TimeSpan.FromSeconds(1.5));

        Assert.InRange(mid, 5, 140); // moving (may overshoot past 120)
        Assert.InRange(Left(w, "settled"), 119, 121);
        Assert.Equal(1, settled);
        w.Close();
    }

    // The core requirement: an interruption continues from the pose ON SCREEN with the live
    // velocity — no jump at the switch, and the reversal is gradual (momentum), not instant.
    [AvaloniaFact]
    public void Interrupting_a_spring_does_not_jump()
    {
        var (w, box) = Scene();
        var x = CompositionMotion.For(box).TranslateX;
        var spring = new Spring(Omega: 12, Decay: 10);

        x.SpringTo(200, spring);
        Pixels.Wait(TimeSpan.FromMilliseconds(180));
        int before = Left(w, "before interruption");
        double velocity = x.Velocity;
        x.SpringTo(0, spring);
        int after = Left(w, "right after interruption");
        Pixels.Wait(TimeSpan.FromMilliseconds(40));
        int later = Left(w, "40 ms later");

        output.WriteLine($"model velocity at the switch: {velocity:0} px/s");
        Assert.True(velocity > 300, "precondition: moving fast toward 200");
        Assert.InRange(after - before, -10, 25); // continues from the on-screen pose
        Assert.True(later >= before - 5, "momentum: still moving forward (or barely turned) 40 ms later");
        Pixels.Wait(TimeSpan.FromSeconds(2));
        Assert.InRange(Left(w, "settled"), 0, 1);
        w.Close();
    }

    // Avalonia trap (ENGINEERING_NOTES §7.4): a static write equal to the client field is skipped,
    // so a plain write cannot stop an animation that started from that value...
    [AvaloniaFact]
    public void Raw_api_equal_write_does_not_stop_a_running_animation()
    {
        var (w, box) = Scene();
        var v = ElementComposition.GetElementVisual(box)!;
        var anim = v.Compositor.CreateVector3DKeyFrameAnimation();
        anim.InsertKeyFrame(1f, new Vector3D(200, 0, 0), new LinearEasing());
        anim.Duration = TimeSpan.FromSeconds(2);
        v.StartAnimation("Translation", anim);
        Pixels.Wait(TimeSpan.FromMilliseconds(300));

        v.Translation = new Vector3D(0, 0, 0); // equal to the client field: skipped
        Pixels.Wait(TimeSpan.FromMilliseconds(300));

        Assert.True(Left(w, "after equal write") > 30, "the animation kept running");
        w.Close();
    }

    // ...which is why the backend forces its static writes.
    [AvaloniaFact]
    public void Pose_stops_a_running_animation_even_at_its_start_value()
    {
        var (w, box) = Scene();
        var x = CompositionMotion.For(box).TranslateX;

        x.EaseTo(200, TimeSpan.FromSeconds(2));
        Pixels.Wait(TimeSpan.FromMilliseconds(300));
        x.Pose(0);
        Pixels.Wait(TimeSpan.FromMilliseconds(300));

        Assert.InRange(Left(w, "after Pose(0)"), 0, 1);
        Assert.False(x.IsAnimating);
        w.Close();
    }

    [AvaloniaFact]
    public void Two_axes_of_one_property_animate_independently()
    {
        var (w, box) = Scene();
        var s = CompositionMotion.For(box);

        s.TranslateX.EaseTo(150, TimeSpan.FromMilliseconds(300));
        Pixels.Wait(TimeSpan.FromMilliseconds(100));
        s.TranslateY.EaseTo(30, TimeSpan.FromMilliseconds(200)); // rebuilds Translation mid-flight of X
        Pixels.Wait(TimeSpan.FromMilliseconds(500));

        var frame = Pixels.Capture(w);
        Assert.InRange(Pixels.LeftEdge(frame, 30 + RowY), 149, 151); // X arrived, square moved down by Y
        Assert.Equal(-1, Pixels.LeftEdge(frame, 5));                // nothing left at the old rows
        w.Close();
    }

    [AvaloniaFact]
    public void Scale_springs_around_the_render_transform_origin()
    {
        var (w, box) = Scene(new Thickness(100, 30, 0, 0)); // square spans x 100..140, y 30..70
        var scale = CompositionMotion.For(box).Scale;

        scale.SpringTo(2.0, new Spring(Omega: 20, Decay: 40));
        Pixels.Wait(TimeSpan.FromSeconds(1));

        var frame = Pixels.Capture(w);
        int left = Pixels.LeftEdge(frame, 50), width = Pixels.DarkWidth(frame, 50);
        output.WriteLine($"scaled: left = {left}, width = {width}");
        Assert.InRange(width, 79, 81); // 40 × 2
        Assert.InRange(left, 79, 81);  // centered on x = 120
        w.Close();
    }
}
