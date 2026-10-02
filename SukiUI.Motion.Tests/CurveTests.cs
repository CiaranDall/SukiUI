using Avalonia.Animation.Easings;
using SukiUI.Motion.Composition;

namespace SukiUI.Motion.Tests;

/// <summary>The analytic curves of the composition backend prototype (PLAN D21).</summary>
public class CurveTests
{
    // Under-, critically and over-damped; profiles in the SukiUI range plus a stiff one.
    public static TheoryData<double, double> Springs => new()
    {
        { 16.0, 9.333 },  // press release (underdamped)
        { 20.0, 40.0 },   // critically damped
        { 20.0, 40.8 },   // popup open (barely overdamped)
        { 12.0, 60.0 },   // clearly overdamped
        { 400.0, 560.0 }, // stiff
    };

    // The closed form must agree with a fine-step integration of the same ODE — including an
    // initial velocity, which the UI engine's springs rarely have but interruptions always do.
    [Theory]
    [MemberData(nameof(Springs))]
    public void Spring_curve_matches_fine_numeric_integration(double omega, double decay)
    {
        var spring = new Spring(omega, decay);
        double x0 = 0.9, v0 = 3.0, target = 1.2;
        var curve = new SpringCurve(x0, v0, target, spring);

        // Reference: classic RK4 at 10 µs (error ~1e-12). A first-order scheme is NOT a valid
        // reference for stiff springs: at omega = 400 semi-implicit Euler is still 1e-5 off at h = 1 µs.
        double x = x0, v = v0, t = 0, h = 1e-5;
        double Accel(double px, double pv) => -omega * omega * (px - target) - decay * pv;
        foreach (double probe in new[] { 0.01, 0.05, 0.1, 0.3, 0.6 })
        {
            while (t < probe - h / 2)
            {
                double k1x = v, k1v = Accel(x, v);
                double k2x = v + h / 2 * k1v, k2v = Accel(x + h / 2 * k1x, v + h / 2 * k1v);
                double k3x = v + h / 2 * k2v, k3v = Accel(x + h / 2 * k2x, v + h / 2 * k2v);
                double k4x = v + h * k3v, k4v = Accel(x + h * k3x, v + h * k3v);
                x += h / 6 * (k1x + 2 * k2x + 2 * k3x + k4x);
                v += h / 6 * (k1v + 2 * k2v + 2 * k3v + k4v);
                t += h;
            }
            Assert.Equal(x, curve.Position(probe), precision: 8);
            Assert.Equal(v, curve.Velocity(probe), precision: 6);
        }
    }

    [Theory]
    [MemberData(nameof(Springs))]
    public void Spring_curve_starts_at_its_initial_state(double omega, double decay)
    {
        var curve = new SpringCurve(x0: 0.5, v0: -2.0, target: 1.0, new Spring(omega, decay));

        Assert.Equal(0.5, curve.Position(0), precision: 12);
        Assert.Equal(-2.0, curve.Velocity(0), precision: 9);
    }

    // Duration is where the curve is settled for good (never early), and not absurdly late.
    [Theory]
    [MemberData(nameof(Springs))]
    public void Spring_curve_duration_is_settled_and_not_wildly_conservative(double omega, double decay)
    {
        var curve = new SpringCurve(x0: 0.0, v0: 5.0, target: 1.0, new Spring(omega, decay));
        double d = curve.Duration;

        for (double t = d; t < d + 2.0; t += 0.001)
        {
            Assert.True(Math.Abs(curve.Position(t) - 1.0) < Integrator.SettlePosition, $"position at {t}");
            Assert.True(Math.Abs(curve.Velocity(t)) < Integrator.SettleVelocity, $"velocity at {t}");
        }

        // The last instant the curve is OUTSIDE the thresholds — the true settle time.
        double last = 0;
        for (double t = 0; t < d; t += 0.0005)
            if (Math.Abs(curve.Position(t) - 1.0) >= Integrator.SettlePosition || Math.Abs(curve.Velocity(t)) >= Integrator.SettleVelocity)
                last = t;
        Assert.True(d <= last * 1.6 + 0.02, $"duration {d:0.000}s vs true settle {last:0.000}s");
    }

    // An interruption builds a new curve from the old one's live state: no jump, no kink.
    [Fact]
    public void Interrupting_a_spring_keeps_pose_and_velocity_continuous()
    {
        var first = new SpringCurve(x0: 0, v0: 0, target: 150, new Spring(14, 9));
        double t1 = 0.12;
        var second = new SpringCurve(first.Position(t1), first.Velocity(t1), target: 0, new Spring(14, 9));

        Assert.Equal(first.Position(t1), second.Position(0), precision: 9);
        Assert.Equal(first.Velocity(t1), second.Velocity(0), precision: 6);
    }

    [Fact]
    public void Eased_curve_endpoints_and_velocity()
    {
        var curve = new EasedCurve(10, 110, TimeSpan.FromMilliseconds(200), new LinearEasing());

        Assert.Equal(10, curve.Position(0));
        Assert.Equal(60, curve.Position(0.1), precision: 9);
        Assert.Equal(110, curve.Position(0.2));
        Assert.Equal(110, curve.Position(5));
        Assert.Equal(500, curve.Velocity(0.1), precision: 6); // 100 units / 0.2 s
        Assert.Equal(0, curve.Velocity(0.3));
    }
}
