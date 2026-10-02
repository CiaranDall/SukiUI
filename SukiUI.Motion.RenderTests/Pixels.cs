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

    /// <summary>Real time passes for the compositor (its clock is a Stopwatch), the UI keeps pumping.</summary>
    public static void Wait(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
