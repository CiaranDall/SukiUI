# SukiUI.Motion — инженерные заметки

Неочевидные особенности Avalonia, движка и тестовой обвязки, найденные по ходу работы.
Каждая запись: **факт → источник → что это значит для нас**.

Версия Avalonia на момент записей: **12.1.1**. При обновлении Avalonia — перепроверять
записи с пометкой источника в исходниках Avalonia.

---

## 1. Avalonia: кадровый цикл и `RequestAnimationFrame`

### 1.1. RAF глобален, а не per-TopLevel

- **Факт.** `TopLevel.RequestAnimationFrame(cb)` только проверяет поток и пересылает вызов в
  `MediaContext.Instance.RequestAnimationFrame` — один `MediaContext` на UI-поток, одна
  очередь колбэков на все окна.
- **Источник.** `src/Avalonia.Controls/TopLevel.cs` (`RequestAnimationFrame`),
  `src/Avalonia.Base/Media/MediaContext.Clock.cs`.
- **Для нас.** «Один цикл на TopLevel» в `SukiTicker` — это N регистраций в одной глобальной
  очереди при N окнах. Корректно, но разбиение по TopLevel не даёт изоляции по времени кадра:
  все окна пульсируют в одном `Render()`.

### 1.2. Как RAF перевзводится изнутри кадра

- **Факт.** `RequestAnimationFrame` = `ScheduleRender(false)` + постановка в очередь.
  Во время `Render()` поле `_nextRenderOp` ещё не обнулено, поэтому новый RAF **не ставит**
  новый проход рендера. Следующий кадр приходит одним из двух путей (конец `RenderCore`):
  1. кадр что-то изменил → коммит в композитор → по завершении пакета
     (`CompositionBatchFinished`) → `ScheduleRender` — темп композитора / vsync;
  2. ничего не изменил → `_animationsTimer`: `DispatcherTimer` на **16 мс реального времени**.
- **Источник.** `src/Avalonia.Base/Media/MediaContext.cs` (`ScheduleRender`, `Render`,
  `RenderCore`), `MediaContext.Compositor.cs` (`CommitCompositorsWithThrottling`,
  `CompositionBatchFinished`).
- **Для нас.**
  - Подписчик тикера, который ничего не пишет, крутится на темпе `DispatcherTimer`, а не vsync.
    Каналы движка пишут каждый кадр, поэтому в норме идут по пути (1). Комментарий в
    `SukiTicker` про «prime the pump» верен по сути.
  - Троттлинг: пока предыдущий пакет композиции не обработан, новый коммит не уходит —
    при тяжёлом рендере кадры анимации пропускаются (позы считаются по абсолютному времени,
    так что траектории не «замедляются», а прореживаются; пружины ограничены `dt ≤ 50 мс`).

### 1.3. Время кадра

- **Факт.** RAF передаёт в колбэк `_time.Elapsed` контекста на момент `Render()`.
  `SukiTicker` этот аргумент игнорирует и берёт `Stopwatch` в момент вызова подписчиков.
- **Источник.** `MediaContext.Clock.cs` (`Pulse(now)`), `SukiUI.Motion/SukiTicker.cs`.
- **Для нас.** Кандидат в раздел производительности (джиттер позы относительно кадра).

---

## 2. Avalonia.Headless: тестирование анимаций

### 2.1. Прокачка кадров

- **Факт.** По умолчанию `ShouldRenderOnUIThread = true` → `HeadlessRenderTimer`;
  `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` синхронно тикает рендер, затем
  `Dispatcher.UIThread.RunJobs()` выполняет `CompositionBatchFinished` → `Render()` → RAF.
  Итог: **один вызов подписчика на один `Frame()`**, если подписчик пишет визуальное свойство.
- **Источник.** `src/Headless/Avalonia.Headless/AvaloniaHeadlessPlatform.cs`;
  проверено тестом `HarnessTests.Ticker_runs_one_callback_per_pumped_frame_on_virtual_time`.
- **Для нас.** Тесты должны анимировать то, что реально пишет свойства. Подписчик «без
  записи» в headless после первого кадра замирает (путь 1.2-(2) идёт по реальному времени).

### 2.2. Общее состояние между тестами

- **Факт.** `MediaContext` — синглтон на всю тестовую сессию. Незавершённые RAF-регистрации
  и пакеты композиции от предыдущего теста влияют на следующий (наблюдали недетерминированный
  провал, зависящий от порядка тестов).
- **Для нас.** `MotionHarness.Dispose` прокачивает 3 кадра перед закрытием окна.
  Параллельность тестов отключена: `SukiTicker.ClockOverride` — статическое поле.

### 2.3. Пакеты

- `Avalonia.Headless.XUnit 12.1.1` зависит от **xunit.v3 3.2.2** (не xunit 2.x):
  тестовый проект — `OutputType=Exe`, `using Xunit` подключается явно (`<Using Include="Xunit" />`).

---

## 3. Движок SukiUI.Motion: поведение под капотом

### 3.1. `Channel.Value` читает состояние движка, а не экран

- **Факт.** Каналы трансформаций читают значения из своего блока (`Transforms.Block`), а не из
  фактического `RenderTransform` элемента. Если блок рассинхронизирован с экраном, `Value`
  врёт. Именно так проявлялся баг 1: `Value == 10`, на экране 0.
- **Для нас.** В тестах проверять **итоговую матрицу** `RenderTransform.Value`, а не только
  `Channel.Value`.

---

## 4. Журнал багов

| # | Суть | Статус | Тест |
|---|---|---|---|
| 1 | Внешняя замена `RenderTransform` отрывала translate/rotate/skew | Подтверждён, исправлен | `TransformBlockTests` |
