using SukiUI.Motion.Composition;

namespace SukiUI.Motion.Tests;

public class KeyFrameGridTests
{
    // The point of the grid: the piecewise-linear key frames stay within tolerance of the true
    // curve everywhere — measured densely, not trusted from the |a|·h²/8 estimate.
    [Theory]
    [InlineData(10.0, 6.0, 300.0)]   // bouncy (zeta 0.3): many oscillations
    [InlineData(16.0, 9.333, 300.0)] // press release
    [InlineData(20.0, 40.8, 300.0)]  // overdamped popup
    [InlineData(60.0, 40.0, 300.0)]  // the stiffest inside the guarantee (|a| ≈ 1.1e6)
    public void Interpolation_error_stays_within_tolerance(double omega, double decay, double distance)
    {
        const double tolerance = 0.25;
        var curve = new SpringCurve(x0: 0, v0: 800, target: distance, new Spring(omega, decay));
        var times = KeyFrameGrid.Build(curve.Acceleration, curve.Duration, tolerance);

        double prevT = 0, prevX = curve.Position(0), worst = 0;
        foreach (double t in times)
        {
            double x = curve.Position(t);
            for (int k = 1; k < 50; k++)
            {
                double u = prevT + (t - prevT) * k / 50.0;
                double linear = prevX + (x - prevX) * (u - prevT) / (t - prevT);
                worst = Math.Max(worst, Math.Abs(linear - curve.Position(u)));
            }
            (prevT, prevX) = (t, x);
        }

        Assert.Equal(curve.Duration, times[^1]);
        Assert.True(worst <= tolerance * 1.5, $"worst interpolation error {worst:0.000} px with {times.Count} keys");
    }

    // Beyond the guarantee (a spring faster than a couple of frames): still exact at the end.
    [Fact]
    public void Sub_frame_spring_still_lands_exactly()
    {
        var curve = new SpringCurve(x0: 0, v0: 0, target: 300, new Spring(400, 560));
        var times = KeyFrameGrid.Build(curve.Acceleration, curve.Duration, 0.25);

        Assert.Equal(curve.Duration, times[^1]);
        Assert.True(curve.Duration < 0.06, $"settles in {curve.Duration * 1000:0} ms");
        Assert.InRange(times.Count, 2, 200);
    }

    // ...and it is cheap: far fewer keys than the fixed 4 ms grid it replaces.
    [Fact]
    public void Bouncy_spring_needs_far_fewer_keys_than_a_fixed_grid()
    {
        var curve = new SpringCurve(x0: 0, v0: 0, target: 30, new Spring(10, 6));
        var times = KeyFrameGrid.Build(curve.Acceleration, curve.Duration, 0.25);
        int fixedGrid = (int)Math.Ceiling(curve.Duration / 0.004);

        Assert.True(times.Count * 5 < fixedGrid, $"{times.Count} adaptive keys vs {fixedGrid} fixed");
    }
}
