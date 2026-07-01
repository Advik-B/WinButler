namespace WinButler.ViewModels;

/// <summary>
/// Stand-in for a nav destination whose real screen hasn't been built yet (Dashboard,
/// Dev Junk). Keeps the shell fully navigable while those screens are built in later
/// passes — replace the reference in <see cref="MainWindowViewModel"/> once the real
/// page exists, don't extend this class.
/// </summary>
public sealed class PlaceholderPageViewModel : ViewModelBase
{
    public string Title { get; }
    public string Message { get; }

    public PlaceholderPageViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }
}
