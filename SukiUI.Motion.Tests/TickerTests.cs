using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace SukiUI.Motion.Tests;

public class TickerTests
{
    // P10: the ticker keeps one loop per TopLevel, but RAF is global (ENGINEERING_NOTES §1.1):
    // N windows = N callbacks inside the SAME render pass, not N frame loops. Pinned here: both
    // windows are dispatched in exactly the same passes, once per frame each, on the same frame
    // time. Counted per frame time, not in total: the headless real-time render timer may add
    // a pass at the same virtual time (NOTES §2.5) - it dispatches both windows alike.
    [AvaloniaFact]
    public void Each_animating_window_gets_one_dispatch_per_frame_in_the_same_pass()
    {
        var a = new Border();
        var b = new Border();
        using var h = new MotionHarness(a);
        var second = new Window { Width = 200, Height = 200, Content = b };
        second.Show();
        try
        {
            var sa = Animate.For(a).Scale;
            var sb = Animate.For(b).Scale;
            sa.Offer(sa.To(2.0).Over(TimeSpan.FromMilliseconds(320)));
            sb.Offer(sb.To(2.0).Over(TimeSpan.FromMilliseconds(320)));
            h.Frame();

            var timesA = new List<TimeSpan>();
            var timesB = new List<TimeSpan>();
            using var probeA = MotionTicker.Subscribe(a, now =>
            {
                timesA.Add(now);
                // A frame that takes 40 ms of real time lets the real-time render timer add a
                // pass (NOTES §2.5): the assertions below must hold through it.
                if (timesA.Count == 3)
                    Thread.Sleep(40);
            });
            using var probeB = MotionTicker.Subscribe(b, timesB.Add);
            h.Frames(10);

            Assert.Equal(timesA, timesB);                // the same passes in both windows
            Assert.Equal(10, timesA.Distinct().Count()); // one frame time per harness frame
            Assert.Equal(sa.Value, sb.Value);            // same frame time in both windows
        }
        finally
        {
            second.Close();
        }
    }
}
