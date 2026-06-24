namespace Shadowsocks.Enums
{
    /// <summary>
    /// Application color theme preference. Named <c>AppThemeMode</c> to avoid colliding with the
    /// WPF <c>System.Windows.ThemeMode</c> type/property introduced in .NET 9+.
    /// </summary>
    public enum AppThemeMode
    {
        /// <summary>Follow the Windows light/dark setting.</summary>
        System,
        Light,
        Dark
    }
}
