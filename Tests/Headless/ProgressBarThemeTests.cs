using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace WinButler.Tests.Headless;

/// <summary>
/// Smoke test for the custom ProgressBar theme's indeterminate state: applying the template
/// with IsIndeterminate on activates the keyframe animation styles, so a malformed
/// TemplateSettings binding or selector throws here instead of at first real busy state.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ProgressBarThemeTests
{
    [AvaloniaFact]
    public void Indeterminate_bar_applies_the_template_and_animation_without_throwing()
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 180 };
        var window = new Window { Content = bar };

        window.Show();                 // forces template apply + style attach
        Dispatcher.UIThread.RunJobs(); // let the animation/binding activation run

        Assert.True(bar.IsIndeterminate);
        window.Close();
    }

    [AvaloniaFact]
    public void Toggling_indeterminate_back_to_determinate_does_not_throw()
    {
        // The shell status bar flips this repeatedly (MFT parse → delete progress).
        var bar = new ProgressBar { IsIndeterminate = true, Width = 180, Maximum = 1 };
        var window = new Window { Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bar.IsIndeterminate = false;
        bar.Value = 0.5;
        Dispatcher.UIThread.RunJobs();

        bar.IsIndeterminate = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(bar.IsIndeterminate);
        window.Close();
    }
}
