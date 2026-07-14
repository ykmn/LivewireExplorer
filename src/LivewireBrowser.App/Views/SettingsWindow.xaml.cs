using System.Diagnostics;
using System.IO;
using System.Windows;
using LivewireBrowser.App.Localization;
using LivewireBrowser.App.ViewModels;
using LivewireBrowser.Core.Cache;
using LivewireBrowser.Core.Discovery;
using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Network;
using LivewireBrowser.Core.Settings;

namespace LivewireBrowser.App.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _mainViewModel;

    private record DiscoveryModeOption(DiscoveryMode Mode, string Label);
    private record LanguageOption(AppLanguage Language, string Label);

    private static DiscoveryModeOption[] BuildDiscoveryModeOptions() => new[]
    {
        new DiscoveryModeOption(DiscoveryMode.BruteForce, Loc.Get("Str_DiscoveryBruteForce")),
        new DiscoveryModeOption(DiscoveryMode.BruteForceAndAdvertisement, Loc.Get("Str_DiscoveryBruteForceAndAdvertisement")),
        new DiscoveryModeOption(DiscoveryMode.AdvertisementOnly, Loc.Get("Str_DiscoveryAdvertisementOnly")),
    };

    private static LanguageOption[] BuildLanguageOptions() => new[]
    {
        new LanguageOption(AppLanguage.English, Loc.Get("Str_LanguageEnglish")),
        new LanguageOption(AppLanguage.Russian, Loc.Get("Str_LanguageRussian")),
    };

    private static readonly LogLevel[] LogLevelOptions = { LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error };

    public SettingsWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;

        PeriodTextBox.Text = _mainViewModel.Settings.AutoRescanPeriodMinutes.ToString();
        MaxSweepHostsTextBox.Text = _mainViewModel.Settings.MaxSweepHosts.ToString();
        CachePathText.Text = new YamlDeviceCache().FilePath;
        LogPathText.Text = Log.LogDirectoryPath;

        var discoveryModeOptions = BuildDiscoveryModeOptions();
        DiscoveryModeComboBox.ItemsSource = discoveryModeOptions;
        DiscoveryModeComboBox.SelectedItem = discoveryModeOptions.First(o => o.Mode == _mainViewModel.Settings.DiscoveryMode);

        var languageOptions = BuildLanguageOptions();
        LanguageComboBox.ItemsSource = languageOptions;
        LanguageComboBox.SelectedItem = languageOptions.First(o => o.Language == _mainViewModel.Settings.Language);

        LogLevelComboBox.ItemsSource = LogLevelOptions;
        LogLevelComboBox.SelectedItem = _mainViewModel.Settings.LogLevel;

        LoadInterfaces();
    }

    private void LoadInterfaces()
    {
        var interfaces = NetworkInterfaceHelper.GetAvailableInterfaces();
        InterfaceComboBox.ItemsSource = interfaces;

        var selected = interfaces.FirstOrDefault(i => i.IpAddress == _mainViewModel.Settings.LivewireNetworkInterfaceAddress);
        InterfaceComboBox.SelectedItem = selected;
    }

    private void OnRefreshInterfacesClick(object sender, RoutedEventArgs e) => LoadInterfaces();

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.LogDirectoryPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = Log.LogDirectoryPath,
            UseShellExecute = true,
        });
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PeriodTextBox.Text, out var minutes) || minutes < 0)
        {
            MessageBox.Show(this, Loc.Get("Str_ErrorMinutes"), Loc.Get("Str_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(MaxSweepHostsTextBox.Text, out var maxSweepHosts) || maxSweepHosts is < 1 or > 65536)
        {
            MessageBox.Show(this, Loc.Get("Str_ErrorMaxSweepHosts"), Loc.Get("Str_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedInterface = InterfaceComboBox.SelectedItem as NetworkInterfaceInfo;
        var selectedDiscoveryMode = (DiscoveryModeComboBox.SelectedItem as DiscoveryModeOption)?.Mode
            ?? DiscoveryMode.BruteForceAndAdvertisement;
        var selectedLogLevel = LogLevelComboBox.SelectedItem is LogLevel level ? level : LogLevel.Warn;
        var selectedLanguage = (LanguageComboBox.SelectedItem as LanguageOption)?.Language ?? AppLanguage.English;
        _mainViewModel.ApplySettings(minutes, maxSweepHosts, selectedInterface?.IpAddress, selectedDiscoveryMode, selectedLogLevel, selectedLanguage);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        new YamlDeviceCache().Clear();
        MessageBox.Show(this, Loc.Get("Str_CacheCleared"), Loc.Get("Str_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
