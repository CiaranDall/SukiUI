using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(SukiUI.Motion.Benchmarks.UiSession).Assembly).Run(args);
