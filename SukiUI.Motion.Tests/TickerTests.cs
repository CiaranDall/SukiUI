using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace SukiUI.Motion.Tests;

public class TickerTests
{
    // P10: the ticker keeps one loop per TopLevel, but RAF is global (ENGINEERING_NOTES §1.1):
    // N windows = N callbacks inside the SAME render pass, not N frame loops. Pinned here: each
    // animating window costs exactly one dispatch per frame, and both run on the same frame time.
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
            var sa = Motion.For(a).Scale;
            var sb = Motion.For(b).Scale;
            sa.Offer(sa.To(2.0).Over(TimeSpan.FromMilliseconds(320)));
            sb.Offer(sb.To(2.0).Over(TimeSpan.FromMilliseconds(320)));
            h.Frame();

            long before = SukiTicker.DispatchCount;
            h.Frames(10);

            Assert.Equal(20, SukiTicker.DispatchCount - before);
            Assert.Equal(sa.Value, sb.Value); // same frame time in both windows
        }
        finally
        {
            second.Close();
        }
    }
}
