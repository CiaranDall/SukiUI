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

### Removed

- `SukiTicker.Timestamp`, `SukiTicker.ElapsedSeconds(long)`, `SukiTicker.ElapsedMilliseconds(long)`:
  unused; read `MotionTicker.Now` (or `MotionStats.Now`) instead.
