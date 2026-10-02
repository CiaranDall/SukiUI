using System.Runtime.CompilerServices;
using Avalonia;

namespace SukiUI.Motion
{
    /// <summary>
    /// The single entry point and access vocabulary of the motion layer: everything
    /// animable hangs off a <see cref="Surface"/> obtained from <see cref="For"/> — the
    /// element itself, one of its template parts (<see cref="Surface.Part"/>), or its
    /// template popup (<see cref="Surface.Popup"/>, a surface root plus the IsOpen
    /// lifecycle). Surfaces differ only by how their target resolves, never by the
    /// channels they expose.
    /// </summary>
    public static class Animate
    {
        private static readonly ConditionalWeakTable<Visual, Surface> Surfaces = new();

        /// <summary>The single entry point: one surface per element (a single arbitration
        /// state per animated property).</summary>
        public static Surface For(Visual visual) =>
            Surfaces.GetValue(visual, v => new Surface(v, () => v));
    }
}
