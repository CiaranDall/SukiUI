using System;
using System.Collections.Generic;

namespace SukiUI.Motion.Composition
{
    /// <summary>
    /// The adaptive time grid of a sampled curve. Linear interpolation over a segment of length h
    /// errs by about |a|·h²/8, so each step is <c>sqrt(8·tolerance/|a|)</c>, clamped to
    /// [<see cref="MinStep"/>, <see cref="MaxStep"/>]. The acceleration is probed at the start, middle
    /// and end of each candidate step and the worst one kept (an oscillating spring's acceleration
    /// crosses zero — a start-only probe would leap over the curved stretch that follows).
    /// A fixed 4 ms step cost ~1000 keys per start on a bouncy spring (ENGINEERING_NOTES §7.14).
    /// Guarantee: the tolerance holds while |a| ≤ 8·tolerance/MinStep² (2·10⁶ DIP/s² at 0.25 DIP) —
    /// every realistic UI spring (ω = 60 over 300 DIP peaks near 1.1·10⁶). Stiffer curves complete
    /// within a frame or two: sub-frame detail is not visible anyway, they land exactly on target.
    /// </summary>
    internal static class KeyFrameGrid
    {
        internal const double MinStep = 0.001, MaxStep = 0.1;

        /// <summary>Key times in (0, duration]; the last one is exactly <paramref name="duration"/>.</summary>
        internal static List<double> Build(Func<double, double> acceleration, double duration, double tolerance)
        {
            var times = new List<double>();
            double t = 0;
            while (t < duration)
            {
                double h = StepFor(Math.Abs(acceleration(t)), tolerance);
                // Refine against the worst acceleration inside the candidate step (two passes suffice:
                // the step only ever shrinks).
                for (int pass = 0; pass < 2; pass++)
                {
                    double worst = Math.Max(Math.Abs(acceleration(Math.Min(t + h / 2, duration))),
                                            Math.Abs(acceleration(Math.Min(t + h, duration))));
                    h = Math.Min(h, StepFor(worst, tolerance));
                }
                t = Math.Min(t + h, duration);
                times.Add(t);
            }
            if (times.Count == 0)
                times.Add(duration);
            return times;
        }

        private static double StepFor(double accel, double tolerance) =>
            accel > 0 ? Math.Clamp(Math.Sqrt(8 * tolerance / accel), MinStep, MaxStep) : MaxStep;
    }
}
