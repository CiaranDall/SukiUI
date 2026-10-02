using Avalonia.Controls;
using Avalonia.Threading;

namespace SukiUI.Motion.Playground.Scenes;

public sealed class ChoreographyRestartScene : Scene
{
    public override string BugId => "Баг 3";
    public override string Title => "Перезапуск идущей хореографии";
    public override string Steps =>
        "Ползунок переключается одной и той же хореографией (так устроен SukiToggleSwitchMotion). " +
        "Быстро нажмите «Переключить» 2–3 раза подряд, затем подождите и смотрите на счётчик и на строку статистики внизу.";
    public override string Before =>
        "Каждый перезапуск на лету оставлял висящую подписку тикера: счётчик завершений рос сам по себе, " +
        "а движок не засыпал — внизу оставалось ≈60 dispatch/s навсегда.";
    public override string Expected =>
        "Одно завершение на серию нажатий; после остановки ползунка внизу «IDLE (0 кадров движка)».";

    protected override Control Build()
    {
        var knob = Box("#00897B", 50);
        var track = new Border
        {
            Width = 260,
            Height = 70,
            CornerRadius = new Avalonia.CornerRadius(35),
            Background = Avalonia.Media.Brushes.Gainsboro,
            Padding = new Avalonia.Thickness(10),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Child = new Border { Child = knob, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
        };
        var counter = new TextBlock { FontSize = 16 };
        int settles = 0;
        bool on = false;

        var x = Motion.For(knob).TranslateX;
        var snap = new Choreography()
            .And(x.To(() => on ? 190 : 0).Spring(new Spring(Omega: 18, Decay: 12)))
            .Then(() => counter.Text = $"Завершений (settle): {++settles}");
        counter.Text = "Завершений (settle): 0";

        return Column(
            Btn("Переключить", () =>
            {
                on = !on;
                snap.Start(knob); // the same instance, restarted mid-flight
            }),
            track,
            counter);
    }
}

public sealed class FrozenChannelScene : Scene
{
    public override string BugId => "Баг 9";
    public override string Title => "Канал после остановленной хореографии";
    public override string Steps =>
        "«Увеличить хореографией» запускает пружину к 1.5 и через 120 мс останавливает хореографию (её вытеснили). " +
        "Затем «Вернуть через Offer» — обычная timed-анимация к 1.0.";
    public override string Before =>
        "В канале оставалась «замёрзшая» пружина, которую никто не продвигал, и правило «пружину вытесняет только нажатие» " +
        "отбрасывало возврат — квадрат застревал увеличенным навсегда.";
    public override string Expected => "Квадрат возвращается к 1.0.";

    protected override Control Build()
    {
        var box = Box("#5C6BC0", 80);
        var scale = Motion.For(box).Scale;
        var trace = new TraceView { Label = "Scale", Min = 0.9, Max = 1.6 }.Follow(() => scale.Value);
        var stage = new Border { Padding = new Avalonia.Thickness(50), Child = box, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };

        return Column(
            Row(
                Btn("Увеличить хореографией", () =>
                {
                    var grow = new Choreography().And(scale.To(1.5).Spring(new Spring(10, 9)));
                    grow.Start(box);
                    DispatcherTimer.RunOnce(grow.Stop, TimeSpan.FromMilliseconds(120));
                }),
                Btn("Вернуть через Offer", () =>
                    scale.Offer(scale.To(1.0).Over(TimeSpan.FromMilliseconds(300))))),
            stage,
            trace);
    }
}
