using System.ComponentModel;
using Avalonia.Controls;
using KPatchLauncher.ViewModels;

namespace KPatchLauncher.Controls;

public partial class PatchDetailsView : UserControl
{
    private MainViewModel? _viewModel;

    public PatchDetailsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnContextChanged();
    }

    private void OnContextChanged()
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainViewModel;
        if (_viewModel == null)
            return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateMarkdownAssetResolver(_viewModel);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MainViewModel vm && e.PropertyName == nameof(MainViewModel.SelectedPatch))
            UpdateMarkdownAssetResolver(vm);
    }

    private void UpdateMarkdownAssetResolver(MainViewModel vm)
    {
        MarkdownViewer.OpenAsset = vm.SelectedPatch?.OpenAsset;
    }
}
