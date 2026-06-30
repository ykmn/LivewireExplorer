using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LivewireBrowser.App.Localization;
using LivewireBrowser.Core.Cache;
using LivewireBrowser.Core.Discovery;
using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Network;
using LivewireBrowser.Core.Settings;

namespace LivewireBrowser.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly NetworkScanner _scanner = new();
    private readonly YamlDeviceCache _cache = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly RescanScheduler _scheduler;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();
    public PlayerViewModel Player { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    // Toggled by the "/" shortcut (MainWindow.OnWindowPreviewKeyDown) — applied to every
    // current DeviceViewModel here, and to each newly created one at the two Devices.Add
    // call sites (cache load on startup, full scan results), so the masked/unmasked state
    // survives rescans instead of resetting every time the list is rebuilt.
    [ObservableProperty]
    private bool _isIpMasked;

    partial void OnIsIpMaskedChanged(bool value)
    {
        foreach (var device in Devices)
            device.IsIpMasked = value;
    }

    [ObservableProperty]
    private DateTime? _lastScanTime;

    [ObservableProperty]
    private double _scanProgress;

    [ObservableProperty]
    private string _scanProgressText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _scanButtonText = Loc.Get("Str_Scan");

    private readonly IProgress<string> _statusProgress;
    private CancellationTokenSource? _scanCts;
    private int _rescanningCount;

    // Set right before the final ("cancelled"/"finished") status line is written on a
    // Scan/Rescan stop, so any straggler progress?.Report(...) calls already queued on the
    // dispatcher from in-flight host probes (LwrpScanner can have up to 64 running
    // concurrently) can't land afterwards and silently overwrite it back to e.g.
    // "x.x.x.x: проверка TCP/93...". Cleared again whenever a new scan/rescan starts.
    private bool _suppressStatusUpdates;

    [ObservableProperty]
    private bool _isAnyDeviceRescanning;

    [ObservableProperty]
    private string _elapsedTimeText = string.Empty;

    private readonly DispatcherTimer _elapsedTimer;
    private DateTime _operationStartTime;

    /// <summary>
    /// One progress bar (in the status area) is shared between the full scan and any
    /// single-device rescan — the full scan reports real (Completed/Total) progress,
    /// while a rescan has no discrete steps (one TCP/93 probe), so it falls back to
    /// an indeterminate bar instead of a second dedicated control.
    /// </summary>
    public bool IsProgressVisible => IsScanning || IsAnyDeviceRescanning;
    public bool IsProgressIndeterminate => !IsScanning && IsAnyDeviceRescanning;

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        UpdateElapsedTimer();
    }

    partial void OnIsAnyDeviceRescanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        UpdateElapsedTimer();
    }

    private void OnDeviceRescanningChanged(bool isRescanning)
    {
        _rescanningCount += isRescanning ? 1 : -1;
        IsAnyDeviceRescanning = _rescanningCount > 0;
    }

    /// <summary>
    /// Stopwatch shown in the status bar for the duration of a Scan or Rescan — starts the
    /// instant either kind of operation begins, stops and clears the moment none are left
    /// running (a device rescan can overlap a full scan, so this tracks "any active" rather
    /// than either flag alone).
    /// </summary>
    private void UpdateElapsedTimer()
    {
        var active = IsScanning || IsAnyDeviceRescanning;
        if (active && !_elapsedTimer.IsEnabled)
        {
            _operationStartTime = DateTime.Now;
            ElapsedTimeText = "00:00";
            _elapsedTimer.Start();
        }
        else if (!active && _elapsedTimer.IsEnabled)
        {
            _elapsedTimer.Stop();
            ElapsedTimeText = string.Empty;
        }
    }

    // The localized key whose value is currently shown in StatusText — null when the current
    // text came from a scanner progress report (per-IP messages, Core layer, always English)
    // rather than a Loc.Get() call. Tracked so OnLanguageChanged can refresh the text when
    // the user switches language while a scan is in progress or just after one finishes.
    private string? _currentStatusKey;

    /// <summary>Sets StatusText from a localized key and remembers the key so OnLanguageChanged
    /// can refresh it — call this instead of StatusText=Loc.Get(...) for any message that
    /// persists visibly across a language switch.</summary>
    private void SetLocalizedStatus(string key)
    {
        _currentStatusKey = key;
        StatusText = Loc.Get(key);
    }

    /// <summary>
    /// Writes a final status line and stops any further progress?.Report(...) callbacks from
    /// overwriting it — see _suppressStatusUpdates. Used when a Scan/Rescan ends, whether by
    /// cancellation, completion, or error.
    /// </summary>
    private void ReportFinalStatus(string text)
    {
        _suppressStatusUpdates = true;
        StatusText = text;
    }

    /// <summary>Variant of ReportFinalStatus that also tracks the source key for OnLanguageChanged
    /// refresh — use this for all cases where a localized Loc.Get() result is the final status.</summary>
    private void ReportFinalLocalizedStatus(string key)
    {
        _currentStatusKey = key;
        ReportFinalStatus(Loc.Get(key));
    }

    /// <summary>
    /// Un-suppresses status updates so a freshly started Scan/Rescan's own progress lines show
    /// up again — without this, starting a new operation right after a cancelled one would stay
    /// silently stuck on the previous "cancelled" line forever.
    /// </summary>
    private void BeginStatusOperation() => _suppressStatusUpdates = false;

    public List<DeviceSortOption> SortOptions { get; private set; } = BuildSortOptions();

    private static List<DeviceSortOption> BuildSortOptions() => new()
    {
        new(DeviceSortMode.ByClass, Loc.Get("Str_SortByClass")),
        new(DeviceSortMode.ByIp, Loc.Get("Str_SortByIp")),
        new(DeviceSortMode.ByName, Loc.Get("Str_SortByName")),
    };

    [ObservableProperty]
    private DeviceSortOption _selectedSortOption;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _searchStatusText = string.Empty;

    [ObservableProperty]
    private DeviceViewModel? _currentMatch;

    private List<DeviceViewModel> _searchMatches = new();
    private int _currentMatchIndex = -1;

    public MainViewModel()
    {
        _scanner.NetworkInterfaceAddress = _settings.LivewireNetworkInterfaceAddress;
        _statusProgress = new Progress<string>(text =>
        {
            if (!_suppressStatusUpdates)
            {
                _currentStatusKey = null; // scanner message — not a localized key, not refreshable
                StatusText = text;
            }
        });
        _selectedSortOption = SortOptions.First(o => o.Mode == DeviceSortMode.ByIp);

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => ElapsedTimeText = (DateTime.Now - _operationStartTime).ToString(@"mm\:ss");

        _scheduler = new RescanScheduler(() => ScanAsync());
        _scheduler.Configure(_settings.AutoRescanPeriodMinutes);

        foreach (var device in _cache.Load())
            Devices.Add(new DeviceViewModel(device, _scanner, _statusProgress, OnDeviceUpdated, OnDeviceRescanningChanged, ReportFinalStatus, BeginStatusOperation) { IsIpMasked = IsIpMasked });

        ApplySort();

        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        ScanButtonText = Loc.Get(IsScanning ? "Str_ToggleStop" : "Str_Scan");

        if (_currentStatusKey != null)
            StatusText = Loc.Get(_currentStatusKey);

        var currentMode = SelectedSortOption.Mode;
        SortOptions = BuildSortOptions();
        OnPropertyChanged(nameof(SortOptions));
        SelectedSortOption = SortOptions.First(o => o.Mode == currentMode);
    }

    /// <summary>
    /// Runs after a single-device rescan finishes: persists the change to the cache
    /// (otherwise corrected names/models/categories only live in memory and revert on
    /// the next app start) and re-applies the current sort, since the rescan may have
    /// changed the device's name/IP-derived sort key.
    /// </summary>
    private void OnDeviceUpdated()
    {
        _cache.Save(Devices.Select(d => d.Device));
        ApplySort();
    }

    public AppSettings Settings => _settings;

    public void ApplySettings(int autoRescanPeriodMinutes, string? networkInterfaceAddress, DiscoveryMode discoveryMode,
        LogLevel logLevel, AppLanguage language)
    {
        _settings.AutoRescanPeriodMinutes = autoRescanPeriodMinutes;
        _settings.LivewireNetworkInterfaceAddress = networkInterfaceAddress;
        _settings.DiscoveryMode = discoveryMode;
        _settings.LogLevel = logLevel;
        _settings.Language = language;
        _settings.Save();
        _scheduler.Configure(autoRescanPeriodMinutes);
        _scanner.NetworkInterfaceAddress = networkInterfaceAddress;
        Log.MinLevel = logLevel;
        if (Loc.Current != language)
            Loc.Apply(language);
    }

    /// <summary>
    /// Bound to the same Scan button: starts a scan when idle, cancels the running
    /// scan when one is in progress (button label/text switches via IsScanning).
    /// AllowConcurrentExecutions is required — otherwise [RelayCommand]'s default
    /// re-entrancy guard disables the button (CanExecute=false) for the whole
    /// duration of the scan, which is exactly what made the "Stop" state unclickable.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            SetLocalizedStatus("Str_StatusStopping");
            Log.Info("MainViewModel: scan cancellation requested by user");
            _scanCts?.Cancel();
            return;
        }

        _scanCts = new CancellationTokenSource();
        BeginStatusOperation();
        IsScanning = true;
        ScanButtonText = Loc.Get("Str_ToggleStop");
        ScanProgress = 0;
        ScanProgressText = string.Empty;
        SetLocalizedStatus("Str_StatusScanStarted");
        try
        {
            var progress = new Progress<(int Completed, int Total)>(p =>
            {
                ScanProgress = p.Total == 0 ? 0 : (double)p.Completed / p.Total;
                ScanProgressText = $"{p.Completed}/{p.Total}";
            });

            var found = await _scanner.FullScanAsync(TimeSpan.FromSeconds(8), progress, _statusProgress, _scanCts.Token, _settings.DiscoveryMode);

            // Dispose the outgoing DeviceViewModels before discarding them — each one
            // subscribes to the static Loc.LanguageChanged event, and without this a full
            // scan would leak the entire previous device list every time.
            foreach (var old in Devices)
                old.Dispose();
            Devices.Clear();
            foreach (var device in found)
                Devices.Add(new DeviceViewModel(device, _scanner, _statusProgress, OnDeviceUpdated, OnDeviceRescanningChanged, ReportFinalStatus, BeginStatusOperation) { IsIpMasked = IsIpMasked });

            ApplySort();
            UpdateSearchMatches();

            _cache.Save(found);
            LastScanTime = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            Log.Info("MainViewModel: scan was cancelled by user");
            ReportFinalLocalizedStatus("Str_StatusScanCancelled");
        }
        catch (Exception ex)
        {
            Log.Error("MainViewModel: scan failed", ex);
            ReportFinalLocalizedStatus("Str_StatusScanError");
        }
        finally
        {
            IsScanning = false;
            ScanButtonText = Loc.Get("Str_Scan");
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    public void PlayChannel(ChannelViewModel channel)
    {
        try
        {
            Log.Info($"MainViewModel: play requested for channel {channel.ChannelNumber} ({channel.MulticastAddress}:{channel.Port})");
            Player.Play(channel.Channel, _settings.LivewireNetworkInterfaceAddress);
        }
        catch (Exception ex)
        {
            Log.Error("MainViewModel: failed to start playback", ex);
        }
    }

    partial void OnSelectedSortOptionChanged(DeviceSortOption value) => ApplySort();

    private void ApplySort()
    {
        IEnumerable<DeviceViewModel> sorted = SelectedSortOption.Mode switch
        {
            DeviceSortMode.ByIp => Devices.OrderBy(d => IpAddressUtil.ToSortKey(d.Ip)),
            DeviceSortMode.ByName => Devices.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
            _ => Devices.OrderBy(d => d.CategoryDisplay, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
        };

        var ordered = sorted.ToList();
        Devices.Clear();
        foreach (var device in ordered)
            Devices.Add(device);
    }

    partial void OnSearchTextChanged(string value) => UpdateSearchMatches();

    private void UpdateSearchMatches()
    {
        if (CurrentMatch != null)
            CurrentMatch.IsHighlighted = false;

        var text = SearchText.Trim();
        if (text.Length == 0)
        {
            _searchMatches = new List<DeviceViewModel>();
            _currentMatchIndex = -1;
            CurrentMatch = null;
            SearchStatusText = string.Empty;
            return;
        }

        _searchMatches = Devices.Where(d => d.MatchesSearch(text)).ToList();
        _currentMatchIndex = _searchMatches.Count > 0 ? 0 : -1;
        UpdateCurrentMatch();
    }

    private void UpdateCurrentMatch()
    {
        if (CurrentMatch != null)
            CurrentMatch.IsHighlighted = false;

        if (_currentMatchIndex >= 0 && _currentMatchIndex < _searchMatches.Count)
        {
            CurrentMatch = _searchMatches[_currentMatchIndex];
            CurrentMatch.IsHighlighted = true;
            SearchStatusText = $"{_currentMatchIndex + 1}/{_searchMatches.Count}";
        }
        else
        {
            CurrentMatch = null;
            SearchStatusText = SearchText.Trim().Length > 0 ? "0/0" : string.Empty;
        }
    }

    [RelayCommand]
    private void NextMatch()
    {
        if (_searchMatches.Count == 0)
            return;

        _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
        UpdateCurrentMatch();
    }

    [RelayCommand]
    private void PreviousMatch()
    {
        if (_searchMatches.Count == 0)
            return;

        _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        UpdateCurrentMatch();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;
}
