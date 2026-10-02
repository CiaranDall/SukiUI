using System.Threading;
using Avalonia.Controls;
using SukiUI.Motion.Composition;

namespace SukiUI.Motion.Playground.Scenes;

public sealed class PrototypeSpringScene : CompositionScene
{
    private static readonly Spring Bouncy = new(Omega: 10, Decay: 6);

    public override string BugId => "Прототип P1";
    public override string Title => "Composition-бэкенд: пружина, прерывание, удар, нагрузка";
    public override string Steps =>
        "Оба квадрата получают одни и те же команды: верхний — SukiUI.Motion (UI-поток), нижний — прототип " +
        "CompositionMotion (поток рендера). «Туда / обратно» можно жать посреди полёта (прерывание с инерцией). " +
        "«Удар» — пинок скоростью с места. «Туда + занять UI-поток на 1 с» — запуск и сразу сон UI-потока.";
    public override string Before =>
        "Прототип держит точную модель пружины на UI-потоке (поза и скорость для прерываний) и отдаёт её композитору " +
        "один раз на старт — плотными ключевыми кадрами; покадровой работы на UI-потоке нет (PLAN D21).";
    public override string Expected =>
        "Без нагрузки квадраты двигаются одинаково: те же перелёты, те же развороты при прерывании, без рывка. " +
        "Под нагрузкой верхний замирает и потом прыгает, нижний доигрывает пружину плавно.";

    protected override Control Build()
    {
        var top = Box("#E07B39", 50);
        var bottom = Box("#3F7FBF", 50);
        var engine = Motion.For(top).TranslateX;
        var proto = CompositionMotion.For(bottom).TranslateX;
        engine.Track(0);
        engine.Track(300);
        double target = 0;

        void Go(double to, double? kick = null)
        {
            target = to;
            var spring = engine.To(to).Spring(Bouncy);
            if (kick is { } k)
                spring.SeedVelocity(k);
            engine.Offer(spring);
            proto.SpringTo(to, Bouncy, kick);
        }

        return Column(
            Row(
                Btn("Туда / обратно", () => Go(target == 0 ? 300 : 0)),
                Btn("Удар", () => Go(target, kick: target == 0 ? 1800 : -1800)),
                Btn("Туда + занять UI-поток на 1 с", () =>
                {
                    Go(target == 0 ? 300 : 0);
                    Thread.Sleep(1000);
                })),
            new TextBlock { Text = "SukiUI.Motion (UI-поток):" },
            top,
            new TextBlock { Text = "CompositionMotion — прототип (поток рендера):" },
            bottom);
    }
}
