using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace SukiUI.Motion.Tests;

public class SpringTests
{
    // Bug 10a: the integrator's substep is capped at 8 ms whatever the stiffness. Explicit
    // (semi-implicit Euler) integration is only stable for roughly omega·h < 2 and
    // decay·h < 2, so a stiff or heavily damped spring diverges instead of settling.
    [AvaloniaTheory]
    [InlineData(400.0, 560.0)] // stiff: omega·h = 3.2
    [InlineData(30.0, 400.0)]  // heavily overdamped: decay·h = 3.2
    public void Stiff_or_heavily_damped_spring_settles_on_its_target(double omega, double decay)
    {
        var border = new Border();
        using var h = new MotionHarness(border);
        var scale = Motion.For(border).Scale;

        scale.Offer(scale.To(1.1).Spring(new Spring(omega, decay)));
        h.Run(TimeSpan.FromSeconds(3));

        Assert.Equal(1.1, scale.Value, precision: 3);
    }

    // Bug 10b: nothing rejects parameters for which a spring can never settle — zero or
    // negative decay oscillates (or grows) forever and keeps the frame loop awake for good.
    [Theory]
    [InlineData(10.0, 0.0)]
    [InlineData(10.0, -1.0)]
    [InlineData(0.0, 5.0)]
    [InlineData(double.NaN, 5.0)]
    [InlineData(10.0, double.PositiveInfinity)]
    public void Spring_rejects_parameters_that_can_never_settle(double omega, double decay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Spring(omega, decay));
    }
}
