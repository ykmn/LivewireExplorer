using System.Windows;
using LivewireBrowser.Core;

namespace LivewireBrowser.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Версия {AppVersion.Version} от {AppVersion.ReleaseDate}";
    }

    public void SetProgress(double value, string status)
    {
        ProgressBarControl.Value = value;
        StatusText.Text = status;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
