using Avalonia.Animation.Easings;
using BenchmarkDotNet.Attributes;

namespace SukiUI.Motion.Benchmarks;

/// <summary>
/// P9: <see cref="SukiSpringEaseOut.Ease"/> renormalizes against the spring's value at t = 1 on
/// every call. One Avalonia transition calls Ease once per frame per animated property.
/// Baseline: Avalonia's <see cref="CubicEaseOut"/>.
/// </summary>
[MemoryDiagnoser]
public class EasingBenchmarks
{
    private const int Samples = 1000;
    private readonly SukiSpringEaseOut _spring = new();
    private readonly CubicEaseOut _cubic = new();

    [Benchmark(Baseline = true, OperationsPerInvoke = Samples)]
    public double CubicEaseOut() => Sweep(_cubic);

    [Benchmark(OperationsPerInvoke = Samples)]
    public double SukiSpringEaseOut() => Sweep(_spring);

    private static double Sweep(Easing easing)
    {
        double sum = 0;
        for (int i = 1; i < Samples; i++)
            sum += easing.Ease(i / (double)Samples);
        return sum;
    }
}
