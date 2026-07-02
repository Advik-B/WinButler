using Avalonia;
using Avalonia.Headless;
using WinButler.Tests.Headless;

// Registers the headless Avalonia app for [AvaloniaFact]/[AvaloniaTheory] tests: each test body
// runs on a real Avalonia UI thread with a working Dispatcher, but on the headless windowing
// backend (no window, no GPU, no UAC). We only need the UI-thread + Dispatcher context for the
// interaction/logic suite, so no Skia rendering backend is configured.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace WinButler.Tests.Headless;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<WinButler.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
