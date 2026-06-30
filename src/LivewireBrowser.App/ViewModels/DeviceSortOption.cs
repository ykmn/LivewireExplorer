namespace LivewireBrowser.App.ViewModels;

public enum DeviceSortMode
{
    ByClass,
    ByIp,
    ByName,
}

public record DeviceSortOption(DeviceSortMode Mode, string Label);
