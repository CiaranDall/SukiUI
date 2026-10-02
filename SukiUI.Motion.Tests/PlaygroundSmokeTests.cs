using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SukiUI.Motion.Playground;

namespace SukiUI.Motion.Tests;

/// <summary>The visual debug app must never be the thing that is broken.</summary>
public class PlaygroundSmokeTests
{
    [AvaloniaFact]
    public void Every_scene_builds_and_every_button_runs()
    {
        var window = new MainWindow();
        using var h = new MotionHarness();
        window.Show();
        MotionHarness.Flush();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        int clicked = 0;

        for (int i = 0; i < list.ItemCount; i++)
        {
            list.SelectedIndex = i;
            h.Frames(2); // layout + template application of the freshly built page
            MotionHarness.Flush();
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b is not ToggleButton && b.FindAncestorOfType<ListBox>() is null).ToList();
            clicked += buttons.Count; // some scenes are pointer-only (hover)
            foreach (var button in buttons)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                h.Frames(5);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                h.Frames(30);
            }
        }
        Assert.True(clicked > 10, $"only {clicked} buttons found");
        window.Close();
    }
}
