namespace LivewireBrowser.Core;

/// <summary>
/// Single source of truth for the app's version, shown in the splash screen and settings.
/// Scheme: major.minor.debug (e.g. 0.01.001). See CLAUDE.md "Версионность" for who is
/// allowed to bump which part.
/// </summary>
public static class AppVersion
{
    public const string Version = "0.01.043";
    public const string ReleaseDate = "2026-06-30";
}
