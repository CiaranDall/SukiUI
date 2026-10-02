using Avalonia;
using Avalonia.Headless;
using SukiUI.Motion.RenderTests;

[assembly: AvaloniaTestApplication(typeof(RenderTestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SukiUI.Motion.RenderTests;

public sealed class RenderTestApp : Application;

public static class RenderTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<RenderTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
