using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Data;

namespace SukiUI.Motion
{
    /// <summary>
    /// The engine's value layer (PLAN D32): every engine write lands at
    /// <see cref="BindingPriority.Animation"/> — like Avalonia's own transitions and animations —
    /// through ONE binding per (element, property), and is let go as soon as the written pose is
    /// back at the base value (the value without animations: style, template, local, default).
    /// Styles and local values therefore keep working once a motion is at rest, a user's own
    /// local value is never touched, and a pose held away from the base (hover, a switched-on
    /// knob) is an animation in progress that wins over them, exactly like a held Avalonia
    /// animation. Writing the base value while nothing is held is a no-op.
    /// </summary>
    internal static class AnimationLayer
    {
        private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, object>> Slots = new();

        /// <summary>Writes an engine pose: held while it differs from the base value, let go
        /// once it is back at it.</summary>
        internal static void Write(AvaloniaObject target, StyledProperty<double> property, double value)
        {
            var held = Find<double>(target, property);
            if (value.Equals(BaseValue(target, property)))
            {
                held?.Release();
                return;
            }
            (held ?? Create(target, property)).Hold(value);
        }

        /// <summary>Lets go of the engine's value: the base value shows again.</summary>
        internal static void Release<T>(AvaloniaObject target, StyledProperty<T> property) =>
            Find<T>(target, property)?.Release();

        /// <summary>The property's value without animations — what shows once the engine lets go.</summary>
        internal static T BaseValue<T>(AvaloniaObject target, StyledProperty<T> property) =>
            target.GetBaseValue(property) is { HasValue: true } b ? b.Value : property.GetDefaultValue(target);

        /// <summary>The one held value of (target, property) — created on first use.</summary>
        internal static HeldValue<T> Of<T>(AvaloniaObject target, StyledProperty<T> property) =>
            Find<T>(target, property) ?? Create(target, property);

        private static HeldValue<T>? Find<T>(AvaloniaObject target, StyledProperty<T> property) =>
            Slots.TryGetValue(target, out var slots) && slots.TryGetValue(property, out var held)
                ? (HeldValue<T>)held
                : null;

        private static HeldValue<T> Create<T>(AvaloniaObject target, StyledProperty<T> property)
        {
            var held = new HeldValue<T>(target, property);
            Slots.GetOrCreateValue(target)[property] = held;
            return held;
        }
    }

    /// <summary>
    /// One engine value of one styled property at Animation priority: the first
    /// <see cref="Hold"/> binds, the next ones push through the same binding (no value-store
    /// frame per write), <see cref="Release"/> disposes the binding.
    /// </summary>
    internal sealed class HeldValue<T> : IObservable<T>
    {
        private readonly AvaloniaObject _target;
        private readonly StyledProperty<T> _property;
        private T _value = default!;
        private IObserver<T>? _observer;
        private IDisposable? _binding;

        internal HeldValue(AvaloniaObject target, StyledProperty<T> property)
        {
            _target = target;
            _property = property;
        }

        internal bool IsHeld => _binding is not null;

        internal void Hold(T value)
        {
            _value = value;
            if (_binding is null)
                _binding = _target.Bind(_property, this, BindingPriority.Animation);
            else
                _observer?.OnNext(value);
        }

        internal void Release()
        {
            var binding = _binding;
            if (binding is null)
                return;
            _binding = null;
            _observer = null;
            binding.Dispose();
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observer = observer;
            observer.OnNext(_value);
            return Unwire.On(() =>
            {
                if (ReferenceEquals(_observer, observer))
                    _observer = null;
            });
        }
    }
}
