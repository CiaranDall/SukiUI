# SukiUI.Motion — migration notes

Breaking changes of the public API, newest first. The package has only been published as
nightly builds so far (`7.0.2-nightly…`); there are no `[Obsolete]` shims.

## Unreleased

### Renames (no behavior change)

| Before | After | Why |
|---|---|---|
| `Motion` | `Animate` | The class had the namespace's name: every consumer inside a `SukiUI.*` namespace needed `using Motion = SukiUI.Motion.Motion;` |
| `Program` | `MotionProgram` | Collided with every application's `Program` entry class |
| `SukiMotion<TSelf>` | `MotionBehavior<TSelf>` | The library no longer carries the SukiUI prefix |
| `SukiTicker` | `MotionTicker` | Same |
| `SukiMotionStats` | `MotionStats` | Same |
| `SukiSpringEaseOut` | `SpringEaseOut` | Same |
| `SukiEaseElasticIn` | `DampedEaseIn` | Same; `ElasticEaseIn` is already an Avalonia easing |

```csharp
// Before
using Motion = SukiUI.Motion.Motion;
var scale = Motion.For(button).Scale;

// After
var scale = Animate.For(button).Scale;
```

### Behavior: engine writes no longer beat styles forever

Engine writes used to be local values that were never cleared: once a property had been
animated, styles and style triggers (`:disabled`, classes) applied later were silently ignored.
Now, like Avalonia's own transitions and animations:

- every write lands at `BindingPriority.Animation`, through one binding per element and property;
- once the pose is back at the base value (style, template, local value, default) the engine
  lets go — styles and local values work again; a local value set before the animation is kept;
- a pose held away from the base (hover scale, a switched-on knob) is an animation in progress
  and wins over styles and local values until it returns to the base;
- a styled `RenderTransform` is composed under the engine's transforms instead of replaced, and is
  the element's transform again at rest; a dissipated blur gives the `Effect` slot back to the
  styled effect;
- the popup item cascade fades each item in to its own (styled) opacity instead of 1.

### Removed

- `SukiTicker.Timestamp`, `SukiTicker.ElapsedSeconds(long)`, `SukiTicker.ElapsedMilliseconds(long)`:
  unused; read `MotionTicker.Now` (or `MotionStats.Now`) instead.
