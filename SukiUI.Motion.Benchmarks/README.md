# SukiUI.Motion.Benchmarks

Micro-benchmarks for phase 3 (performance) of SukiUI.Motion: `SukiUI.Motion/PLAN.md` §4, rule R3
("measure before and after"). Results and conclusions: `SukiUI.Motion/ENGINEERING_NOTES.md` §8.

Avalonia runs headless on the benchmark thread (`UiSession`). Headless drawing by default: the
numbers are UI-thread CPU. `BlurBenchmarks` renders for real with Skia's CPU rasterizer
(the software-rendering worst case).

## Run

Always Release. One class:

```bash
dotnet run -c Release --project SukiUI.Motion.Benchmarks -- --filter "*EasingBenchmarks*"
```

Everything (takes a while):

```bash
dotnet run -c Release --project SukiUI.Motion.Benchmarks -- --filter "*"
```

Reports land in `BenchmarkDotNet.Artifacts/` (or the folder passed with `--artifacts`).

## What is measured

| Class | Backlog item | Question |
|---|---|---|
| `EasingBenchmarks` | P9 | Cost of `SukiSpringEaseOut.Ease` (renormalizes on every call) |
| `PropertyChangedBenchmarks` | P6 | Cost of the catch-all `PropertyChanged` triggers on unrelated changes |
| `OutsidePressBenchmarks` | P7 | Cost of N window-level outside-press handlers per press |
| `LayoutChannelBenchmarks` | P5 | A layout-property frame (toast `MaxHeight`) vs a render-transform frame |
| `BlurBenchmarks` | P4 | Blur layers per frame on the CPU rasterizer |
| `ChannelWriteBenchmarks` | baseline | One channel write per kind: time and allocations |

## Caveats

- Raising pointer events on a headless window retains ~0.8 KB per press inside Avalonia, so
  `OutsidePressBenchmarks` uses a short fixed job (see the class comment).
- Absolute numbers depend on the machine; compare cases within one run.
