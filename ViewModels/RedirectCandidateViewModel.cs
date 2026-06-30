using CommunityToolkit.Mvvm.ComponentModel;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>UI wrapper around a <see cref="RedirectCandidate"/>.</summary>
public partial class RedirectCandidateViewModel : ViewModelBase
{
    public RedirectCandidate Candidate { get; }

    [ObservableProperty]
    private bool _isSelected;

    public RedirectCandidateViewModel(RedirectCandidate candidate)
    {
        Candidate = candidate;
    }

    public string DisplayName => Candidate.DisplayName;
    public string Category => Candidate.Category;
    public string Description => Candidate.Description;
    public string SourcePath => Candidate.SourcePath;
    public string SizeText => SizeFormatter.Format(Candidate.SizeBytes);
    public long SizeBytes => Candidate.SizeBytes;
    public bool IsAlreadyRedirected => Candidate.IsAlreadyRedirected;

    /// <summary>Already-redirected rows can't be selected for redirect again.</summary>
    public bool CanRedirect => !Candidate.IsAlreadyRedirected;

    public string StatusText => Candidate.IsAlreadyRedirected
        ? $"Already redirected → {Candidate.ExistingTarget}"
        : Candidate.Description;
}
