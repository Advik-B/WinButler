using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>UI wrapper around one <see cref="DevToolGroup"/> card. Selection is per-card
/// (all-or-nothing for that tool's reclaimable subset), matching the mockup's single
/// "CLEAN" pill per card rather than a nested per-subfolder checklist.</summary>
public partial class DevToolGroupViewModel : ViewModelBase
{
    public DevToolGroup Group { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _cleaned;

    public string DisplayName => Group.DisplayName;
    public string SourcePath => Group.SourcePath;
    public string Description => Group.Description;
    public string Category => Group.Category;
    public string OnDiskText => SizeFormatter.Format(Group.OnDiskBytes);
    public string ReclaimableText => SizeFormatter.Format(Group.ReclaimableBytes);
    public bool IsLocked => Group.IsLocked;
    public bool IsAlreadyRedirected => Group.IsAlreadyRedirected;

    /// <summary>Only tools with something reclaimable and not locked show the CLEAN toggle.</summary>
    public bool IsSelectable => !Group.IsLocked && Group.ReclaimableBytes > 0 && !Cleaned;

    /// <summary>Every dev-tool root in scope came from the redirect catalog, so redirect is
    /// always offered unless it's already a junction.</summary>
    public bool CanRedirect => !Group.IsAlreadyRedirected;

    /// <summary>Two-letter mark from the display name, e.g. "JetBrains (Local)" -> "JB".</summary>
    public string Mark
    {
        get
        {
            var letters = System.Array.FindAll(DisplayName.ToCharArray(), char.IsLetter);
            return letters.Length >= 2
                ? new string(new[] { char.ToUpperInvariant(letters[0]), char.ToUpperInvariant(letters[1]) })
                : DisplayName.ToUpperInvariant();
        }
    }

    public DevToolGroupViewModel(DevToolGroup group)
    {
        Group = group;
    }

    [RelayCommand]
    private void ToggleSelected()
    {
        if (IsSelectable)
            IsSelected = !IsSelected;
    }
}
