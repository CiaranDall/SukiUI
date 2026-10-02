using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace SukiUI.Motion.Tests;

/// <summary>Validates the test harness itself before any bug test relies on it.</summary>
public class HarnessTests
{
    // A subscriber must write a visual property to keep RAF on the compositor cadence:
    // Avalonia 12 re-arms a RAF requested from inside a frame either on composition-batch
    // completion (something changed) or on a real-time 16 ms DispatcherTimer (nothing
    // changed) — the latter does not follow the virtual clock. Every engine channel writes.
    [AvaloniaFact]
    public void Ticker_runs_one_callback_per_pumped_frame_on_virtual_time()
    {
        var border = new Border();
        using var h = new MotionHarness(border);
        var seen = new List<TimeSpan>();

        using var token = SukiTicker.Subscribe(border, now =>
        {
            seen.Add(now);
            border.Opacity = 1.0 - seen.Count * 0.01;
        });
        h.Frames(3);

        Assert.Equal(3, seen.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(16), seen[1] - seen[0]);
    }

    [AvaloniaFact]
    public void Timed_trajectory_reaches_its_target_on_virtual_time()
    {
        var border = new Border();
        using var h = new MotionHarness(border);
        var scale = Motion.For(border).Scale;

        scale.Offer(scale.To(2.0).Over(TimeSpan.FromMilliseconds(160)));
        h.Frames(5);
        Assert.InRange(scale.Value, 1.3, 1.7); // halfway, linear easing

        h.Frames(10);
        Assert.Equal(2.0, scale.Value);
    }
}
