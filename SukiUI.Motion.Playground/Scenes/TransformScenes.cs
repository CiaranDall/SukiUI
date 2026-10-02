using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground.Scenes;

public sealed class TransformReplaceScene : Scene
{
    public override string BugId => "Баг 1";
    public override string Title => "Внешняя замена RenderTransform";
    public override string Steps =>
        "Нажмите «Сдвинуть» и «Повернуть» — квадрат двигается. Затем «Заменить RenderTransform извне» " +
        "(так делает стиль/тема/чужой код) и снова «Сдвинуть» / «Повернуть».";
    public override string Before =>
        "После замены сдвиг и поворот переставали работать: записи шли в оторванные от группы трансформации. " +
        "При этом Channel.Value «двигался» — строка ниже показывала расхождение Value и реальной матрицы. Масштаб работал.";
    public override string Expected =>
        "Сдвиг, поворот и масштаб работают и после замены; Value совпадает с реальной матрицей.";

    protected override Control Build()
    {
        var box = Box("#3F7FBF");
        var s = Motion.For(box);
        bool shifted = false, rotated = false, scaled = false;
        var ease = new CubicEaseOut();
        var readout = new TextBlock { FontFamily = FontFamily.Parse("Consolas, monospace") };

        var stage = Stage(120, box);
        Place(box, 20, 30);

        // Value vs what is really on screen.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var m = box.RenderTransform?.Value;
            readout.Text = $"TranslateX.Value = {s.TranslateX.Value,7:0.0}   реально на экране (M31) = {(m?.M31 ?? 0),7:0.0}";
        };
        box.AttachedToVisualTree += (_, _) => timer.Start();
        box.DetachedFromVisualTree += (_, _) => timer.Stop();

        return Column(
            Row(
                Btn("Сдвинуть", () =>
                {
                    shifted = !shifted;
                    s.TranslateX.Offer(s.TranslateX.To(shifted ? 160 : 0).Over(TimeSpan.FromMilliseconds(350)).Ease(ease));
                }),
                Btn("Повернуть", () =>
                {
                    rotated = !rotated;
                    s.Rotate.Offer(s.Rotate.To(rotated ? 45 : 0).Over(TimeSpan.FromMilliseconds(350)).Ease(ease));
                }),
                Btn("Масштаб", () =>
                {
                    scaled = !scaled;
                    s.Scale.Offer(s.Scale.To(scaled ? 1.3 : 1.0).Over(TimeSpan.FromMilliseconds(350)).Ease(ease));
                }),
                Btn("Заменить RenderTransform извне", () => box.RenderTransform = new ScaleTransform(1, 1))),
            stage,
            readout);
    }
}

public sealed class SharedEffectScene : Scene
{
    public override string BugId => "Баг 7";
    public override string Title => "Тень из стиля общая для всех контролов";
    public override string Steps =>
        "Три карточки получают одну и ту же DropShadowEffect из сеттера стиля. Нажмите «Анимировать тень первой карточки».";
    public override string Before =>
        "Движок мутировал эффект на месте, а значение сеттера — один экземпляр на все контролы: тень менялась у всех трёх карточек.";
    public override string Expected => "Меняется тень только первой карточки.";

    protected override Control Build()
    {
        var cards = Enumerable.Range(0, 3).Select(i =>
        {
            var card = Box("#FFFFFF", 90);
            card.Classes.Add("card");
            return card;
        }).ToArray();
        var root = Row(cards);
        root.Spacing = 40;
        root.Margin = new Avalonia.Thickness(30);
        root.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Border>().Class("card"))
        {
            Setters =
            {
                new Avalonia.Styling.Setter(Avalonia.Visual.EffectProperty,
                    new DropShadowEffect { BlurRadius = 10, Opacity = 0.35, Color = Colors.Black }),
            },
        });

        var s = Motion.For(cards[0]);
        bool deep = false;
        return Column(
            Btn("Анимировать тень первой карточки", () =>
            {
                deep = !deep;
                new Choreography()
                    .And(s.ShadowBlur.To(deep ? 40 : 10).Over(TimeSpan.FromMilliseconds(400)))
                    .And(s.ShadowOpacity.To(deep ? 0.9 : 0.35).Over(TimeSpan.FromMilliseconds(400)))
                    .Start(cards[0]);
            }),
            root);
    }
}

public sealed class StylePriorityScene : Scene
{
    public override string BugId => "Баг 6 (открыт)";
    public override string Title => "Записи движка навсегда перебивают стили";
    public override string Steps =>
        "Нажмите «Проявить» (движок анимирует Opacity 0.2 → 1). Затем включите класс dim — стиль задаёт Opacity = 0.4.";
    public override string Before =>
        "Движок пишет LocalValue и не очищает его: стиль dim после анимации игнорируется.";
    public override string Expected =>
        "Пока НЕ исправлено — запланировано на этап API (приоритет записи и семантика конца анимации). " +
        "Сцена показывает текущее поведение.";

    protected override Control Build()
    {
        var box = Box("#7E57C2", 90);
        var host = new Border { Child = box, Padding = new Avalonia.Thickness(20) };
        host.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Border>().Class("dim"))
        {
            Setters = { new Avalonia.Styling.Setter(Avalonia.Visual.OpacityProperty, 0.4) },
        });
        var opacity = Motion.For(box).Opacity;
        var state = new TextBlock();
        var toggle = new ToggleButton { Content = "Класс dim (Opacity 0.4 в стиле)" };
        toggle.IsCheckedChanged += (_, _) =>
        {
            box.Classes.Set("dim", toggle.IsChecked == true);
            state.Text = $"Opacity сейчас = {box.Opacity:0.00}";
        };

        return Column(
            Row(Btn("Проявить", () =>
                {
                    box.Opacity = 0.2;
                    opacity.Offer(opacity.To(1.0).Over(TimeSpan.FromMilliseconds(500)));
                }), toggle),
            host,
            state);
    }
}
