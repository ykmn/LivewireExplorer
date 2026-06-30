using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LivewireBrowser.App.ViewModels;
using LivewireBrowser.App.Views;

namespace LivewireBrowser.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    // Tracks where the "+"/"-" expand/collapse-all shortcuts are in their two-step cycle —
    // see OnWindowPreviewKeyDown. 0 = nothing forced expanded, 1 = groups expanded only,
    // 2 = groups and devices both expanded. Starts at 1, not 0: CategoryGroupExpanderStyle's
    // default Setter is IsExpanded="True", so a freshly realized group is already expanded
    // out of the box — starting this counter at 0 would desync it from that real default and
    // make the first "-" press after launch silently no-op (it thought everything was already
    // collapsed). Reset to 1 whenever the group structure is rebuilt from scratch (sort mode
    // switch, full rescan) for the same reason — new Expanders/DeviceViewModels come back to
    // their class defaults (groups expanded, devices collapsed), not wherever the stage was
    // left from before.
    private int _expandStage = 1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        PreviewKeyDown += OnWindowPreviewKeyDown;

        var settings = _viewModel.Settings;
        if (settings.WindowWidth is > 200 && settings.WindowHeight is > 200)
        {
            Width = settings.WindowWidth.Value;
            Height = settings.WindowHeight.Value;
        }

        if (settings.WindowLeft is { } left && settings.WindowTop is { } top && IsOnVisibleScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        Closing += OnClosing;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateGrouping();
        LufsScaleHost.Content = BuildLufsScale();
        // The CurrentChannel != null guard matters: PhasePairsReceived is marshaled onto the
        // UI thread via Dispatcher.BeginInvoke (PlayerViewModel ctor), so a batch raised on
        // the audio thread a moment before Stop() runs can still be sitting in the dispatcher
        // queue when Stop() executes — Stop() sets CurrentChannel to null and clears the
        // scope synchronously, but that queued Plot() call would still run right after,
        // repainting a few stale points and making it look like Clear() never happened.
        _viewModel.Player.PhasePairsReceived += pairs =>
        {
            if (_viewModel.Player.CurrentChannel != null)
                Phasescope.Plot(pairs);
        };
        _viewModel.Player.PropertyChanged += OnPlayerPropertyChanged;
    }

    /// <summary>
    /// Builds the LUFS scale shown left of the loudness meters: one tick per 1 LU (60 ticks
    /// for the meters' -60..0 dB range — far too many to hand-write in XAML), with a labeled
    /// major tick every 10 LU. Uses the same number of equal-height rows as the range in LU,
    /// so each tick's row boundary lines up exactly with the corresponding fraction of the
    /// bars' fill (PlayerViewModel.Normalize uses the same -60dB floor).
    /// </summary>
    private static UIElement BuildLufsScale()
    {
        const int totalLu = 60;
        const int majorEvery = 10;

        var grid = new Grid();
        for (var i = 0; i < totalLu; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var tickBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C6370"));
        tickBrush.Freeze();

        for (var i = 0; i <= totalLu; i++)
        {
            var isMajor = i % majorEvery == 0;
            var isBottom = i == totalLu;
            var row = Math.Min(i, totalLu - 1);
            var alignment = isBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;

            var tick = new Border
            {
                Height = 1,
                Width = isMajor ? 8 : 4,
                Background = tickBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = alignment,
            };
            Grid.SetRow(tick, row);
            grid.Children.Add(tick);

            if (!isMajor)
                continue;

            var label = new TextBlock
            {
                Text = isBottom ? "-∞" : (-i).ToString(),
                FontSize = 9,
                Foreground = tickBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = alignment,
                Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
        }

        return grid;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentMatch) && _viewModel.CurrentMatch != null)
            DevicesListBox.ScrollIntoView(_viewModel.CurrentMatch);

        if (e.PropertyName == nameof(MainViewModel.SelectedSortOption))
        {
            UpdateGrouping();
            _expandStage = 1;
        }

        // A full scan replaces every DeviceViewModel/group container with fresh ones at
        // their class defaults (see _expandStage's doc comment) — without this, the stage
        // counter from before the scan would no longer match what's actually on screen.
        if (e.PropertyName == nameof(MainViewModel.LastScanTime))
            _expandStage = 1;
    }

    /// <summary>Blanks the phasescope once playback stops (PlayerViewModel.Stop sets
    /// CurrentChannel back to null) — otherwise the last trace before Stop just sits there
    /// frozen, looking like a still-live signal instead of "nothing playing".</summary>
    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.CurrentChannel) && _viewModel.Player.CurrentChannel == null)
            Phasescope.Clear();
    }

    /// <summary>
    /// Global shortcuts: "/" toggles masking the last three IP octets behind asterisks;
    /// "+"/"-" cycle expand/collapse for the device list. Skipped while a TextBox has focus
    /// (the search box) so typing those characters works normally there.
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        switch (e.Key)
        {
            case Key.OemQuestion or Key.Divide:
                _viewModel.IsIpMasked = !_viewModel.IsIpMasked;
                e.Handled = true;
                break;
            case Key.OemPlus or Key.Add:
                HandleExpandPress();
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract:
                HandleCollapsePress();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// In "by class" sort: first press expands every class group, a second press also
    /// expands every device's channel list. In the other sort modes there are no groups, so
    /// it just expands every device's channel list directly.
    /// </summary>
    private void HandleExpandPress()
    {
        if (_viewModel.SelectedSortOption.Mode != DeviceSortMode.ByClass)
        {
            SetAllDevicesExpanded(true);
            return;
        }

        if (_expandStage >= 2)
            return;
        _expandStage++;
        ApplyExpandStage();
    }

    /// <summary>Mirror of HandleExpandPress: first press collapses every device's channel
    /// list, a second press also collapses every class group.</summary>
    private void HandleCollapsePress()
    {
        if (_viewModel.SelectedSortOption.Mode != DeviceSortMode.ByClass)
        {
            SetAllDevicesExpanded(false);
            return;
        }

        if (_expandStage <= 0)
            return;
        _expandStage--;
        ApplyExpandStage();
    }

    private void ApplyExpandStage()
    {
        DevicesListBox.UpdateLayout();
        SetAllGroupsExpanded(_expandStage >= 1);
        SetAllDevicesExpanded(_expandStage >= 2);
    }

    private void SetAllGroupsExpanded(bool expanded)
    {
        foreach (var expander in FindVisualChildren<Expander>(DevicesListBox))
            expander.IsExpanded = expanded;
    }

    private void SetAllDevicesExpanded(bool expanded)
    {
        foreach (var device in _viewModel.Devices)
            device.IsExpanded = expanded;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;
            foreach (var grandchild in FindVisualChildren<T>(child))
                yield return grandchild;
        }
    }

    /// <summary>
    /// Device cards are only grouped by class when that's the active sort — for the
    /// other two sort modes the list stays flat (group headers would just repeat
    /// "Прочее" in IP/name order, which isn't useful).
    /// </summary>
    private void UpdateGrouping()
    {
        var view = (CollectionViewSource)Resources["GroupedDevices"];
        view.GroupDescriptions.Clear();
        if (_viewModel.SelectedSortOption.Mode == DeviceSortMode.ByClass)
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceViewModel.CategoryDisplay)));
    }

    /// <summary>
    /// Guards against restoring a position from a monitor that's no longer connected (laptop
    /// undocked, second monitor unplugged, etc.) — without this the window could reopen
    /// entirely off the visible desktop with no way to drag it back.
    /// </summary>
    private static bool IsOnVisibleScreen(double left, double top)
    {
        const double margin = 50; // a sliver of the title bar on-screen is enough to drag it back
        return left + margin >= SystemParameters.VirtualScreenLeft
            && top + margin >= SystemParameters.VirtualScreenTop
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Use RestoreBounds when maximized/minimized — Width/Height/Left/Top reflect the
        // maximized (full-screen) or minimized (taskbar) geometry at that point, not the
        // normal window position the user would expect back on next launch.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        var settings = _viewModel.Settings;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
        settings.Save();
    }

    private void OnChannelNameClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChannelViewModel { IsActive: true } channel })
            _viewModel.PlayChannel(channel);
    }

    private void OnDeviceHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceViewModel device })
            device.ToggleExpandedCommand.Execute(null);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel)
        {
            Owner = this,
        };
        settingsWindow.ShowDialog();
    }

    private void OnOutputDeviceClick(object sender, MouseButtonEventArgs e)
    {
        var player = _viewModel.Player;
        var dialog = new OutputDeviceWindow(player.OutputDevices, player.SelectedOutputDevice)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true && dialog.SelectedDevice != null)
            player.SelectedOutputDevice = dialog.SelectedDevice;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow
        {
            Owner = this,
        };
        aboutWindow.ShowDialog();
    }
}
