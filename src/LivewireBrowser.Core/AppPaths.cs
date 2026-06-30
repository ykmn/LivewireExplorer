namespace LivewireBrowser.Core;

/// <summary>
/// Data files (cache, settings) live next to the executable, in a "data" subfolder,
/// rather than %LOCALAPPDATA%, so they travel with the app and survive even when
/// the app is run from a portable/non-Program-Files location.
/// </summary>
public static class AppPaths
{
    public static string AppDataDirectory => Path.Combine(AppContext.BaseDirectory, "data");
}
