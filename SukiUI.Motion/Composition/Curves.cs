using System;
using Avalonia.Animation.Easings;

namespace SukiUI.Motion.Composition
{
    /// <summary>
    /// An analytic one-axis trajectory, evaluated at any time since its start (seconds). The
    /// composition backend keeps one per channel on the UI thread: the render thread only plays
    /// pre-sampled key frames and cannot be read back (ENGINEERING_NOTES §7.5), so pose and velocity
    /// for interruptions come from here.
    /// </summary>
    internal interface ICurve
    {
        /// <summary>Seconds until the trajectory rests on its final pose.</summary>
        double Duration { get; }

        double Position(double t);

        double Velocity(double t);
    }

    /// <summary>A pose at rest.</summary>
    internal sealed class RestCurve : ICurve
    {
        private readonly double _value;

        public RestCurve(double value) => _value = value;

        public double Duration => 0.0;

        public double Position(double t) => _value;

        public double Velocity(double t) => 0.0;
    }

    /// <summary>
    /// The exact solution of <c>x'' = -omega²(x - target) - decay·x'</c> from (x0, v0), in all three
    /// damping regimes. Unlike the UI engine's integrator it can be evaluated at any instant, which
    /// is what an interruption needs. Settles at the same thresholds as the integrator.
    /// </summary>
    internal sealed class SpringCurve : ICurve
    {
        private const double MaxDuration = 30.0; // safety cap: a valid spring always settles far sooner

        private enum Regime { Under, Critical, Over }

        private readonly double _target, _d0, _a;
        private readonly Regime _regime;
        private readonly double _w;      // omega_d (under) or omega_o (over)
        private readonly double _b;      // under / critical second coefficient
        private readonly double _r1, _r2, _c1, _c2; // over: modes and amplitudes

        public SpringCurve(double x0, double v0, double target, Spring spring)
        {
            if (!spring.IsValid)
                throw new ArgumentException("default(Spring) has no stiffness or damping.", nameof(spring));
            _target = target;
            _d0 = x0 - target;
            _a = spring.Decay / 2.0;
            double omega2 = spring.Omega * spring.Omega;
            double disc = omega2 - _a * _a;

            if (Math.Abs(disc) <= 1e-9 * omega2)
            {
                _regime = Regime.Critical;
                _b = v0 + _a * _d0;
            }
            else if (disc > 0)
            {
                _regime = Regime.Under;
                _w = Math.Sqrt(disc);
                _b = (v0 + _a * _d0) / _w;
            }
            else
            {
                _regime = Regime.Over;
                _w = Math.Sqrt(-disc);
                _r1 = -_a + _w; // slow mode (closest to 0)
                _r2 = -_a - _w;
                _c1 = (v0 - _r2 * _d0) / (_r1 - _r2);
                _c2 = _d0 - _c1;
            }

            Duration = ComputeDuration();
        }

        public double Duration { get; }

        public double Position(double t) => _target + Displacement(t);

        public double Velocity(double t)
        {
            switch (_regime)
            {
                case Regime.Under:
                {
                    double e = Math.Exp(-_a * t), c = Math.Cos(_w * t), s = Math.Sin(_w * t);
                    return e * ((_b * _w - _a * _d0) * c - (_a * _b + _d0 * _w) * s);
                }
                case Regime.Critical:
                    return Math.Exp(-_a * t) * (_b - _a * (_d0 + _b * t));
                default:
                    return _c1 * _r1 * Math.Exp(_r1 * t) + _c2 * _r2 * Math.Exp(_r2 * t);
            }
        }

        private double Displacement(double t) => _regime switch
        {
            Regime.Under => Math.Exp(-_a * t) * (_d0 * Math.Cos(_w * t) + _b * Math.Sin(_w * t)),
            Regime.Critical => Math.Exp(-_a * t) * (_d0 + _b * t),
            _ => _c1 * Math.Exp(_r1 * t) + _c2 * Math.Exp(_r2 * t),
        };

        /// <summary>
        /// First time from which BOTH |displacement| and |velocity| stay under the settle thresholds,
        /// from envelope bounds (conservative: never early). Under/over-damped bounds are pure
        /// exponentials and solve in closed form; the critical bound (polynomial × exponential) is
        /// scanned.
        /// </summary>
        private double ComputeDuration()
        {
            double eps = Integrator.SettlePosition, epsV = Integrator.SettleVelocity;
            double t;
            switch (_regime)
            {
                case Regime.Under:
                {
                    double kp = Math.Abs(_d0) + Math.Abs(_b);
                    double kv = Math.Abs(_b * _w - _a * _d0) + Math.Abs(_a * _b + _d0 * _w);
                    t = Math.Max(Solve(kp, eps, _a), Solve(kv, epsV, _a));
                    break;
                }
                case Regime.Over:
                {
                    double kp = Math.Abs(_c1) + Math.Abs(_c2);
                    double kv = Math.Abs(_c1 * _r1) + Math.Abs(_c2 * _r2);
                    t = Math.Max(Solve(kp, eps, -_r1), Solve(kv, epsV, -_r1));
                    break;
                }
                default:
                {
                    // |d| <= e^(-a t)(|d0| + |b| t), |v| <= e^(-a t)(|b| + a|d0| + a|b| t); both bounds
                    // decrease once t > 1/a, so scan from there.
                    t = 0;
                    const double step = 0.001;
                    while (t < MaxDuration)
                    {
                        double e = Math.Exp(-_a * t);
                        double bp = e * (Math.Abs(_d0) + Math.Abs(_b) * t);
                        double bv = e * (Math.Abs(_b) + _a * Math.Abs(_d0) + _a * Math.Abs(_b) * t);
                        if (bp < eps && bv < epsV && t > 1.0 / _a)
                            break;
                        t += step;
                    }
                    break;
                }
            }
            return Math.Clamp(t, 0.0, MaxDuration);
        }

        /// <summary>Smallest t ≥ 0 with k·e^(-rate·t) &lt; eps.</summary>
        private static double Solve(double k, double eps, double rate) =>
            k <= eps ? 0.0 : Math.Log(k / eps) / rate;
    }

    /// <summary>A timed eased trajectory: <c>lerp(from, to, ease(t / T))</c>.</summary>
    internal sealed class EasedCurve : ICurve
    {
        private const double DerivativeStep = 0.0005; // seconds, for the numeric velocity

        private readonly double _from, _to, _duration;
        private readonly Easing _easing;

        public EasedCurve(double from, double to, TimeSpan duration, Easing easing)
        {
            _from = from;
            _to = to;
            _duration = Math.Max(duration.TotalSeconds, 0.0);
            _easing = easing;
        }

        public double Duration => _duration;

        public double Position(double t)
        {
            if (_duration <= 0 || t >= _duration)
                return _to;
            if (t <= 0)
                return _from;
            return _from + (_to - _from) * _easing.Ease(t / _duration);
        }

        public double Velocity(double t)
        {
            if (_duration <= 0 || t >= _duration || t < 0)
                return 0.0;
            double t0 = Math.Max(0.0, t - DerivativeStep), t1 = Math.Min(_duration, t + DerivativeStep);
            return (Position(t1) - Position(t0)) / (t1 - t0);
        }
    }
}
