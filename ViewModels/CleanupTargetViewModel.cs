using CommunityToolkit.Mvvm.ComponentModel;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>UI wrapper around a single <see cref="CleanupTarget"/>.</summary>
public partial class CleanupTargetViewModel : ViewModelBase
{
    public CleanupTarget Target { get; }

    [ObservableProperty]
    private bool _isSelected;

    public CleanupTargetViewModel(CleanupTarget target, bool isSelected)
    {
        Target = target;
        _isSelected = isSelected;
    }

    public string DisplayName => Target.DisplayName;
    public string FullPath => Target.FullPath;
    public string Reason => Target.Reason;
    public string SizeText => SizeFormatter.Format(Target.SizeBytes);
    public long SizeBytes => Target.SizeBytes;
    public RiskLevel Risk => Target.Risk;
    public string RiskText => Target.Risk.ToString();

    /// <summary>Where the delete will route — shown so the user knows what's recoverable.</summary>
    public string DeleteModeText =>
        Target.DeleteMode == DeleteMode.RecycleBin ? "→ Recycle Bin" : "→ Permanent";
}
