using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using LivewireBrowser.App.Localization;
using LivewireBrowser.Core;

namespace LivewireBrowser.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        HeadingText.Text = Loc.Format("Str_AboutHeading", AppVersion.Version, AppVersion.ReleaseDate);
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();

    // Same Process.Start(UseShellExecute=true) pattern as SettingsWindow's "open logs
    // folder" — launches the URI in the user's default browser instead of WPF trying
    // (and failing) to navigate its own Frame to an external http(s) address.
    private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true,
        });
        e.Handled = true;
    }
}
