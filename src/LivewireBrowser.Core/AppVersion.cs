namespace LivewireBrowser.Core;

/// <summary>
/// Single source of truth for the app's version, shown in the splash screen and settings.
/// Scheme: major.minor.debug (e.g. 0.01.001). See CLAUDE.md "Версионность" for who is
/// allowed to bump which part.
/// </summary>
public static class AppVersion
{
    public const string Version = "1.00.002";
    public const string ReleaseDate = "2026-07-14";
}
