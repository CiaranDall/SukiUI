using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SukiUI.Motion.RenderTests;

/// <summary>Renders a frame and reads back pixels — the only window onto server-side state.</summary>
public static class Pixels
{
    /// <summary>Lets the dispatcher run, renders one frame and returns it.</summary>
    public static WriteableBitmap Capture(TopLevel topLevel)
    {
        Dispatcher.UIThread.RunJobs();
        return topLevel.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No frame was rendered.");
    }

    /// <summary>Gray level 0..255 (mean of R, G, B) of one pixel.</summary>
    public static unsafe double Gray(WriteableBitmap bitmap, int x, int y)
    {
        using var fb = bitmap.Lock();
        byte* p = (byte*)fb.Address + y * fb.RowBytes + x * 4;
        return (p[0] + p[1] + p[2]) / 3.0; // RGBA or BGRA: the channel order does not matter for gray
    }

    /// <summary>Gray levels of one pixel row (one lock for the whole row).</summary>
    public static unsafe double[] Row(WriteableBitmap bitmap, int y)
    {
        using var fb = bitmap.Lock();
        var row = new double[fb.Size.Width];
        byte* p = (byte*)fb.Address + y * fb.RowBytes;
        for (int x = 0; x < row.Length; x++, p += 4)
            row[x] = (p[0] + p[1] + p[2]) / 3.0;
        return row;
    }

    /// <summary>Leftmost x of a dark pixel (gray &lt; 128) in row <paramref name="y"/>; -1 if none.</summary>
    public static int LeftEdge(WriteableBitmap bitmap, int y) => Array.FindIndex(Row(bitmap, y), g => g < 128);

    /// <summary>Number of dark pixels in row <paramref name="y"/>.</summary>
    public static int DarkWidth(WriteableBitmap bitmap, int y) => Row(bitmap, y).Count(g => g < 128);

    /// <summary>Real time passes for the compositor (its clock is a Stopwatch), the UI keeps pumping.</summary>
    public static void Wait(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            Pump();
            Thread.Sleep(10);
        }
        Pump();
    }

    /// <summary>
    /// Runs the dispatcher INCLUDING due DispatcherTimers. In headless, <c>RunJobs</c> promotes due
    /// timers only after executing some other job (<c>Dispatcher.ExecuteJob → PromoteTimers</c>); on
    /// an empty queue it returns at once and timers never fire (ENGINEERING_NOTES §2.4). A no-op job
    /// forces the promotion.
    /// </summary>
    public static void Pump()
    {
        Dispatcher.UIThread.Post(static () => { }, DispatcherPriority.Send);
        Dispatcher.UIThread.RunJobs();
    }
}
