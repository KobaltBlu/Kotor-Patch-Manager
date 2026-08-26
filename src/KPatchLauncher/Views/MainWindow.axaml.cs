using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using KPatchLauncher.ViewModels;

namespace KPatchLauncher.Views;

public partial class MainWindow : Window
{
    private bool _detailsModalOpen;

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnPatchesDragOver);
        AddHandler(DragDrop.DropEvent, OnPatchesDrop);
        SizeChanged += OnWindowSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.NotifyWindowWidth(Bounds.Width);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPatch))
            _ = MaybeOpenPatchDetailsModalAsync();
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.NotifyWindowWidth(e.NewSize.Width);
    }

    private async System.Threading.Tasks.Task MaybeOpenPatchDetailsModalAsync()
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (!vm.IsWorkspaceNarrow || vm.SelectedPatch == null || _detailsModalOpen)
            return;

        if (vm.Dialogs == null)
            return;

        _detailsModalOpen = true;
        try
        {
            await vm.Dialogs.ShowPatchDetailsAsync(vm);
            vm.SelectedPatch = null;
        }
        finally
        {
            _detailsModalOpen = false;
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://KPatchLauncher/Assets/icon.ico"));
            Icon = new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load window icon: {ex.Message}");
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximize();

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private static bool IsOverPatchesPanel(object? source)
    {
        for (var visual = source as Control; visual != null; visual = visual.Parent as Control)
        {
            if (visual.Name == "PatchesPanel")
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> GetKpatchPathsFromDrag(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null || files.Length == 0)
            return Array.Empty<string>();

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p)
                        && p!.EndsWith(".kpatch", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .ToList();
    }

    private void OnPatchesDragOver(object? sender, DragEventArgs e)
    {
        if (!IsOverPatchesPanel(e.Source))
            return;

        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnPatchesDrop(object? sender, DragEventArgs e)
    {
        if (!IsOverPatchesPanel(e.Source))
            return;

        e.Handled = true;
        var paths = GetKpatchPathsFromDrag(e);
        if (paths.Count == 0 || DataContext is not MainViewModel vm)
            return;

        await vm.AddKpatchFilesAsync(paths);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (e.Key == Key.F1)
        {
            if (vm.OpenAboutCommand.CanExecute(null))
                vm.OpenAboutCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.L)
        {
            if (vm.LaunchGameCommand.CanExecute(null))
                vm.LaunchGameCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Enter)
        {
            if (vm.ApplyPatchesCommand.CanExecute(null))
                vm.ApplyPatchesCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            if (vm.RefreshCommand.CanExecute(null))
                vm.RefreshCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (SearchBox.IsFocused && !string.IsNullOrEmpty(vm.SearchText))
                vm.SearchText = string.Empty;
            else
                vm.SelectedPatch = null;

            e.Handled = true;
        }
    }
}
