using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace SukiUI.Motion.RenderTests;

/// <summary>
/// Characterization of Avalonia's Composition API (ENGINEERING_NOTES §7.4), observed on real
/// pixels: a black square on white, its composition Opacity animated 1 → 0 over 2 s. The
/// pixel's gray level IS the on-screen opacity: 0 = opaque black, 255 = fully faded.
/// </summary>
public class CompositionOpacityTests(ITestOutputHelper output)
{
    private static (Window Window, Border Box) Scene()
    {
        var box = new Border { Width = 100, Height = 100, Background = Brushes.Black,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 200, Height = 200, Background = Brushes.White, Content = box };
        window.Show();
        Pixels.Capture(window);
        return (window, box);
    }

    private static void StartFade(Border box)
    {
        var v = ElementComposition.GetElementVisual(box)!;
        var anim = v.Compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(1f, 0f, new LinearEasing());
        anim.Duration = TimeSpan.FromSeconds(2);
        v.StartAnimation("Opacity", anim);
    }

    private double Sample(Window w, string label)
    {
        double g = Pixels.Gray(Pixels.Capture(w), 50, 50);
        output.WriteLine($"{label}: gray={g:0}");
        return g;
    }

    [AvaloniaFact]
    public void Control_fade_progresses_on_its_own()
    {
        var (w, box) = Scene();
        StartFade(box);
        Pixels.Wait(TimeSpan.FromMilliseconds(600));
        double a = Sample(w, "t≈0.6s");
        Pixels.Wait(TimeSpan.FromMilliseconds(400));
        double b = Sample(w, "t≈1.0s");

        Assert.InRange(a, 30, 220);   // mid-fade
        Assert.True(b > a + 15, "the fade keeps progressing");
        w.Close();
    }

    [AvaloniaFact]
    public void InvalidateVisual_mid_fade_keeps_the_fade()
    {
        var (w, box) = Scene();
        StartFade(box);
        Pixels.Wait(TimeSpan.FromMilliseconds(600));
        double a = Sample(w, "before InvalidateVisual");
        box.InvalidateVisual();
        Pixels.Wait(TimeSpan.FromMilliseconds(400));
        double b = Sample(w, "after InvalidateVisual");

        // A redraw re-syncs comp.Opacity with the SAME value: the client setter skips it.
        Assert.True(b > a + 15, "InvalidateVisual must not cancel the composition fade");
        w.Close();
    }

    [AvaloniaFact]
    public void Visual_opacity_change_mid_fade_cancels_it()
    {
        var (w, box) = Scene();
        StartFade(box);
        Pixels.Wait(TimeSpan.FromMilliseconds(600));
        double a = Sample(w, "before Visual.Opacity = 0.99");
        box.Opacity = 0.99;
        Pixels.Wait(TimeSpan.FromMilliseconds(400));
        double b = Sample(w, "after Visual.Opacity = 0.99");

        // A changed Visual.Opacity is written through: the animation is removed, 0.99 shows.
        Assert.True(b < 10, "a Visual.Opacity change cancels the fade and snaps to the new value");
        w.Close();
    }

    // The scene's Start(): Visual.Opacity set back to 1 right before StartAnimation, after an
    // earlier "0.99" click — the sync then writes comp.Opacity in the same batch.
    [AvaloniaFact]
    public void Visual_opacity_change_in_the_same_batch_kills_the_start()
    {
        var (w, box) = Scene();
        box.Opacity = 0.99;
        Pixels.Capture(w);
        box.Opacity = 1.0;   // what C2's Start does first
        StartFade(box);
        Pixels.Wait(TimeSpan.FromMilliseconds(800));
        double b = Sample(w, "0.8 s after Start");

        // The sync writes comp.Opacity in the same batch, after StartAnimation: the pending
        // animation is dropped before it ever reaches the server.
        Assert.True(b < 10, "a Visual.Opacity change in the same batch kills a just-started fade");
        w.Close();
    }
}
