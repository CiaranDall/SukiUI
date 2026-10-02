using System;

namespace SukiUI.Motion
{
    /// <summary>
    /// The damped-spring parameters of a trajectory: <c>x'' = -omega²(x - target) - decay·x'</c>.
    /// The low-level truth of the engine — the exact pixels of the integrated physics.
    /// SwiftUI-style sugar (<c>Spring(duration:, bounce:)</c>) is deliberately absent
    /// for now: it is a spelling convenience on top of these two numbers.
    /// Both parameters must be finite and positive: a spring without stiffness or without
    /// damping never settles, and would keep the frame loop awake forever.
    /// </summary>
    public readonly record struct Spring
    {
        public Spring(double Omega, double Decay)
        {
            if (!double.IsFinite(Omega) || Omega <= 0)
                throw new ArgumentOutOfRangeException(nameof(Omega), Omega, "Spring stiffness (omega) must be finite and > 0.");
            if (!double.IsFinite(Decay) || Decay <= 0)
                throw new ArgumentOutOfRangeException(nameof(Decay), Decay, "Spring damping (decay) must be finite and > 0 — an undamped spring never settles.");
            this.Omega = Omega;
            this.Decay = Decay;
        }

        /// <summary>Angular frequency in rad/s. Higher = snappier.</summary>
        public double Omega { get; }

        /// <summary>Damping in 1/s (<c>2·zeta·omega</c>). Higher = fewer/softer overshoots.</summary>
        public double Decay { get; }

        /// <summary>True for a spring built through the constructor; false for <c>default(Spring)</c>.</summary>
        internal bool IsValid => Omega > 0 && Decay > 0;

        public void Deconstruct(out double Omega, out double Decay)
        {
            Omega = this.Omega;
            Decay = this.Decay;
        }
    }

    /// <summary>
    /// The shared integrator: semi-implicit Euler with substeps capped at 8 ms — the exact
    /// numeric behavior of the proven engine, so trajectories settle to the same poses at the
    /// same substep cadence. Stiff or heavily damped springs get finer substeps: the scheme
    /// is only stable while omega·h and decay·h stay small (both kept ≤ 1 here). Time base
    /// is caller-provided <c>dt</c>, always derived from <see cref="SukiTicker"/> so every
    /// spring shares the same monotonic clock.
    /// </summary>
    internal static class Integrator
    {
        /// <summary>The historical substep cap — unchanged for every spring soft enough.</summary>
        private const double MaxSubstep = 0.008;

        internal static void Step(ref double x, ref double v, double target, double dt, in Spring spring)
        {
            // omega·h ≤ 1 and decay·h ≤ 1 keep semi-implicit Euler stable for any spring
            // (its update matrix has |eigenvalues| < 1 there); softer springs keep 8 ms.
            double maxStep = Math.Min(MaxSubstep, 1.0 / Math.Max(spring.Omega, spring.Decay));
            int steps = Math.Max(1, (int)Math.Ceiling(dt / maxStep));
            double h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                double accel = -spring.Omega * spring.Omega * (x - target) - spring.Decay * v;
                v += accel * h;
                x += v * h;
            }
        }

        internal static double Lerp(double from, double to, double t) => from + (to - from) * t;
    }

    /// <summary>
    /// When a spring counts as at rest: |x - target| &lt; <see cref="Position"/> and
    /// |v| &lt; <see cref="Velocity"/>, then it snaps exactly onto the target and stops ticking.
    /// Sized per channel unit so the final snap stays invisible without ticking (and re-rendering)
    /// long after the motion stopped on screen (P8, ENGINEERING_NOTES §8.2). The velocity bound is
    /// 40 × the position bound in every unit — the historical ratio.
    /// </summary>
    internal readonly record struct SettleThreshold(double Position, double Velocity)
    {
        /// <summary>Unitless channels (scale, opacity) and generic properties of unknown unit — the
        /// historical thresholds: 0.0005 of scale is ~0.1 DIP on a 200-DIP element, ~1/8 of an
        /// 8-bit opacity level.</summary>
        internal static readonly SettleThreshold Unitless = new(0.0005, 0.02);

        /// <summary>Channels in DIPs (translation, blur radius, shadow offsets and blur): a snap of
        /// at most 1/20 DIP (0.1 device pixel at 200 %).</summary>
        internal static readonly SettleThreshold Dip = new(0.05, 2.0);

        /// <summary>Channels in degrees (rotation, skew): 0.01° moves a point 300 DIPs from the
        /// origin by ~0.05 DIP.</summary>
        internal static readonly SettleThreshold Degrees = new(0.01, 0.4);

        internal bool IsSettled(double displacement, double velocity) =>
            Math.Abs(displacement) < Position && Math.Abs(velocity) < Velocity;
    }
}
