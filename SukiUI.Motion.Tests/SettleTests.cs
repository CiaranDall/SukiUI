using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace SukiUI.Motion.Tests;

/// <summary>
/// P8 (phase 3): the settle thresholds are absolute (|Δ| &lt; 0.0005, |v| &lt; 0.02) whatever the
/// channel's unit. Fine for scale and opacity, but 200× below anything visible for a channel in
/// DIPs: a translate spring keeps the frame loop (and a re-render per frame) awake long after it
/// stopped moving on screen. Measured on the integrator: ENGINEERING_NOTES §8.2.
/// </summary>
public class SettleTests
{
    private static long FramesUntilIdle(MotionHarness h, TimeSpan budget)
    {
        long start = SukiTicker.DispatchCount, last = start;
        int quiet = 0;
        for (int i = 0; i < budget.TotalMilliseconds / MotionHarness.FrameMs && quiet < 10; i++)
        {
            h.Frame();
            long now = SukiTicker.DispatchCount;
            quiet = now == last ? quiet + 1 : 0;
            last = now;
        }
        return last - start;
    }

    // Bouncy 300-DIP slide (the playground's P1 spring): the motion is under 0.1 DIP from
    // frame ~150, yet the engine ticks until frame ~254.
    [AvaloniaFact]
    public void Translate_spring_stops_ticking_once_the_motion_is_below_a_twentieth_of_a_DIP()
    {
        var border = new Border();
        using var h = new MotionHarness(border);
        var x = Motion.For(border).TranslateX;
        x.Track(0);
        x.Track(300);

        x.Offer(x.To(300).Spring(new Spring(Omega: 10, Decay: 6)));
        long frames = FramesUntilIdle(h, TimeSpan.FromSeconds(6));

        Assert.Equal(300, x.Value); // still snaps exactly onto the target
        Assert.InRange(frames, 140, 170);
    }

    // Unitless channels keep the fine threshold: 0.0005 of scale is ~0.1 DIP on a 200-DIP element.
    [AvaloniaFact]
    public void Scale_spring_keeps_the_fine_threshold()
    {
        var border = new Border();
        using var h = new MotionHarness(border);
        var scale = Motion.For(border).Scale;

        scale.Offer(scale.To(0.92).Spring(new Spring(Omega: 20, Decay: 40))); // the popup's closed scale
        FramesUntilIdle(h, TimeSpan.FromSeconds(2));
        scale.Offer(scale.To(1.0).Spring(new Spring(Omega: 20, Decay: 40)));
        long frames = FramesUntilIdle(h, TimeSpan.FromSeconds(2));

        Assert.Equal(1.0, scale.Value);
        Assert.InRange(frames, 20, 30);
    }
}
