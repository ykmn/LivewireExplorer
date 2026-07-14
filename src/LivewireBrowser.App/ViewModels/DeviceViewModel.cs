using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LivewireBrowser.App.Localization;
using LivewireBrowser.Core.Discovery;
using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Models;

namespace LivewireBrowser.App.ViewModels;

public partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly NetworkScanner _scanner;
    private readonly IProgress<string>? _statusProgress;
    private readonly Action? _onUpdated;
    private readonly Action<bool>? _onRescanningChanged;
    private readonly Action<string>? _reportFinalStatus;
    private readonly Action? _beginStatusOperation;
    private CancellationTokenSource? _rescanCts;

    public DeviceInfo Device { get; private set; }

    [ObservableProperty]
    private bool _isRescanning;

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isIpMasked;

    [ObservableProperty]
    private string _rescanButtonText = Loc.Get("Str_Rescan");

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    public string Ip => Device.Ip;

    /// <summary>Last three octets masked behind asterisks when toggled via the "/" shortcut
    /// (MainWindow.OnWindowPreviewKeyDown sets IsIpMasked on every device) — first octet is
    /// kept since it's the network/subnet identifier, not a per-device address.</summary>
    public string IpDisplay => IsIpMasked ? MaskIp(Ip) : Ip;

    private static string MaskIp(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.*.*.*" : ip;
    }

    partial void OnIsIpMaskedChanged(bool value) => OnPropertyChanged(nameof(IpDisplay));

    public string Model => Device.Model;
    public string Name => Device.Name;
    public DeviceCategory Category => Device.Category;
    public string CategoryDisplay => Category switch
    {
        DeviceCategory.AnalogNode => Loc.Get("Str_CategoryAnalogNode"),
        DeviceCategory.DigitalNode => Loc.Get("Str_CategoryDigitalNode"),
        DeviceCategory.Engine => Loc.Get("Str_CategoryEngine"),
        DeviceCategory.Fusion => Loc.Get("Str_CategoryFusion"),
        DeviceCategory.Codec => Loc.Get("Str_CategoryCodec"),
        DeviceCategory.Processor => Loc.Get("Str_CategoryProcessor"),
        DeviceCategory.TelephoneHybrid => Loc.Get("Str_CategoryTelephoneHybrid"),
        DeviceCategory.IpDriver => Loc.Get("Str_CategoryIpDriver"),
        DeviceCategory.Gpio => Loc.Get("Str_CategoryGpio"),
        _ => Loc.Get("Str_CategoryOther"),
    };

    public DeviceViewModel(DeviceInfo device, NetworkScanner scanner, IProgress<string>? statusProgress = null,
        Action? onUpdated = null, Action<bool>? onRescanningChanged = null, Action<string>? reportFinalStatus = null,
        Action? beginStatusOperation = null)
    {
        Device = device;
        _scanner = scanner;
        _statusProgress = statusProgress;
        _onUpdated = onUpdated;
        _onRescanningChanged = onRescanningChanged;
        _reportFinalStatus = reportFinalStatus;
        _beginStatusOperation = beginStatusOperation;
        RefreshChannels();

        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(CategoryDisplay));
        // Always update regardless of IsRescanning — same pattern as MainViewModel.OnLanguageChanged's
        // ScanButtonText update. Without this, switching language while a rescan was in progress
        // left the button showing the old-language "Стоп"/"Stop" until the rescan finished.
        RescanButtonText = Loc.Get(IsRescanning ? "Str_ToggleStop" : "Str_Rescan");
    }

    /// <summary>
    /// Unsubscribes from the static Loc.LanguageChanged event — without this, every
    /// DeviceViewModel ever created (a full rescan replaces the whole list) would stay
    /// referenced forever via that static event, leaking memory for the life of the app.
    /// </summary>
    public void Dispose() => Loc.LanguageChanged -= OnLanguageChanged;

    public bool MatchesSearch(string text) =>
        Ip.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        Model.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        CategoryDisplay.Contains(text, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = $"http://{Ip}", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"DeviceViewModel: failed to open {Ip} in browser", ex);
        }
    }

    private void RefreshChannels()
    {
        Channels.Clear();
        foreach (var channel in Device.Channels.OrderBy(c => c.ChannelNumber))
            Channels.Add(new ChannelViewModel(channel));
    }

    /// <summary>
    /// Bound to the same Rescan button: starts a rescan when idle, cancels the running
    /// one when in progress (mirrors MainViewModel.ScanAsync's Scan/Stop toggle).
    /// AllowConcurrentExecutions is required, otherwise [RelayCommand]'s default
    /// re-entrancy guard disables the button for the whole rescan, making the "Stop"
    /// state unclickable.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RescanAsync()
    {
        if (IsRescanning)
        {
            Log.Info($"DeviceViewModel: rescan of {Device.Ip} cancellation requested by user");
            _rescanCts?.Cancel();
            return;
        }

        _rescanCts = new CancellationTokenSource();
        _beginStatusOperation?.Invoke();
        IsRescanning = true;
        _onRescanningChanged?.Invoke(true);
        RescanButtonText = Loc.Get("Str_ToggleStop");
        try
        {
            var updated = await _scanner.RescanDeviceAsync(Device, TimeSpan.FromSeconds(5), _statusProgress, _rescanCts.Token);
            if (updated != null)
            {
                Device = updated;
                OnPropertyChanged(nameof(Model));
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Category));
                OnPropertyChanged(nameof(CategoryDisplay));
                RefreshChannels();
                _onUpdated?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            Log.Info($"DeviceViewModel: rescan of {Device.Ip} was cancelled by user");
            _reportFinalStatus?.Invoke(Loc.Get("Str_StatusScanCancelled"));
        }
        catch (Exception ex)
        {
            Log.Error($"DeviceViewModel: rescan of {Device.Ip} failed", ex);
            _reportFinalStatus?.Invoke(Loc.Get("Str_StatusScanError"));
        }
        finally
        {
            IsRescanning = false;
            _onRescanningChanged?.Invoke(false);
            RescanButtonText = Loc.Get("Str_Rescan");
            _rescanCts?.Dispose();
            _rescanCts = null;
        }
    }
}
