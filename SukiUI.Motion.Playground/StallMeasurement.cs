using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SukiUI.Motion.Composition;

namespace SukiUI.Motion.Playground;

/// <summary>
/// <c>--measure-stall &lt;file&gt;</c> — where the composition box REALLY is after the UI thread
/// sleeps right after a start. The compositor cannot be read back, but hit testing runs on the
/// server's readback (ENGINEERING_NOTES §7.7): scanning a row with hit tests finds the drawn left
/// edge. Two variants:
///  A — SpringTo then Thread.Sleep in the same handler (the scene P1 button);
///  B — SpringTo, await Compositor.RequestCommitAsync(), then Thread.Sleep.
/// After the sleep, samples drawn position vs the channel's model Value every 50 ms.
/// </summary>
internal static class StallMeasurement
{
    private const double Left0 = 20, Top0 = 40, Size = 40, Target = 300, StallMs = 1000;
    private static readonly Spring Spring = new(Omega: 10, Decay: 6);

    public static void Run(Window window, string path)
    {
        var canvas = new Canvas { Background = Brushes.White };
        var box = new Border { Width = Size, Height = Size, Background = Brushes.SteelBlue };
        Canvas.SetLeft(box, Left0);
        Canvas.SetTop(box, Top0);
        canvas.Children.Add(box);
        window.Content = canvas;
        window.Width = 420;
        window.Height = 140;

        var report = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        var x = CompositionMotion.For(box).TranslateX;

        double Drawn()
        {
            // Leftmost x in the box's row whose hit test reaches the box (server readback).
            for (int px = 0; px < 420; px++)
                if (window.GetVisualsAt(new Point(px, Top0 + Size / 2)).Contains(box))
                    return px - Left0;
            return double.NaN;
        }

        async Task Variant(string name, bool awaitCommit)
        {
            x.Pose(0);
            await Task.Delay(500);
            report.AppendLine(ci, $"--- {name}");
            x.SpringTo(Target, Spring);
            if (awaitCommit && ElementComposition.GetElementVisual(box) is { } v)
                await v.Compositor.RequestCommitAsync();
            Thread.Sleep((int)StallMs); // the UI thread is busy (layout, content creation, sync I/O...)
            for (int i = 0; i < 30; i++)
            {
                report.AppendLine(ci, $"t+{i * 50,4} ms after the stall   drawn {Drawn(),7:0.0}   model {x.Value,7:0.0}");
                await Task.Delay(50);
            }
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(1000);
            await Variant("A: SpringTo, then sleep in the same handler", awaitCommit: false);
            await Variant("B: SpringTo, await RequestCommitAsync(), then sleep", awaitCommit: true);
            File.WriteAllText(path, report.ToString());
            window.Close();
        });
    }
}
