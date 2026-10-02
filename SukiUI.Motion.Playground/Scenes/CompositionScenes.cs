using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground.Scenes;

/// <summary>
/// Phase 2 research scenes: what Avalonia's Composition API does, observed live — the
/// claims of ENGINEERING_NOTES §7 that headless tests cannot show (headless renders on the
/// UI thread and server-side values are not observable).
/// </summary>
public abstract class CompositionScene : Scene
{
    protected override string BeforeHeader => "Что показывают исходники Avalonia";
    protected override string ExpectedHeader => "Как проверить вывод";

    /// <summary>Runs <paramref name="start"/> once the element has its composition visual
    /// (it only exists while attached) and <paramref name="stop"/> on detach.</summary>
    protected static void WithVisual(Visual element, Action<CompositionVisual> start, Action<CompositionVisual>? stop = null)
    {
        element.AttachedToVisualTree += (_, _) =>
        {
            if (ElementComposition.GetElementVisual(element) is { } v)
                start(v);
        };
        element.DetachedFromVisualTree += (_, _) =>
        {
            if (ElementComposition.GetElementVisual(element) is { } v)
                stop?.Invoke(v);
        };
    }

    /// <summary>A forever ping-pong of Translation.X on the render thread.</summary>
    protected static void PingPong(CompositionVisual v, double distance, TimeSpan halfPeriod)
    {
        var anim = v.Compositor.CreateVector3DKeyFrameAnimation();
        anim.InsertKeyFrame(0f, new Vector3D(0, 0, 0), new LinearEasing());
        anim.InsertKeyFrame(1f, new Vector3D(distance, 0, 0), new SineEaseInOut());
        anim.Duration = halfPeriod;
        anim.IterationBehavior = Avalonia.Rendering.Composition.Animations.AnimationIterationBehavior.Forever;
        anim.Direction = PlaybackDirection.Alternate;
        v.StartAnimation("Translation", anim);
    }
}

public sealed class UiThreadStallScene : CompositionScene
{
    /// <summary>Measurement hooks (see <see cref="FrameMeasurement"/>): the engine-driven box
    /// and every half-cycle start, in MotionTicker time.</summary>
    internal static Border? EngineBox;
    internal static Action<TimeSpan>? HalfCycleStarted;

    public override string BugId => "Composition C1";
    public override string Title => "Анимация при занятом UI-потоке";
    public override string Steps =>
        "Два квадрата качаются одинаково: верхний — SukiUI.Motion (UI-поток), нижний — composition-анимация " +
        "(поток рендера). Нажмите «Занять UI-поток на 1.5 с» — UI-поток засыпает (так выглядят тяжёлый layout, " +
        "создание страницы, синхронный I/O, большой GC).";
    public override string Before =>
        "На Windows композитор по умолчанию рендерит на отдельном потоке (Win32PlatformOptions.ShouldRenderOnUIThread = false). " +
        "Composition-анимации вычисляются там же (KeyFrameAnimationInstance) и не требуют UI-потока.";
    public override string Expected =>
        "Верхний квадрат замирает на 1.5 с (и потом прыгает), нижний продолжает плавно качаться. " +
        "Сама кнопка тоже «залипает» — это нормально, UI-поток спит. Замер (--measure-c1): без нагрузки движок идёт " +
        "ровно (≈144 кадр/с на 144 Гц, ±0.5 мс), но каждый полупериод на ~1.3 мс длиннее 700 мс (следующий полупериод " +
        "стартует с кадра settle) — квадраты постепенно расходятся по фазе; после «залипания» верхний сдвигается по фазе навсегда.";

    protected override Control Build()
    {
        var top = Box("#E07B39", 50);
        var bottom = Box("#3F7FBF", 50);
        var half = TimeSpan.FromMilliseconds(700);

        // Engine side: the same motion, re-armed at every settle.
        var x = Animate.For(top).TranslateX;
        bool right = false;
        void Swing()
        {
            if (TopLevel.GetTopLevel(top) is null)
                return; // page left: stop the loop
            right = !right;
            HalfCycleStarted?.Invoke(MotionStats.Now);
            new Choreography()
                .And(x.To(right ? 300 : 0).Over(half).Ease(new SineEaseInOut()))
                .Then(Swing)
                .Start(top);
        }
        top.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(Swing);
        EngineBox = top;

        WithVisual(bottom, v => PingPong(v, 300, half), v => v.StopAnimation("Translation"));

        return Column(
            Btn("Занять UI-поток на 1.5 с", () => Thread.Sleep(1500)),
            new TextBlock { Text = "SukiUI.Motion (UI-поток):" },
            top,
            new TextBlock { Text = "Composition (поток рендера):" },
            bottom);
    }
}

public sealed class OpacitySyncScene : CompositionScene
{
    public override string BugId => "Composition C2";
    public override string Title => "Синхронизация Visual → CompositionVisual";
    public override string Steps =>
        "Нажмите «Запустить все четыре». Каждый квадрат — composition-анимация Opacity 1 → 0.15 за 4 с, " +
        "у каждого свой сценарий (подпись под квадратом). Через 1.5 с сценарии B и C делают своё действие. " +
        "Сравните квадраты между собой.";
    public override string Before =>
        "Visual.SynchronizeCompositionProperties при перерисовке пишет comp.Opacity, но клиентский сеттер пишет только " +
        "при ИЗМЕНЕНИИ значения; запись снимает анимацию свойства. Проверено на пикселях (SukiUI.Motion.RenderTests).";
    public override string Expected =>
        "A — плавно бледнеет 4 с. B (InvalidateVisual на 1.5 с) — как A, ничего не ломается. " +
        "C (Visual.Opacity = 0.99 на 1.5 с) — на 1.5 с резко становится почти чёрным и остаётся. " +
        "D (Visual.Opacity изменена прямо перед стартом, в том же кадре) — не анимируется вообще.";

