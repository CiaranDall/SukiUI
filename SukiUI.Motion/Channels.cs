using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace SukiUI.Motion
{
    /// <summary>
    /// One animatable target: a resolved visual and its channels. Everything animable is
    /// a surface — the element, a template part, a popup root — differing only by the
    /// resolver; the channel vocabulary is identical everywhere. Channels are lazily
    /// created, one instance per property per surface, all writing through the resolver.
    /// The semantic factories carry the write protocols that cannot be derived from a
    /// property reference: the transform channels (scale, translate, rotate, skew)
    /// compose through one shared block per target (see <see cref="Transforms"/>); blur
    /// and shadow manage the single <see cref="Visual.Effect"/> slot's lifecycle (last
    /// writer owns it).
    /// </summary>
    public sealed class Surface
    {
        private static readonly ConditionalWeakTable<TemplatedControl, Dictionary<string, Surface>> Parts = new();

        private readonly Visual _owner; // ticker fallback owner; only used by the Offer path
        private readonly Func<Visual?> _target;

        private Channel? _scale, _scaleX, _scaleY, _blur, _translateX, _translateY, _rotate;
        private Channel? _skewX, _skewY;
        private Channel? _shadowBlur, _shadowOffsetX, _shadowOffsetY, _shadowOpacity;
        private Dictionary<StyledProperty<double>, Channel>? _properties;

        public Surface(Visual owner, Func<Visual?> target)
        {
            _owner = owner;
            _target = target;
        }

        /// <summary>Uniform render scale (transform block, X+Y, render-only — no layout).
        /// The first write attaches the block and schedules the frame the animation rides
        /// on.</summary>
        public Channel Scale => _scale ??= Channel.ForScale(_owner, _target);

        /// <summary>Horizontal render scale — through the shared transform block.</summary>
        public Channel ScaleX => _scaleX ??= Channel.ForScaleX(_owner, _target);

        /// <summary>Vertical render scale — through the shared transform block.</summary>
        public Channel ScaleY => _scaleY ??= Channel.ForScaleY(_owner, _target);

        /// <summary>Horizontal translation in parent coordinates — through the shared
        /// transform block (composes with scale and rotate instead of fighting over
        /// RenderTransform).</summary>
        public Channel TranslateX => _translateX ??= Channel.ForTranslateX(_owner, _target);

        /// <summary>Vertical translation in parent coordinates — through the shared
        /// transform block.</summary>
        public Channel TranslateY => _translateY ??= Channel.ForTranslateY(_owner, _target);

        /// <summary>Rotation in degrees around the target's RenderTransformOrigin —
        /// through the shared transform block.</summary>
        public Channel Rotate => _rotate ??= Channel.ForRotate(_owner, _target);

        /// <summary>Horizontal skew in degrees — through the shared transform block.
        /// Composition order is translate · skew · rotate · scale: the skew shears in the
        /// PARENT frame (applied after rotation in point order), so a leaning target that
        /// also rotates keeps a predictable screen-space shear.</summary>
        public Channel SkewX => _skewX ??= Channel.ForSkewX(_owner, _target);

        /// <summary>Vertical skew in degrees — through the shared transform block.</summary>
        public Channel SkewY => _skewY ??= Channel.ForSkewY(_owner, _target);

        /// <summary>Target opacity — the generic property channel over <see cref="Visual.OpacityProperty"/>.</summary>
        public Channel Opacity => Property(Visual.OpacityProperty);

        /// <summary>One engine-owned BlurEffect while blurring; below 0.5 DIP the blur lets go
        /// of the Effect slot — no shader pass once it has dissipated, and the base (styled)
        /// effect shows again. The Effect slot is single: a shadow-channel write takes it over,
        /// and back.</summary>
        public Channel Blur => _blur ??= Channel.ForBlur(_owner, _target);

        /// <summary>Drop-shadow opacity (0..1) — the shadow's PRESENCE channel over the
        /// single <see cref="Visual.Effect"/> slot: the engine shows its own copy of the base
        /// (styled) shadow — never the shared style instance — and below 0.02 shows none (no
        /// shader pass); back at the base shadow's parameters it lets go of the slot. Reads the
        /// DropShadowEffect on screen (pose continuity with a styled one). Mutually exclusive
        /// with <see cref="Blur"/> on one target: one slot, last writer owns it.</summary>
        public Channel ShadowOpacity => _shadowOpacity ??= Channel.ForShadowOpacity(_owner, _target);

        /// <summary>Drop-shadow blur radius in DIPs — over the single Effect slot (see
        /// <see cref="ShadowOpacity"/> for the slot rules).</summary>
        public Channel ShadowBlur => _shadowBlur ??= Channel.ForShadowBlur(_owner, _target);

        /// <summary>Drop-shadow horizontal offset — over the single Effect slot.</summary>
        public Channel ShadowOffsetX => _shadowOffsetX ??= Channel.ForShadowOffsetX(_owner, _target);

        /// <summary>Drop-shadow vertical offset — over the single Effect slot.</summary>
        public Channel ShadowOffsetY => _shadowOffsetY ??= Channel.ForShadowOffsetY(_owner, _target);

        /// <summary>Any double styled property — the generic channel. Writes land at
        /// Animation priority and are let go once back at the base value (style, local value,
        /// default), so styles keep working at rest (see <see cref="AnimationLayer"/>). The same
        /// property always yields the same channel (one arbitration state per animated property).</summary>
        public Channel Property(StyledProperty<double> property)
        {
            _properties ??= new Dictionary<StyledProperty<double>, Channel>();
            if (!_properties.TryGetValue(property, out var channel))
                _properties[property] = channel = Channel.ForProperty(_owner, property, _target);
            return channel;
        }

        // ---- descending from the element --------------------------------------------------

        /// <summary>A named template part of this element as an animatable surface — one
        /// surface per (host, name). The target re-resolves at every TemplateApplied (the
        /// proven popup-root rule); while the part does not resolve (template not applied
        /// yet, or the name does not exist) the channels no-op silently — the popup
        /// precedent — with a discrete trace.</summary>
        public Surface Part(string name)
        {
            if (_owner is not TemplatedControl host)
                throw new InvalidOperationException(
                    $"Surface.Part: template parts require a TemplatedControl host, got {_owner.GetType().Name}.");

            var parts = Parts.GetValue(host, _ => new Dictionary<string, Surface>());
            if (parts.TryGetValue(name, out var existing))
                return existing;

            Visual? target = null;
            var surface = new Surface(host, () => target);
            parts[name] = surface;
            Resolve();
            // Never unwired on purpose: the surface is held through the Parts table keyed
            // on the host — both live and die with it.
            host.TemplateApplied += (_, _) => Resolve();

            void Resolve()
            {
                target = host.GetTemplateDescendants()
                    .OfType<Control>()
                    .FirstOrDefault(c => c.Name == name);
                if (target is null)
                    Debug.WriteLine($"Surface.Part: '{name}' not found in the template of {host.GetType().Name} — channels no-op until it resolves.");
            }
            return surface;
        }

        /// <summary>The template popup of this element — a popup root surface plus the
        /// IsOpen lifecycle (see <see cref="PopupHandle"/>). Not cached: one handle per
        /// Attach, disposed with its Mover — a second call would observe a disposed handle.</summary>
        public PopupHandle Popup(
            string popupPart,
            string rootPart,
            string itemsPart,
            Func<bool> isHostOpen)
        {
            if (_owner is not TemplatedControl host)
                throw new InvalidOperationException(
                    $"Surface.Popup: popups require a TemplatedControl host, got {_owner.GetType().Name}.");
            return new PopupHandle(host, popupPart, rootPart, itemsPart, isHostOpen);
        }

        /// <summary>A popup whose real Popup is NOT a template part — resolved by the
        /// caller's rule (a code-built popup, e.g. the one ContextMenu.Open() parents the
        /// host into). Root and items resolve relative to that popup per the caller's rule;
        /// resolution re-runs at TemplateApplied AND at logical attach, so late-hosted
        /// flavors work.</summary>
        public PopupHandle Popup(
            Func<Popup?> resolvePopup,
            Func<Popup?, Control?> resolveRoot,
            Func<Control?, ItemsPresenter?> resolveItems,
            Func<bool> isHostOpen)
        {
            if (_owner is not TemplatedControl host)
                throw new InvalidOperationException(
                    $"Surface.Popup: popups require a TemplatedControl host, got {_owner.GetType().Name}.");
            return new PopupHandle(host, resolvePopup, resolveRoot, resolveItems, isHostOpen);
        }
    }

    /// <summary>
    /// One strongly typed, writable visual property of one target, carrying its own physics
    /// (<see cref="Value"/>, <see cref="Velocity"/>) and the channel arbitration — each rule
    /// a generalized branch of the proven press/popup engines:
    /// interrupting captures the on-screen pose (velocity only survives inside a spring);
    /// a MustFinish() chain owns the channel to completion (a spring offered meanwhile is
    /// memorized, a re-offered chain re-arms from the pose, hover is dropped); a spring in
    /// flight is preempted by a press chain or by a DIFFERENT spring (which takes over the
    /// live pose and velocity); an incoming hover never preempts it, it re-resolves its lazy
    /// target (retarget without snap, pose and velocity kept), as does the same spring
    /// re-offered; a plain
    /// timed trajectory is preemptable; a pose write wins over everything; a spring settles at
    /// the channel's <see cref="SettleThreshold"/> (sized to its unit) with an exact snap; an
    /// idle channel costs zero frame callbacks.
    /// Choreographies (popup) do not go through <see cref="Offer"/>: they use <see cref="Run"/>
    /// (forced preemption, spring velocity carry) and <see cref="PrePoseIfIdle"/> (the From
    /// rule), and own their ticker subscription themselves.
    /// </summary>
    public sealed class Channel
    {
        // A trajectory begun with From but never given a To fails loudly at Start.
        private static readonly Func<double> MissingTo =
            static () => throw new InvalidOperationException("A From trajectory was never given a To.");

        private readonly Visual _owner; // ticker fallback owner; only used by the Offer path
        private readonly Func<double> _read;
        private readonly Action<double> _write;
        private MotionProgram? _active;
        // The active program belongs to a stopped choreography: nobody advances it any more.
        // It still answers Velocity (the displacing spring carries it) and still counts as
        // in flight for the From rule, but Offer treats the channel as free.
        private bool _frozen;
        private IDisposable? _subscription;

        // Defensive pose-clamp window (the old engines clamped starts to
        // [DeepFloor, HoverScale]): grows as trajectory targets and Froms resolve; every
        // pose the behaviors can produce already fits it, so the clamp is a no-op unless
        // the transform was written by something else.
        private double _seenMin = double.PositiveInfinity;
        private double _seenMax = double.NegativeInfinity;
        private bool _startPoseTracked;

        private Channel(Visual owner, Func<double> read, Action<double> write, SettleThreshold? settle = null)
        {
            _owner = owner;
            _read = read;
            _write = write;
            Settle = settle ?? SettleThreshold.Unitless;
        }

        /// <summary>When a spring on this channel counts as at rest — sized to the channel's unit.</summary>
        internal SettleThreshold Settle { get; }

        // ---- channel factories: one write protocol each, all over a resolved target ------

        internal static Channel ForScale(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadScaleX(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteScale(t, v);
            });

        internal static Channel ForScaleX(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadScaleX(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteScaleX(t, v);
            });

        internal static Channel ForScaleY(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadScaleY(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteScaleY(t, v);
            });

        internal static Channel ForTranslateX(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadTranslateX(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteTranslateX(t, v);
            },
            SettleThreshold.Dip);

        internal static Channel ForTranslateY(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadTranslateY(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteTranslateY(t, v);
            },
            SettleThreshold.Dip);

        internal static Channel ForRotate(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadRotate(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteRotate(t, v);
            },
            SettleThreshold.Degrees);

        internal static Channel ForSkewX(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadSkewX(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteSkewX(t, v);
            },
            SettleThreshold.Degrees);

        internal static Channel ForSkewY(Visual owner, Func<Visual?> target) => new(
            owner,
            () => Transforms.ReadSkewY(target()),
            v =>
            {
                if (target() is { } t)
                    Transforms.WriteSkewY(t, v);
            },
            SettleThreshold.Degrees);

        internal static Channel ForShadowOpacity(Visual owner, Func<Visual?> target) => new(
            owner,
            () => target()?.Effect is DropShadowEffect s ? s.Opacity : 0.0,
            v =>
            {
                if (target() is { } t)
                    EffectSlots.WriteShadowOpacity(t, v);
            });

        internal static Channel ForShadowBlur(Visual owner, Func<Visual?> target) => new(
            owner,
            () => target()?.Effect is DropShadowEffect s ? s.BlurRadius : 0.0,
            v =>
            {
                if (target() is { } t)
                    EffectSlots.WriteShadowBlur(t, v);
            },
            SettleThreshold.Dip);

        internal static Channel ForShadowOffsetX(Visual owner, Func<Visual?> target) => new(
            owner,
            () => target()?.Effect is DropShadowEffect s ? s.OffsetX : 0.0,
            v =>
            {
                if (target() is { } t)
                    EffectSlots.WriteShadowOffsetX(t, v);
            },
            SettleThreshold.Dip);

        internal static Channel ForShadowOffsetY(Visual owner, Func<Visual?> target) => new(
            owner,
            () => target()?.Effect is DropShadowEffect s ? s.OffsetY : 0.0,
            v =>
            {
                if (target() is { } t)
                    EffectSlots.WriteShadowOffsetY(t, v);
            },
            SettleThreshold.Dip);

        /// <summary>Any double styled property of one target — the generic channel: reading
        /// and writing the property IS the whole semantics.</summary>
        internal static Channel ForProperty(Visual owner, StyledProperty<double> property, Func<Visual?> target) => new(
            owner,
            () => target()?.GetValue(property) ?? 1.0,
            v =>
            {
                if (target() is { } t)
                    AnimationLayer.Write(t, property, v);
            });

        /// <summary>One live BlurEffect while blurring, dropped entirely below the 0.5
        /// threshold — no shader pass is paid once the blur has dissipated (the proven rule).</summary>
        internal static Channel ForBlur(Visual owner, Func<Visual?> target) => new(
            owner,
            () => target()?.Effect is BlurEffect b ? b.Radius : 0.0,
            v =>
            {
                if (target() is { } t)
                    EffectSlots.WriteBlur(t, v);
            },
            SettleThreshold.Dip);

        /// <summary>Current on-screen pose of the channel.</summary>
        public double Value => _read();

        /// <summary>Live velocity of the running spring (0 for timed trajectories).</summary>
        public double Velocity => _active is SpringTrajectory s ? s.Velocity : 0.0;

        // ---- description surface -----------------------------------------------------

        /// <summary>A timed trajectory toward a constant target.</summary>
        public TimedTrajectory To(double to)
        {
            Track(to);
            return new TimedTrajectory(this, () => to, lazyTarget: false);
        }

        /// <summary>A timed trajectory whose target resolves at each Start (per-gesture
        /// snapshot semantics) — and whose paired spring is retargetable mid-flight.</summary>
        public TimedTrajectory To(Func<double> to) => new(this, to, lazyTarget: true);

        /// <summary>
        /// Begins a trajectory with an explicit start pose — the plan's From rule: the pose
        /// is PRE-POSED on this channel when at rest (written before the popup shows, no
        /// flash) and ignored while a program is in flight (a reopen mid-collapse resumes
        /// pose + velocity instead). Complete it with the trajectory's To. Reads as
        /// <c>x.From(0.92).To(1.0).Spring(...)</c>.
        /// </summary>
        public TimedTrajectory From(double from) => new TimedTrajectory(this, MissingTo, lazyTarget: true).From(from);

        /// <summary>From resolved per gesture (profile-driven during the port).</summary>
        public TimedTrajectory From(Func<double> from) => new TimedTrajectory(this, MissingTo, lazyTarget: true).From(from);

        /// <summary>An instantaneous pose write (lifecycle: detach/disable).</summary>
        public PoseProgram Pose(double value)
        {
            Track(value);
            return new PoseProgram(this, value);
        }

        // ---- engine: arbitration -----------------------------------------------------

        /// <summary>
        /// Offers an incoming program to the channel: the single place where the
        /// "who owns the channel" rules apply (the press rules).
        /// </summary>
        public void Offer(MotionProgram incoming)
        {
            if (incoming is PoseProgram)
            {
                // A pose write wins over everything: stop, rest, nothing runs afterwards.
                SetActive(null);
                Stop();
                incoming.Start();
                return;
            }

            if (_frozen)
            {
                // Left behind by a stopped choreography: free, but keep the momentum.
                if (incoming is SpringTrajectory resumed && !resumed.HasKick)
                    resumed.SeedVelocity(Velocity);
                StartProgram(incoming);
                return;
            }

            switch (_active)
            {
                case Chain chain:
                    if (incoming is Chain)
                    {
                        StartProgram(incoming); // re-press: purge the parked release, re-arm from the pose
                        return;
                    }

                    if (incoming is SpringTrajectory chainSpring)
                    {
                        chain.Park(chainSpring); // memorized during the guaranteed descent… or immediate beyond it
                        return;
                    }

                    return; // hover never preempts the press chain

                case SpringTrajectory running:
                    if (incoming is Chain)
                    {
                        StartProgram(incoming); // re-click mid-bounce: the press owns the channel again
                        return;
                    }

                    if (incoming is TimedTrajectory && running.CanRetarget)
                    {
                        // Hover mid-bounce: the resting point moves — no snap, pose and
                        // velocity untouched, the incoming hover is consumed by it.
                        running.Retarget();
                        return;
                    }

                    if (ReferenceEquals(incoming, running))
                    {
                        // The same spring re-offered (release then capture-lost): never a
                        // restart — at most its lazy resting point re-resolves.
                        running.Retarget();
                        return;
                    }

                    if (incoming is SpringTrajectory takeover)
                    {
                        // A different spring takes over from the live pose AND velocity
                        // (hover in → out mid-flight): no drop, no kink. An explicitly armed
                        // kick wins over the carried velocity (the Run rule).
                        if (!takeover.HasKick)
                            takeover.SeedVelocity(running.Velocity);
                        StartProgram(incoming);
                        return;
                    }

                    return; // a non-retargetable timed trajectory never preempts a spring

                case TimedTrajectory:
                    StartProgram(incoming); // plain timed trajectories are preemptable
                    return;

                default:
                    StartProgram(incoming); // idle: anything starts
                    return;
            }
        }

        /// <summary>
        /// Forced start used by choreographies (popup): unlike <see cref="Offer"/>, the
        /// incoming program ALWAYS wins — the on-screen pose is captured and, for a spring,
        /// the live velocity of the spring it displaces is carried over (the mid-collapse
        /// reopen: pose + velocity kept, spring constants swapped mid-flight — the old
        /// engine's in-place retarget). The carry is a DEFAULT: an explicitly armed kick
        /// (see <see cref="SpringTrajectory.SeedVelocity"/>) always wins over ambient
        /// state. The choreography owns the single ticker subscription and advances the
        /// program itself: the channel drops its own subscription, if an Offer-started
        /// program had one — never two drivers on one channel.
        /// </summary>
        public void Run(MotionProgram incoming)
        {
            if (incoming is SpringTrajectory spring && !spring.HasKick)
                spring.SeedVelocity(Velocity);
            Stop();
            SetActive(incoming);
            incoming.Start();
        }

        /// <summary>True while <paramref name="program"/> owns this channel — a choreography
        /// stops advancing a member whose channel was taken over.</summary>
        internal bool Owns(MotionProgram program) => ReferenceEquals(_active, program) && !_frozen;

        /// <summary>Marks the member of a stopped choreography as frozen: it keeps its pose
        /// and velocity for a displacing program, but no longer blocks an Offer.</summary>
        internal void Freeze(MotionProgram program)
        {
            if (ReferenceEquals(_active, program))
                _frozen = true;
        }

        /// <summary>
        /// The plan's From rule: writes the pose ONLY when the channel is at rest (the open
        /// block's Froms, written before the popup becomes visible — no one-frame flash); a
        /// channel in flight keeps its live pose (the reopen mid-collapse resumes pose +
        /// velocity instead).
        /// </summary>
        public void PrePoseIfIdle(double value)
        {
            if (_active is not null)
                return;
            Track(value);
            Write(value);
        }

        /// <summary>Releases the channel once its choreography member completed (settle):
        /// idle again, a later From pre-poses it. Never called on preemption — the displacing
        /// spring reads the live velocity through the active program first.</summary>
        public void Release(MotionProgram program)
        {
            if (ReferenceEquals(_active, program))
                SetActive(null);
        }

        /// <summary>Forgets any program on the channel (instant/abnormal close, template
        /// re-apply, detach, disable) — the next From pre-poses it whatever frozen state it
        /// was left in.</summary>
        public void Rest() => SetActive(null);

        private void SetActive(MotionProgram? program)
        {
            _active = program;
            _frozen = false;
        }

        private void StartProgram(MotionProgram program)
        {
            if (TopLevel.GetTopLevel(_owner) is null)
                return;
            
            SetActive(program);
            program.Start();
            EnsureSubscribed();
        }

        /// <summary>
        /// A chain completed and hands the channel over to its memorized release: start it
        /// WITHOUT a synchronous tick — the spring integrates from the next frame, exactly
        /// like the old completion tick starting the release spring.
        /// </summary>
        internal void Handoff(MotionProgram program)
        {
            SetActive(program);
            program.Start();
        }

        /// <summary>Replaces a still-active program (chain releasing the channel to a spring
        /// beyond its guaranteed descent).</summary>
        internal void Replace(MotionProgram oldActive, MotionProgram incoming)
        {
            if (ReferenceEquals(_active, oldActive))
                StartProgram(incoming);
        }

        private void EnsureSubscribed()
        {
            if (_subscription is not null)
                return;
            _subscription = MotionTicker.Subscribe(_owner, OnFrame);
            // Prime the pump (the proven engine's trick): the synchronous advance produces
            // the first property write that schedules the frame the first callback rides on.
            OnFrame(MotionTicker.Now);
        }

        private void OnFrame(TimeSpan now)
        {
            var active = _active;
            if (active is null)
            {
                Stop();
                return;
            }

            bool more = active.Advance(now);
            if (!more && ReferenceEquals(_active, active))
            {
                // Settled (or played out — the exact snap is already written): an idle
                // channel costs zero callbacks. (A Handoff replaced _active — keep rolling.)
                SetActive(null);
                Stop();
            }
        }

        private void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        // ---- engine: plumbing ----------------------------------------------------------

        public void Write(double value) => _write(value);

        /// <summary>Clamps a program start pose into the channel window (defensive, mirrors
        /// the old <c>Math.Clamp(pose, DeepFloor, HoverScale)</c> on press/spring starts).</summary>
        public double ClampPose(double pose)
        {
            // The window always contains the pose the channel first started from: grown
            // from targets alone, a lone target collapses it onto itself and the very first
            // program would start AT its target (no animation at all).
            if (!_startPoseTracked)
            {
                _startPoseTracked = true;
                Track(pose);
            }
            return _seenMin <= _seenMax ? Math.Clamp(pose, _seenMin, _seenMax) : pose;
        }

        /// <summary>Feeds the defensive pose-clamp window with a resolved target/from.</summary>
        public void Track(double value)
        {
            if (value < _seenMin) _seenMin = value;
            if (value > _seenMax) _seenMax = value;
        }
    }

    /// <summary>
    /// The shared render-transform block of one target: ONE <c>TransformGroup</c> holding the
    /// engine's children (created lazily per channel kind), all transform channels (scale,
    /// translate, rotate, skew) writing through the same group so they COMPOSE instead of
    /// fighting over RenderTransform. Keyed on the rendered visual (two surfaces over the same
    /// target share the block; a re-resolved template part gets a fresh one for free). The group
    /// is held at Animation priority (PLAN D32) and let go once every engine child is back at
    /// identity — the base RenderTransform (style, template, local value) is the element's
    /// transform again. While held, that base is composed as the innermost child, so the engine
    /// starts and stops without a jump; it is re-read at every write (base changes under an
    /// animation raise no public notification). Fixed composition order of the engine children
    /// — translate · skew · rotate · scale around the target's RenderTransformOrigin (the proven
    /// dialog order when rotate/skew are absent; the skew shears in the parent frame, after
    /// rotation in point order). Writing identity to a target the engine never moved attaches
    /// nothing.
    /// </summary>
    internal static class Transforms
    {
        private static readonly ConditionalWeakTable<Visual, Block> Blocks = new();

        private sealed class Block
        {
            public Block(Visual target) => Held = new HeldValue<ITransform?>(target, Visual.RenderTransformProperty);

            public readonly HeldValue<ITransform?> Held;
            public TransformGroup? Group;
            public ITransform? BaseSource; // the base RenderTransform composed into Group
            public int Version;            // bumped when a child is created
            public int GroupVersion = -1;  // the Version Group was built at
            public TranslateTransform? Translate;
            public SkewTransform? Skew;
            public RotateTransform? Rotate;
            public ScaleTransform? Scale;

            public bool IsIdentity =>
                (Translate is null || (Translate.X == 0.0 && Translate.Y == 0.0))
                && (Skew is null || (Skew.AngleX == 0.0 && Skew.AngleY == 0.0))
                && (Rotate is null || Rotate.Angle == 0.0)
                && (Scale is null || (Scale.ScaleX == 1.0 && Scale.ScaleY == 1.0));
        }

        // ---- reads (never attach; identity until the first write) -------------------------

        internal static double ReadScaleX(Visual? t) =>
            t is not null && Blocks.TryGetValue(t, out var b) && b.Scale is { } s ? s.ScaleX : 1.0;

        internal static double ReadScaleY(Visual? t) =>
            t is not null && Blocks.TryGetValue(t, out var b) && b.Scale is { } s ? s.ScaleY : 1.0;

        internal static double ReadTranslateX(Visual? t) =>
            t is not null && Blocks.TryGetValue(t, out var b) && b.Translate is { } tr ? tr.X : 0.0;

        internal static double ReadTranslateY(Visual? t) =>
            t is not null && Blocks.TryGetValue(t, out var b) && b.Translate is { } tr ? tr.Y : 0.0;

        internal static double ReadRotate(Visual? t) =>
            t is not null && Blocks.TryGetValue(t, out var b) && b.Rotate is { } r ? r.Angle : 0.0;

        internal static double ReadSkewX(Visual? t) =>
            t is not null && Blocks.TryGetValue(t, out var b) && b.Skew is { } sk ? sk.AngleX : 0.0;

        internal static double ReadSkewY(Visual? t) =>
            t is not null && Blocks.TryGetValue(t, out var b) && b.Skew is { } sk ? sk.AngleY : 0.0;

        // ---- writes (mutate the children in place, then hold or let go of the group) ----------

        internal static void WriteScale(Visual t, double v)
        {
            if (ScaleOf(t, v) is not { } b)
                return;
            b.Scale!.ScaleX = v;
            b.Scale.ScaleY = v;
            Commit(t, b);
        }

        internal static void WriteScaleX(Visual t, double v)
        {
            if (ScaleOf(t, v) is not { } b)
                return;
            b.Scale!.ScaleX = v;
            Commit(t, b);
        }

        internal static void WriteScaleY(Visual t, double v)
        {
            if (ScaleOf(t, v) is not { } b)
                return;
            b.Scale!.ScaleY = v;
            Commit(t, b);
        }

        internal static void WriteTranslateX(Visual t, double v)
        {
            if (TranslateOf(t, v) is not { } b)
                return;
            b.Translate!.X = v;
            Commit(t, b);
        }

        internal static void WriteTranslateY(Visual t, double v)
        {
            if (TranslateOf(t, v) is not { } b)
                return;
            b.Translate!.Y = v;
            Commit(t, b);
        }

        internal static void WriteRotate(Visual t, double v)
        {
            if (RotateOf(t, v) is not { } b)
                return;
            b.Rotate!.Angle = v;
            Commit(t, b);
        }

        internal static void WriteSkewX(Visual t, double v)
        {
            if (SkewOf(t, v) is not { } b)
                return;
            b.Skew!.AngleX = v;
            Commit(t, b);
        }

        internal static void WriteSkewY(Visual t, double v)
        {
            if (SkewOf(t, v) is not { } b)
                return;
            b.Skew!.AngleY = v;
            Commit(t, b);
        }

        // ---- the block --------------------------------------------------------------------

        private static Block? ScaleOf(Visual t, double v)
        {
            var b = BlockFor(t, v == 1.0, static b => b.Scale is null);
            if (b is not null && b.Scale is null)
            {
                b.Scale = new ScaleTransform(1, 1);
                b.Version++;
            }
            return b;
        }

        private static Block? TranslateOf(Visual t, double v)
        {
            var b = BlockFor(t, v == 0.0, static b => b.Translate is null);
            if (b is not null && b.Translate is null)
            {
                b.Translate = new TranslateTransform();
                b.Version++;
            }
            return b;
        }

        private static Block? RotateOf(Visual t, double v)
        {
            var b = BlockFor(t, v == 0.0, static b => b.Rotate is null);
            if (b is not null && b.Rotate is null)
            {
                b.Rotate = new RotateTransform();
                b.Version++;
            }
            return b;
        }

        private static Block? SkewOf(Visual t, double v)
        {
            var b = BlockFor(t, v == 0.0, static b => b.Skew is null);
            if (b is not null && b.Skew is null)
            {
                b.Skew = new SkewTransform();
                b.Version++;
            }
            return b;
        }

        /// <summary>The target's block — null when an identity write would only create
        /// state (no block yet, or no child of that kind): nothing to move, nothing attached.</summary>
        private static Block? BlockFor(Visual t, bool identity, Func<Block, bool> childMissing)
        {
            if (Blocks.TryGetValue(t, out var b))
                return identity && childMissing(b) ? null : b;
            if (identity)
                return null;
            b = new Block(t);
            Blocks.Add(t, b);
            return b;
        }

        private static void Commit(Visual t, Block b)
        {
            if (b.IsIdentity)
            {
                // At rest: the base RenderTransform is the element's transform again.
                b.Held.Release();
                return;
            }
            var baseTransform = AnimationLayer.BaseValue(t, Visual.RenderTransformProperty);
            if (b.Held.IsHeld && b.GroupVersion == b.Version && ReferenceEquals(b.BaseSource, baseTransform))
                return; // the children were mutated in place — the group re-renders by itself
            Rebuild(b, baseTransform);
        }

        private static void Rebuild(Block b, ITransform? baseTransform)
        {
            // A fresh group whenever its child set or the base changes (structure is rare:
            // animation starts, base changes) — the previous one releases its children first.
            b.Group?.Children.Clear();
            var group = new TransformGroup();
            // The base is the innermost child: the engine moves the element as styled.
            switch (baseTransform)
            {
                case null:
                    break;
                case Transform transform:
                    group.Children.Add(transform);
                    break;
                default:
                    group.Children.Add(new MatrixTransform(baseTransform.Value));
                    break;
            }
            if (b.Translate is { } translate)
                group.Children.Add(translate);
            if (b.Skew is { } skew)
                group.Children.Add(skew);
            if (b.Rotate is { } rotate)
                group.Children.Add(rotate);
            if (b.Scale is { } scale)
                group.Children.Add(scale);
            b.Group = group;
            b.GroupVersion = b.Version;
            b.BaseSource = baseTransform;
            b.Held.Hold(group);
        }
    }

    /// <summary>
    /// The single <see cref="Visual.Effect"/> slot as the engine sees it (PLAN D32): the engine
    /// holds ONE effect of its own at Animation priority — a blur or a drop shadow, the last
    /// writer owning the slot — and lets go when it adds nothing to the base effect (style,
    /// template, local value), which then shows again. The engine only ever mutates instances it
    /// created: a base effect — typically a style setter value, ONE instance shared by every
    /// control the style matches — is copied on the first write (pose continuity), never
    /// mutated (bug 7). Presence thresholds keep the shader pass off at rest: a blur below 0.5
    /// DIP and a shadow below 0.02 opacity count as absent.
    /// </summary>
    internal static class EffectSlots
    {
        private const double BlurPresentFrom = 0.5;
        private const double ShadowPresentFrom = 0.02;

        private static readonly ConditionalWeakTable<Visual, Slot> Slots = new();

        private enum Owner { None, Blur, Shadow }

        private sealed class Slot
        {
            public Slot(Visual target) => Held = new HeldValue<IEffect?>(target, Visual.EffectProperty);

            public readonly HeldValue<IEffect?> Held;
            public Owner Owner;               // which channel kind holds the slot (None: let go)
            public IEffect? Shown;            // what the engine holds (null: "no effect" as a pose)
            public BlurEffect? Blur;          // the engine's blur, kept while absent
            public DropShadowEffect? Shadow;  // the engine's shadow parameters, kept while hidden
        }

        internal static void WriteBlur(Visual t, double radius)
        {
            var slot = Slots.TryGetValue(t, out var s) ? s : null;
            var baseEffect = AnimationLayer.BaseValue(t, Visual.EffectProperty);
            double baseRadius = baseEffect is BlurEffect baseBlur ? baseBlur.Radius : 0.0;

            if (radius < BlurPresentFrom)
            {
                if (slot?.Owner == Owner.Shadow)
                    return; // a shadow owns the slot: an absent blur leaves it alone
                if (baseRadius < BlurPresentFrom)
                {
                    if (slot is not null)
                        Release(slot);             // nothing added: the base effect shows again
                    return;
                }
                Hold(slot ?? Create(t), null, Owner.Blur); // a styled blur dissolved to crisp: a pose
                return;
            }

            slot ??= Create(t);
            if (baseEffect is BlurEffect sameBlur && sameBlur.Radius == radius)
            {
                Release(slot);
                return;
            }
            slot.Blur ??= new BlurEffect();
            slot.Blur.Radius = radius;
            Hold(slot, slot.Blur, Owner.Blur);
        }

        internal static void WriteShadowOpacity(Visual t, double v) => WriteShadow(t, v, static (s, x) => s.Opacity = x);

        internal static void WriteShadowBlur(Visual t, double v) => WriteShadow(t, v, static (s, x) => s.BlurRadius = x);

        internal static void WriteShadowOffsetX(Visual t, double v) => WriteShadow(t, v, static (s, x) => s.OffsetX = x);

        internal static void WriteShadowOffsetY(Visual t, double v) => WriteShadow(t, v, static (s, x) => s.OffsetY = x);

        private static void WriteShadow(Visual t, double value, Action<DropShadowEffect, double> apply)
        {
            var slot = Slots.TryGetValue(t, out var s) ? s : Create(t);
            var baseShadow = AnimationLayer.BaseValue(t, Visual.EffectProperty) as DropShadowEffect;

            // The parameters continue from the base shadow while the engine holds no shadow of
            // its own; while it does (shown or hidden), from its own.
            if (slot.Owner != Owner.Shadow)
                slot.Shadow = baseShadow is not null ? Copy(baseShadow) : slot.Shadow ?? new DropShadowEffect();
            var shadow = slot.Shadow!;
            apply(shadow, value);

            if (shadow.Opacity < ShadowPresentFrom)
            {
                if (slot.Owner == Owner.Blur)
                    return; // a blur owns the slot: a hidden shadow leaves it alone
                if (baseShadow is not null && baseShadow.Opacity >= ShadowPresentFrom)
                    Hold(slot, null, Owner.Shadow); // a styled shadow faded out: a pose of its own
                else
                    Release(slot);                  // nothing added: the base effect shows again
                return;
            }

            if (baseShadow is not null && SameShadow(shadow, baseShadow))
            {
                Release(slot);
                return;
            }
            Hold(slot, shadow, Owner.Shadow);
        }

        private static Slot Create(Visual t)
        {
            var slot = new Slot(t);
            Slots.Add(t, slot);
            return slot;
        }

        private static void Hold(Slot slot, IEffect? effect, Owner owner)
        {
            slot.Owner = owner;
            if (slot.Held.IsHeld && ReferenceEquals(slot.Shown, effect))
                return; // mutated in place — the effect re-renders by itself
            slot.Shown = effect;
            slot.Held.Hold(effect);
        }

        private static void Release(Slot slot)
        {
            slot.Owner = Owner.None;
            slot.Shown = null;
            slot.Held.Release();
        }

        private static DropShadowEffect Copy(DropShadowEffect s) => new()
        {
            BlurRadius = s.BlurRadius,
            Color = s.Color,
            Opacity = s.Opacity,
            OffsetX = s.OffsetX,
            OffsetY = s.OffsetY,
        };

        private static bool SameShadow(DropShadowEffect a, DropShadowEffect b) =>
            a.Opacity == b.Opacity && a.BlurRadius == b.BlurRadius && a.OffsetX == b.OffsetX
            && a.OffsetY == b.OffsetY && a.Color == b.Color;
    }
}
