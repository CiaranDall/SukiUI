using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace SukiUI.Motion.Tests;

/// <summary>Channel.Offer — the "who owns the channel" rules.</summary>
public class ArbitrationTests
{
    private static readonly Spring Hover = new(Omega: 20, Decay: 28); // zeta = 0.7

    // Bug 2: a spring offered while another spring runs was silently dropped, so a
    // spring-driven hover that is left mid-flight stayed at the hover pose forever.
    [AvaloniaFact]
    public void Spring_offered_during_a_running_spring_takes_over()
    {
        var border = new Border();
        using var h = new MotionHarness(border);
        var scale = Motion.For(border).Scale;
        var enter = scale.To(1.1).Spring(Hover);
        var exit = scale.To(1.0).Spring(Hover);

        scale.Offer(enter);
        h.Frames(3);           // pointer enters, spring in flight
        scale.Offer(exit);     // pointer leaves mid-flight
        h.Run(TimeSpan.FromSeconds(2));

        Assert.Equal(1.0, scale.Value, precision: 3);
    }

    // The takeover keeps the live velocity: no kink in the trajectory at the switch.
    [AvaloniaFact]
    public void Spring_takeover_carries_the_live_velocity()
    {
        var border = new Border();
        using var h = new MotionHarness(border);
        var scale = Motion.For(border).Scale;
        var enter = scale.To(1.1).Spring(Hover);
        var exit = scale.To(1.0).Spring(Hover);

        scale.Offer(enter);
        h.Frames(3);
        double velocityBefore = scale.Velocity;
        scale.Offer(exit);

        Assert.True(velocityBefore > 0);
        Assert.Equal(velocityBefore, scale.Velocity, precision: 6);
    }
}
