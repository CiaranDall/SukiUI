using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace SukiUI.Motion.Composition
{
    internal static class ExperimentalIds
    {
        /// <summary>The composition backend prototype (PLAN D21): API and semantics may change.</summary>
        internal const string CompositionBackend = "SUKIMOTION001";
    }

    /// <summary>
    /// PROTOTYPE (PLAN D21) — transform channels played by Avalonia's compositor on the render
    /// thread: once started, they keep moving while the UI thread is busy. One surface per visual.
    /// </summary>
    [Experimental(ExperimentalIds.CompositionBackend)]
    public static class CompositionMotion
    {
        private static readonly ConditionalWeakTable<Visual, CompositionSurface> Surfaces = new();

        public static CompositionSurface For(Visual visual) =>
            Surfaces.GetValue(visual, v => new CompositionSurface(v));

        /// <summary>
        /// Completes once every composition change issued so far — the starts included — has reached
        /// the render thread. A start only travels with the next commit, and commits run ON THE UI
        /// THREAD: a start followed by synchronous heavy work in the same handler does not move until
        /// that work ends (measured, ENGINEERING_NOTES §7.15). Await this between the start and the
        /// heavy work: <c>x.SpringTo(…); await CompositionMotion.CommitAsync(); BuildPage();</c>.
        /// </summary>
        public static Task CommitAsync() =>
            Compositor.TryGetDefaultCompositor()?.RequestCommitAsync() ?? Task.CompletedTask;
    }

    /// <summary>
    /// The composition-backed channels of one visual. Axes of one composition property are played
    /// together: TranslateX and TranslateY are the two components of <c>Translation</c>, so starting
    /// either rebuilds the whole <c>Translation</c> animation from both curves (one key-frame track per
    /// property — PLAN D21).
    /// <para>
    /// Pushes are deferred to the commit (<see cref="Compositor.RequestCompositionUpdate"/>): the
    /// compositor times an animation from its batch commit, which runs on the UI thread at the next
    /// render pass — possibly long after the call if the UI thread is busy. Building the key frames
    /// AT the commit, and re-timing the curves started since the previous commit to that moment,
    /// keeps the UI-side curves (pose and velocity for interruptions, completion) in step with the
    /// screen (ENGINEERING_NOTES §7.15). It also makes the push reflect the final state of the turn:
    /// <c>SpringTo</c> then <c>Pose</c> in one handler sends only the pose.
    /// </para>
    /// </summary>
    [Experimental(ExperimentalIds.CompositionBackend)]
    public sealed class CompositionSurface
    {
        // Interpolation tolerance of the sampled key frames (see KeyFrameGrid).
        private const double TranslateTolerance = 0.25; // DIPs
        private const double ScaleTolerance = 0.0025;   // ≈ 0.25 DIP on a 100-DIP element

        private static readonly LinearEasing Linear = new();
        private readonly Visual _visual;
        private bool _translationDirty, _scaleDirty, _flushRequested;

        internal CompositionSurface(Visual visual)
        {
            _visual = visual;
            TranslateX = new CompositionChannel(this, isScale: false, rest: 0.0);
            TranslateY = new CompositionChannel(this, isScale: false, rest: 0.0);
            Scale = new CompositionChannel(this, isScale: true, rest: 1.0);

            // A composition visual exists only while attached and may be re-created: re-apply the
            // poses (and running curves) to the fresh one.
            visual.AttachedToVisualTree += (_, _) =>
            {
                MarkDirty(isScale: false);
                MarkDirty(isScale: true);
            };
            visual.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.BoundsProperty || e.Property == Visual.RenderTransformOriginProperty)
                    UpdateCenter();
            };
        }

        /// <summary>Horizontal translation in parent coordinates (composition <c>Translation.X</c>).</summary>
        public CompositionChannel TranslateX { get; }

        /// <summary>Vertical translation in parent coordinates (composition <c>Translation.Y</c>).</summary>
        public CompositionChannel TranslateY { get; }

        /// <summary>Uniform scale around the visual's <see cref="Visual.RenderTransformOrigin"/>. Applied
        /// AFTER the visual's RenderTransform (ENGINEERING_NOTES §7.11).</summary>
        public CompositionChannel Scale { get; }

        /// <summary>Schedules a push of one property at the next commit.</summary>
        internal void MarkDirty(bool isScale)
        {
            if (isScale)
                _scaleDirty = true;
            else
                _translationDirty = true;
            if (_flushRequested || ElementComposition.GetElementVisual(_visual) is not { } visual)
                return; // already scheduled, or detached (re-marked at the next attach)
            _flushRequested = true;
            visual.Compositor.RequestCompositionUpdate(Flush);
        }

        /// <summary>Runs on the UI thread immediately before the commit — the instant the compositor
        /// will time the pushed animations from.</summary>
        private void Flush()
        {
            _flushRequested = false;
            if (ElementComposition.GetElementVisual(_visual) is not { } visual)
                return;
            var now = SukiTicker.Now;
            if (_translationDirty)
            {
                _translationDirty = false;
                TranslateX.Commit(now);
                TranslateY.Commit(now);
                Push(visual, isScale: false, now);
            }
            if (_scaleDirty)
            {
                _scaleDirty = false;
                Scale.Commit(now);
                UpdateCenter();
                Push(visual, isScale: true, now);
            }
        }

        /// <summary>Pushes the current curves of one property: a static value at rest, otherwise one
        /// sampled key-frame animation covering the longest remaining curve.</summary>
        private void Push(CompositionVisual visual, bool isScale, TimeSpan now)
        {
            double remaining = isScale
                ? Scale.Remaining(now)
                : Math.Max(TranslateX.Remaining(now), TranslateY.Remaining(now));

            if (remaining <= 0)
            {
                SetStatic(visual, isScale, Sample(isScale, now, final: true));
                return;
            }

            var anim = visual.Compositor.CreateVector3DKeyFrameAnimation();
            double tolerance = isScale ? ScaleTolerance : TranslateTolerance;
            var times = KeyFrameGrid.Build(
                tau => MaxAcceleration(isScale, now + TimeSpan.FromSeconds(tau)), remaining, tolerance);
            for (int i = 0; i < times.Count; i++)
            {
                // The first segment starts from the compositor's CURRENT value (implicit start): pose
                // continuity on interruption. The last key is the exact final pose, so server and
                // client agree once at rest.
                bool last = i == times.Count - 1;
                var value = last
                    ? Sample(isScale, now, final: true)
                    : Sample(isScale, now + TimeSpan.FromSeconds(times[i]), final: false);
                anim.InsertKeyFrame(last ? 1f : (float)(times[i] / remaining), value, Linear);
            }
            anim.Duration = TimeSpan.FromSeconds(remaining);
            visual.StartAnimation(isScale ? "Scale" : "Translation", anim);
        }

        private double MaxAcceleration(bool isScale, TimeSpan at) => isScale
            ? Math.Abs(Scale.AccelerationAt(at))
            : Math.Max(Math.Abs(TranslateX.AccelerationAt(at)), Math.Abs(TranslateY.AccelerationAt(at)));

        private Vector3D Sample(bool isScale, TimeSpan at, bool final)
        {
            if (isScale)
            {
                double s = final ? Scale.FinalValue : Scale.PositionAt(at);
                return new Vector3D(s, s, 1);
            }
            double x = final ? TranslateX.FinalValue : TranslateX.PositionAt(at);
            double y = final ? TranslateY.FinalValue : TranslateY.PositionAt(at);
            return new Vector3D(x, y, 0);
        }

        /// <summary>
        /// A static write that ALWAYS reaches the compositor. The client setter skips a value equal to
        /// its field, and that field is the last UI-side write, not what is on screen
        /// (ENGINEERING_NOTES §7.12): <c>Pose(0)</c> during an animation that started from 0 would be
        /// silently dropped and the animation would keep running. A neighbouring value first makes
        /// the write a change; only the last value of the batch is sent.
        /// </summary>
        private static void SetStatic(CompositionVisual visual, bool isScale, Vector3D value)
        {
            var nudge = new Vector3D(value.X + 1e-4, value.Y, value.Z);
            if (isScale)
            {
                if (visual.Scale == value)
                    visual.Scale = nudge;
                visual.Scale = value;
            }
            else
            {
                if (visual.Translation == value)
                    visual.Translation = nudge;
                visual.Translation = value;
            }
        }

        private void UpdateCenter()
        {
            if (ElementComposition.GetElementVisual(_visual) is not { } visual)
                return;
            var origin = _visual.RenderTransformOrigin.ToPixels(_visual.Bounds.Size);
            visual.CenterPoint = new Vector3D(origin.X, origin.Y, 0);
        }
    }

    /// <summary>
    /// One composition-backed axis. Its pose and velocity come from an analytic curve on the UI
    /// thread (the compositor cannot be read back); the compositor plays the same curve, sampled.
    /// Every start begins from the live pose and velocity — interruptions keep momentum.
    /// No per-frame UI-thread work: completion is a single timer.
    /// </summary>
    [Experimental(ExperimentalIds.CompositionBackend)]
    public sealed class CompositionChannel
    {
        private readonly CompositionSurface _owner;
        private readonly bool _isScale;
        private ICurve _curve;
        private double _final;
        private TimeSpan _start;
        private bool _uncommitted;   // started since the last commit: its timing is provisional
        private IDisposable? _settle;

        internal CompositionChannel(CompositionSurface owner, bool isScale, double rest)
        {
            _owner = owner;
            _isScale = isScale;
            _curve = new RestCurve(rest);
            _final = rest;
        }

        /// <summary>Current pose, from the curve (what the compositor is playing, within ~1 frame).</summary>
        public double Value => PositionAt(SukiTicker.Now);

        /// <summary>Current velocity, from the curve.</summary>
        public double Velocity => _curve.Velocity(Elapsed(SukiTicker.Now));

        public bool IsAnimating => _settle is not null || (_uncommitted && _curve.Duration > 0);

        /// <summary>Raised once the running curve has come to rest on screen (not for <see cref="Pose"/>).</summary>
        public event Action? Settled;

        /// <summary>A damped spring toward <paramref name="target"/> from the live pose, carrying the
        /// live velocity unless an explicit <paramref name="initialVelocity"/> (a kick) is given.</summary>
        public void SpringTo(double target, Spring spring, double? initialVelocity = null) =>
            Start(new SpringCurve(Value, initialVelocity ?? Velocity, target, spring), target);

        /// <summary>A timed eased move toward <paramref name="target"/> from the live pose.</summary>
        public void EaseTo(double target, TimeSpan duration, Easing? easing = null) =>
            Start(new EasedCurve(Value, target, duration, easing ?? new LinearEasing()), target);

        /// <summary>An immediate pose: stops whatever runs.</summary>
        public void Pose(double value) => Start(new RestCurve(value), value);

        internal double FinalValue => _final;

        internal double PositionAt(TimeSpan at) => _curve.Position(Elapsed(at));

        internal double AccelerationAt(TimeSpan at) => _curve.Acceleration(Elapsed(at));

        internal double Remaining(TimeSpan now) => Math.Max(0.0, _curve.Duration - Elapsed(now));

        /// <summary>
        /// The commit that carries this channel's push is happening now. A curve started since the
        /// previous commit has not been on screen yet: it starts NOW (re-timed from its call time;
        /// the gap is the UI thread's busy time) and its completion is re-armed accordingly.
        /// </summary>
        internal void Commit(TimeSpan now)
        {
            if (!_uncommitted)
                return; // already on screen and timed: the rebuilt animation continues it
            _uncommitted = false;
            _start = now;
            ArmSettle();
        }

        /// <summary>Elapsed curve time at <paramref name="at"/>. A curve not committed yet has not
        /// started on screen: it is held at its beginning.</summary>
        private double Elapsed(TimeSpan at) => _uncommitted ? 0.0 : Math.Max(0.0, (at - _start).TotalSeconds);

        private void Start(ICurve curve, double final)
        {
            _settle?.Dispose();
            _settle = null;
            _curve = curve;
            _final = final;
            _start = SukiTicker.Now;
            _uncommitted = true; // timed for real at the commit (CompositionSurface.Flush)
            _owner.MarkDirty(_isScale);
        }

        private void ArmSettle()
        {
            _settle?.Dispose();
            _settle = _curve.Duration > 0
                ? DispatcherTimer.RunOnce(OnSettled, TimeSpan.FromSeconds(_curve.Duration))
                : null;
        }

        private void OnSettled()
        {
            _settle = null;
            _curve = new RestCurve(_final);
            _start = SukiTicker.Now;
            _owner.MarkDirty(_isScale); // static final pose; the other axis may still be running
            Settled?.Invoke();
        }
    }
}
