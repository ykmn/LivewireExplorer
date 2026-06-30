using System.Windows;
using LivewireBrowser.App.ViewModels;

namespace LivewireBrowser.App.Views;

public partial class OutputDeviceWindow : Window
{
    public OutputDeviceOption? SelectedDevice { get; private set; }

    public OutputDeviceWindow(IReadOnlyList<OutputDeviceOption> devices, OutputDeviceOption? current)
    {
        InitializeComponent();
        DataContext = devices;
        DeviceListBox.SelectedItem = devices.FirstOrDefault(d => d.Id == current?.Id);
    }

    private void OnSelectClick(object sender, RoutedEventArgs e)
    {
        if (DeviceListBox.SelectedItem is OutputDeviceOption selected)
            SelectedDevice = selected;

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
