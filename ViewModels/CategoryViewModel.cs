using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinButler.Models;
using WinButler.Services;

namespace WinButler.ViewModels;

/// <summary>Groups the scan results of one <see cref="IScanner"/> for the dashboard.</summary>
public partial class CategoryViewModel : ViewModelBase
{
    private readonly Action _onSelectionChanged;

    public string Title { get; }
    public CleanupCategory Category { get; }

    public ObservableCollection<CleanupTargetViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    public CategoryViewModel(string title, CleanupCategory category, Action onSelectionChanged)
    {
        Title = title;
        Category = category;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>Replaces the item list with fresh scan results (largest first).</summary>
    public void SetItems(IEnumerable<CleanupTarget> targets)
    {
        foreach (var existing in Items)
            existing.PropertyChanged -= OnItemPropertyChanged;
        Items.Clear();

        foreach (var t in targets.OrderByDescending(t => t.SizeBytes))
        {
            // Safe items are pre-selected; Caution/Risky require an explicit opt-in.
            var vm = new CleanupTargetViewModel(t, isSelected: t.Risk == RiskLevel.Safe);
            vm.PropertyChanged += OnItemPropertyChanged;
            Items.Add(vm);
        }

        RaiseTotals();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanupTargetViewModel.IsSelected))
        {
            RaiseTotals();
            _onSelectionChanged();
        }
    }

    public IEnumerable<CleanupTargetViewModel> SelectedItems => Items.Where(i => i.IsSelected);

    /// <summary>Drives the per-category pages' list-vs-empty-state switch.</summary>
    public bool HasItems => Items.Count > 0;

    public long TotalBytes => Items.Sum(i => i.SizeBytes);
    public long SelectedBytes => SelectedItems.Sum(i => i.SizeBytes);
    public int SelectedCount => SelectedItems.Count();
    public string SelectedBytesText => SizeFormatter.Format(SelectedBytes);
    public string TotalBytesText => SizeFormatter.Format(TotalBytes);

    public string HeaderText =>
        Items.Count == 0
            ? $"{Title} — nothing found"
            : $"{Title} — {Items.Count} item(s), {SizeFormatter.Format(TotalBytes)} " +
              $"({SizeFormatter.Format(SelectedBytes)} selected)";

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    private void SetAll(bool selected)
    {
        foreach (var item in Items)
            item.IsSelected = selected;
    }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(TotalBytesText));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedBytesText));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HeaderText));
    }
}
