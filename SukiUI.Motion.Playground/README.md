# SukiUI.Motion.Playground

A small visual debug app for the SukiUI.Motion bug fixes. It is **not** part of the pull request:
it lives on its own branch so the fixes can be checked by eye. It depends **only** on
`SukiUI.Motion` (not on SukiUI) and uses only its public API.

Each scene on the left is one fix from the pull request ("Fix N" matches the numbering in the
PR description): what to do, what happened before the fix, and what is expected now. The bottom
bar shows live ticker stats (`dispatch/s`): an idle engine must read **IDLE (0 engine frames)**.

## Run

```bash
dotnet run --project SukiUI.Motion.Playground
```

Or run `SukiUI.Motion.Playground.exe` from the zip attached to the pull request (win-x64,
self-contained, no .NET install needed).

## Compare before and after

The playground also builds against the engine **before** the fixes (upstream `main`), so you
can temporarily swap the engine sources, look, and swap them back:

```bash
git restore --source=c2bf6bcae --worktree -- SukiUI.Motion
dotnet run --project SukiUI.Motion.Playground
git restore --worktree -- SukiUI.Motion
```

The last command restores the fixed engine from the index (the working tree is clean again).
The test project does not build in "before" mode: the old engine has no test clock seam.

## Scenes

| Scene | Fix | What to look at |
|---|---|---|
| External RenderTransform replacement | 1 | shift/rotate after `RenderTransform` is replaced; the `Value` line vs the real matrix |
| Restarting a running choreography | 2 | the settle counter and `dispatch/s` once idle |
| A spring during a spring | 3 | a fast hover in/out; the scale chart |
| A lone spring teleports | 4 | a spring / Chain on a fresh channel; the chart |
| A channel after a stopped choreography | 7 | the return through `Offer` after a preempted choreography |
| Stiff springs and springs without damping | 9 | the value chart (NaN / 1e128 are printed as numbers), the validation exception |
| SukiSpringEaseOut for ζ > 1 | 10 | engine curves (solid) vs the exact solution (dashed) |
| A styled shadow is shared by every control | 11 | three cards sharing one shadow from a style setter |

Fixes 5, 6, 8 and 12 (detached hosts, parked popup choreographies, chain easing snapshot,
`Mover` lifecycle) have no visual scene: they are covered by the unit tests only.

`PlaygroundSmokeTests` in `SukiUI.Motion.Tests` builds every scene and clicks every button
headless, so the playground cannot break unnoticed.
