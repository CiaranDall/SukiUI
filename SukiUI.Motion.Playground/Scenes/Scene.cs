using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace SukiUI.Motion.Playground.Scenes;

/// <summary>
/// One reproducible scenario. The page explains what to do, what the bug looked like and
/// what is expected now; <see cref="Build"/> returns the live demo. Scenes use the public
/// engine API only.
/// </summary>
public abstract class Scene
{
    public abstract string BugId { get; }
    public abstract string Title { get; }
    /// <summary>What to do on the page.</summary>
    public abstract string Steps { get; }
    /// <summary>What happened before the fix.</summary>
    public abstract string Before { get; }
    /// <summary>What must happen now.</summary>
    public abstract string Expected { get; }

    /// <summary>Header of the <see cref="Before"/> note (research scenes relabel it).</summary>
    protected virtual string BeforeHeader => "До исправления";
    /// <summary>Header of the <see cref="Expected"/> note.</summary>
    protected virtual string ExpectedHeader => "Ожидается сейчас";

    protected abstract Control Build();

    public Control BuildPage() => new StackPanel
    {
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = $"{BugId} — {Title}", FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            Note("Что сделать", Steps, "#1F4E79"),
            Note(BeforeHeader, Before, "#8B1A1A"),
            Note(ExpectedHeader, Expected, "#1B5E20"),
            new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Child = Build(),
            },
        },
    };

    private static Control Note(string header, string text, string color) => new StackPanel
    {
        Children =
        {
            new TextBlock { Text = header, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse(color)) },
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        },
    };

    // ---- small builders shared by the scenes -----------------------------------------

    protected static Button Btn(string text, Action onClick)
    {
        var b = new Button { Content = text };
        b.Click += (_, _) => onClick();
        return b;
    }

    protected static Border Box(string color, double size = 60) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.Parse(color)),
        RenderTransformOrigin = RelativePoint.Center,
    };

    protected static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in children)
            row.Children.Add(c);
        return row;
    }

    protected static StackPanel Column(params Control[] children)
    {
        var col = new StackPanel { Spacing = 12 };
        foreach (var c in children)
            col.Children.Add(c);
        return col;
    }

    /// <summary>A fixed-size stage that lets transformed boxes move without relayout.</summary>
    protected static Panel Stage(double height, params Control[] children)
    {
        var canvas = new Canvas { Height = height, ClipToBounds = false };
        foreach (var c in children)
            canvas.Children.Add(c);
        return canvas;
    }

    protected static void Place(Control c, double left, double top)
    {
        Canvas.SetLeft(c, left);
        Canvas.SetTop(c, top);
    }
}
