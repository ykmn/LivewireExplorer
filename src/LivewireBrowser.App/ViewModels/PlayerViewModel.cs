using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LivewireBrowser.App.Localization;
using LivewireBrowser.Audio;
using LivewireBrowser.Core.Models;

namespace LivewireBrowser.App.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly ChannelPlayer _player = new();

    /// <summary>Display floor for the loudness/true-peak meters — values are clamped here
    /// before being normalized to a 0..1 bar fill, since -infinity (digital silence) has
    /// no natural lower bound.</summary>
    private const double MeterFloorDb = -60.0;

    // True Peak zone thresholds (dBTP) — red >= -6, yellow -6..-18, green below -18.
    private const double TruePeakRedThreshold = -6.0;
    private const double TruePeakYellowThreshold = -18.0;

    // Momentary/Short Term/Integrated zone thresholds (LUFS) — red >= -12, yellow -12..-23,
    // green below -23.
    private const double LoudnessRedThreshold = -12.0;
    private const double LoudnessYellowThreshold = -23.0;

    [ObservableProperty]
    private ChannelInfo? _currentChannel;

    [ObservableProperty]
    private float _level;

    [ObservableProperty]
    private double _volume = 0.8;

    [ObservableProperty]
    private OutputDeviceOption? _selectedOutputDevice;

    [ObservableProperty]
    private double _truePeakCurrent = double.NegativeInfinity;
    [ObservableProperty]
    private double _truePeakMax = double.NegativeInfinity;
    [ObservableProperty]
    private double _truePeakLeftCurrent = double.NegativeInfinity;
    [ObservableProperty]
    private double _truePeakRightCurrent = double.NegativeInfinity;
    [ObservableProperty]
    private double _momentaryCurrent = double.NegativeInfinity;
    [ObservableProperty]
    private double _momentaryMax = double.NegativeInfinity;
    [ObservableProperty]
    private double _shortTermCurrent = double.NegativeInfinity;
    [ObservableProperty]
    private double _shortTermMax = double.NegativeInfinity;
    [ObservableProperty]
    private double _integratedCurrent = double.NegativeInfinity;
    [ObservableProperty]
    private double _integratedMax = double.NegativeInfinity;

    public string TruePeakCurrentDisplay => Format(TruePeakCurrent);
    public string TruePeakMaxDisplay => Format(TruePeakMax);
    public string MomentaryCurrentDisplay => Format(MomentaryCurrent);
    public string MomentaryMaxDisplay => Format(MomentaryMax);
    public string ShortTermCurrentDisplay => Format(ShortTermCurrent);
    public string ShortTermMaxDisplay => Format(ShortTermMax);
    public string IntegratedCurrentDisplay => Format(IntegratedCurrent);
    public string IntegratedMaxDisplay => Format(IntegratedMax);

    public MeterZoneFractions TruePeakZones => ComputeZones(TruePeakCurrent, TruePeakRedThreshold, TruePeakYellowThreshold);
    public MeterZoneFractions TruePeakLeftZones => ComputeZones(TruePeakLeftCurrent, TruePeakRedThreshold, TruePeakYellowThreshold);
    public MeterZoneFractions TruePeakRightZones => ComputeZones(TruePeakRightCurrent, TruePeakRedThreshold, TruePeakYellowThreshold);
    public MeterZoneFractions MomentaryZones => ComputeZones(MomentaryCurrent, LoudnessRedThreshold, LoudnessYellowThreshold);
    public MeterZoneFractions ShortTermZones => ComputeZones(ShortTermCurrent, LoudnessRedThreshold, LoudnessYellowThreshold);
    public MeterZoneFractions IntegratedZones => ComputeZones(IntegratedCurrent, LoudnessRedThreshold, LoudnessYellowThreshold);

    public List<OutputDeviceOption> OutputDevices { get; } = new();

    // Computed instead of a Binding.FallbackValue in XAML — WPF doesn't allow a
    // DynamicResource inside a Binding's FallbackValue (throws XamlParseException at
    // load time, confirmed), so the localized "(not selected)" fallback has to live here.
    public string SelectedOutputDeviceDisplay => SelectedOutputDevice?.Name ?? Loc.Get("Str_OutputNotSelected");

    /// <summary>LW number + name for the player bar's "Канал:" readout — previously just the
    /// name, which left no way to tell which Livewire channel is actually playing when
    /// several sources share the same/similar name. "—" mirrors ChannelViewModel.LwDisplay's
    /// fallback for channels with no LW number (e.g. SRC entries with no live source yet).</summary>
    public string CurrentChannelDisplay
    {
        get
        {
            if (CurrentChannel == null)
                return "—";
            var lw = CurrentChannel.LwNumber > 0 ? CurrentChannel.LwNumber.ToString() : "—";
            return $"{lw}  {CurrentChannel.Name}";
        }
    }

    partial void OnCurrentChannelChanged(ChannelInfo? value) => OnPropertyChanged(nameof(CurrentChannelDisplay));

    // Re-exposed (not bound through a property — see PhasescopeControl, which MainWindow.xaml.cs
    // feeds directly via this event, the same procedural-UI pattern already used for the LUFS
    // scale/BuildLufsScale) so a continuous stream of point batches doesn't have to go through
    // ObservableProperty/PropertyChanged churn just to reach a custom-drawn control.
    public event Action<(float Left, float Right)[]>? PhasePairsReceived;

    public PlayerViewModel()
    {
        // BeginInvoke (not Invoke): the level/loudness events fire on the WASAPI render
        // thread, and Stop() blocks the UI thread waiting for that thread to exit — a
        // synchronous Invoke back into the UI thread at that moment deadlocks the app.
        _player.LevelChanged += level => Application.Current.Dispatcher.BeginInvoke(() => Level = level);
        _player.LoudnessChanged += snapshot => Application.Current.Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
        _player.PhasePairsReceived += pairs => Application.Current.Dispatcher.BeginInvoke(() => PhasePairsReceived?.Invoke(pairs));
        RefreshOutputDevices();

        // PlayerViewModel is created once and lives for the app's lifetime (owned by the
        // singleton MainViewModel), so a permanent subscription here doesn't leak the way
        // DeviceViewModel's would.
        Loc.LanguageChanged += () => OnPropertyChanged(nameof(SelectedOutputDeviceDisplay));
    }

    private void ApplySnapshot(LoudnessSnapshot snapshot)
    {
        TruePeakCurrent = snapshot.TruePeakDb;
        if (snapshot.TruePeakDb > TruePeakMax)
            TruePeakMax = snapshot.TruePeakDb;

        TruePeakLeftCurrent = snapshot.TruePeakDbLeft;
        TruePeakRightCurrent = snapshot.TruePeakDbRight;

        MomentaryCurrent = snapshot.MomentaryLufs;
        if (snapshot.MomentaryLufs > MomentaryMax)
            MomentaryMax = snapshot.MomentaryLufs;

        ShortTermCurrent = snapshot.ShortTermLufs;
        if (snapshot.ShortTermLufs > ShortTermMax)
            ShortTermMax = snapshot.ShortTermLufs;

        IntegratedCurrent = snapshot.IntegratedLufs;
        if (snapshot.IntegratedLufs > IntegratedMax)
            IntegratedMax = snapshot.IntegratedLufs;

        OnPropertyChanged(nameof(TruePeakCurrentDisplay));
        OnPropertyChanged(nameof(TruePeakMaxDisplay));
        OnPropertyChanged(nameof(TruePeakZones));
        OnPropertyChanged(nameof(TruePeakLeftZones));
        OnPropertyChanged(nameof(TruePeakRightZones));
        OnPropertyChanged(nameof(MomentaryCurrentDisplay));
        OnPropertyChanged(nameof(MomentaryMaxDisplay));
        OnPropertyChanged(nameof(MomentaryZones));
        OnPropertyChanged(nameof(ShortTermCurrentDisplay));
        OnPropertyChanged(nameof(ShortTermMaxDisplay));
        OnPropertyChanged(nameof(ShortTermZones));
        OnPropertyChanged(nameof(IntegratedCurrentDisplay));
        OnPropertyChanged(nameof(IntegratedMaxDisplay));
        OnPropertyChanged(nameof(IntegratedZones));
    }

    [RelayCommand]
    private void ResetTruePeakMax() => TruePeakMax = double.NegativeInfinity;

    [RelayCommand]
    private void ResetMomentaryMax() => MomentaryMax = double.NegativeInfinity;

    [RelayCommand]
    private void ResetShortTermMax() => ShortTermMax = double.NegativeInfinity;

    [RelayCommand]
    private void ResetIntegratedMax() => IntegratedMax = double.NegativeInfinity;

    // [ObservableProperty] notifies the underlying *Max property itself; these forward
    // that into the *MaxDisplay strings the UI actually binds to (covers both the reset
    // commands above and the normal ApplySnapshot update path).
    partial void OnTruePeakMaxChanged(double value) => OnPropertyChanged(nameof(TruePeakMaxDisplay));
    partial void OnMomentaryMaxChanged(double value) => OnPropertyChanged(nameof(MomentaryMaxDisplay));
    partial void OnShortTermMaxChanged(double value) => OnPropertyChanged(nameof(ShortTermMaxDisplay));
    partial void OnIntegratedMaxChanged(double value) => OnPropertyChanged(nameof(IntegratedMaxDisplay));

    private static string Format(double db) =>
        double.IsFinite(db) ? db.ToString("0.0", CultureInfo.InvariantCulture) : "-inf";

    private static double Normalize(double db)
    {
        if (!double.IsFinite(db))
            return 0;
        var clamped = Math.Clamp(db, MeterFloorDb, 0.0);
        return (clamped - MeterFloorDb) / -MeterFloorDb;
    }

    /// <summary>
    /// Splits the meter's full 0..1 height into 6 stacked fractions (red/yellow/green, each
    /// "lit" up to the current reading and "unlit" above it) instead of coloring the whole
    /// filled bar a single color for whatever zone the current value happens to be in — a
    /// segment only changes color once the reading actually reaches it, like a real LED meter.
    /// Zone heights are fixed (based on the thresholds), only how far each is "lit" varies.
    /// </summary>
    private static MeterZoneFractions ComputeZones(double currentDb, double redThreshold, double yellowThreshold)
    {
        var normalizedLevel = Normalize(currentDb);

        var redSpan = (0.0 - redThreshold) / -MeterFloorDb;
        var yellowSpan = (redThreshold - yellowThreshold) / -MeterFloorDb;
        var greenSpan = (yellowThreshold - MeterFloorDb) / -MeterFloorDb;

        var greenLit = Math.Clamp(normalizedLevel, 0, greenSpan);
        var yellowLit = Math.Clamp(normalizedLevel - greenSpan, 0, yellowSpan);
        var redLit = Math.Clamp(normalizedLevel - greenSpan - yellowSpan, 0, redSpan);

        return new MeterZoneFractions(
            RedUnlit: redSpan - redLit,
            RedLit: redLit,
            YellowUnlit: yellowSpan - yellowLit,
            YellowLit: yellowLit,
            GreenUnlit: greenSpan - greenLit,
            GreenLit: greenLit);
    }

    public void RefreshOutputDevices()
    {
        OutputDevices.Clear();
        foreach (var device in AudioPlaybackEngine.EnumerateOutputDevices())
            OutputDevices.Add(new OutputDeviceOption(device.ID, device.FriendlyName));

        SelectedOutputDevice ??= OutputDevices.FirstOrDefault();
    }

    public void Play(ChannelInfo channel, string? localInterfaceAddress = null)
    {
        CurrentChannel = channel;
        _player.Play(channel.MulticastAddress, channel.Port, channel.SampleRate, channel.Channels,
            channel.BitsPerSample, SelectedOutputDevice?.Id, (float)Volume, localInterfaceAddress);
    }

    [RelayCommand]
    private void Stop()
    {
        _player.Stop();
        CurrentChannel = null;
        Level = 0;
        ApplySnapshot(new LoudnessSnapshot
        {
            TruePeakDb = double.NegativeInfinity,
            TruePeakDbLeft = double.NegativeInfinity,
            TruePeakDbRight = double.NegativeInfinity,
            MomentaryLufs = double.NegativeInfinity,
            ShortTermLufs = double.NegativeInfinity,
            IntegratedLufs = double.NegativeInfinity,
        });
    }

    partial void OnVolumeChanged(double value)
    {
        _player.SetVolume((float)value);
    }

    partial void OnSelectedOutputDeviceChanged(OutputDeviceOption? value)
    {
        OnPropertyChanged(nameof(SelectedOutputDeviceDisplay));
        if (CurrentChannel != null)
            Play(CurrentChannel);
    }
}

public record OutputDeviceOption(string Id, string Name);

/// <summary>
/// Six stacked Star-weight fractions (top to bottom: red unlit/lit, yellow unlit/lit, green
/// unlit/lit) that sum to 1.0, describing a segmented LED-style vertical meter bar. Bound
/// directly into Grid.RowDefinitions in MainWindow.xaml via ProgressFillConverter.
/// </summary>
public record MeterZoneFractions(
    double RedUnlit,
    double RedLit,
    double YellowUnlit,
    double YellowLit,
    double GreenUnlit,
    double GreenLit);