    protected override Control Build()
    {
        var boxes = new[] { "A: только анимация", "B: + InvalidateVisual", "C: + Visual.Opacity = 0.99", "D: Opacity перед стартом" }
            .Select(label => (Label: label, Box: Box("#000000", 80)))
            .ToArray();

        static void Fade(Border box)
        {
            if (ElementComposition.GetElementVisual(box) is not { } v)
                return;
            var anim = v.Compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(1f, 0.15f, new LinearEasing());
            anim.Duration = TimeSpan.FromSeconds(4);
            v.StartAnimation("Opacity", anim);
        }

        void RunAll()
        {
            // Reset in a separate frame: a Visual.Opacity change and StartAnimation of the same
            // property in ONE batch cancel the animation (that is exactly scenario D).
            foreach (var (_, box) in boxes)
                box.Opacity = 1.0;
            DispatcherTimer.RunOnce(() =>
            {
                Fade(boxes[0].Box);
                Fade(boxes[1].Box);
                Fade(boxes[2].Box);
                boxes[3].Box.Opacity = 0.98; // scenario D: change, then start, same frame
                Fade(boxes[3].Box);
                DispatcherTimer.RunOnce(() =>
                {
                    boxes[1].Box.InvalidateVisual();
                    boxes[2].Box.Opacity = 0.99;
                }, TimeSpan.FromMilliseconds(1500));
            }, TimeSpan.FromMilliseconds(100));
        }

        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 24 };
        foreach (var (label, box) in boxes)
            row.Children.Add(new StackPanel { Spacing = 6, Children = { box, new TextBlock { Text = label, Width = 130, TextWrapping = TextWrapping.Wrap } } });

        return Column(Btn("Запустить все четыре", RunAll), row);
    }
}

public sealed class NoReadbackScene : CompositionScene
{
    public override string BugId => "Composition C3";
    public override string Title => "Анимированное значение не читается на UI-потоке";
    public override string Steps => "«Запустить»: composition-анимация Scale 1 → 1.6 за 2 с. Смотрите на строку под квадратом.";
    public override string Before =>
        "Клиентский геттер свойства возвращает последнее значение, записанное с UI-потока (поле клиента); анимация живёт " +
        "только на сервере. Readback сервера (TryGetValidReadback) — internal, используется для хит-теста.";
    public override string Expected =>
        "Квадрат растёт, а comp.Scale, прочитанный на UI-потоке, остаётся 1. Следствие для движка: позу и скорость " +
        "для прерываний нужно считать по своей модели траектории, а не читать.";

    protected override Control Build()
    {
        var box = Box("#7E57C2", 80);
        var readout = new TextBlock { FontFamily = FontFamily.Parse("Consolas, monospace") };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => readout.Text = $"comp.Scale (UI-поток) = {ElementComposition.GetElementVisual(box)?.Scale}";
        box.AttachedToVisualTree += (_, _) => timer.Start();
        box.DetachedFromVisualTree += (_, _) => timer.Stop();

        return Column(
            Btn("Запустить", () =>
            {
                if (ElementComposition.GetElementVisual(box) is not { } v)
                    return;
                v.CenterPoint = new Vector3D(box.Bounds.Width / 2, box.Bounds.Height / 2, 0);
                var anim = v.Compositor.CreateVector3DKeyFrameAnimation();
                anim.InsertKeyFrame(0f, new Vector3D(1, 1, 1), new LinearEasing());
                anim.InsertKeyFrame(1f, new Vector3D(1.6, 1.6, 1), new LinearEasing());
                anim.Duration = TimeSpan.FromSeconds(2);
                v.StartAnimation("Scale", anim);
            }),
            new Border { Padding = new Thickness(40), Child = box, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
            readout);
    }
}

public sealed class HitTestScene : CompositionScene
{
    public override string BugId => "Composition C4";
    public override string Title => "Хит-тест и геометрия при composition-сдвиге";
    public override string Steps =>
        "Квадрат сдвинут на 200 px вправо через comp.Translation (не RenderTransform). Пунктир — место, которое ему " +
        "отвёл layout. Наведите курсор на нарисованный квадрат, потом на пунктир.";
    public override string Before =>
        "Хит-тест (CompositionTarget.TryHitTest) идёт по серверному readback-у — по тому, что реально отрисовано, " +
        "с задержкой до кадра. Геометрия UI-потока (TranslatePoint, Bounds) про comp.Translation не знает.";
    public override string Expected =>
        "Квадрат подсвечивается, когда курсор над нарисованным квадратом, а не над пунктиром. " +
        "TranslatePoint ниже показывает позицию из layout — без сдвига на 200.";

    protected override Control Build()
    {
        var box = Box("#3F7FBF", 70);
        var idle = box.Background;
        box.PointerEntered += (_, _) => box.Background = Brushes.OrangeRed;
        box.PointerExited += (_, _) => box.Background = idle;
        WithVisual(box, v => v.Translation = new Vector3D(200, 0, 0));

        var ghost = new Border
        {
            Width = 70,
            Height = 70,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = box,
        };
        var readout = new TextBlock();
        box.LayoutUpdated += (_, _) =>
        {
            var root = TopLevel.GetTopLevel(box);
            if (root is not null && box.TranslatePoint(default, root) is { } p)
                readout.Text = $"TranslatePoint(0,0 → окно) = {p}  (layout, без comp.Translation)";
        };

        return Column(
            new Border { Padding = new Thickness(20), Child = ghost, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
            readout);
    }
}
