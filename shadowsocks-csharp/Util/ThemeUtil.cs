using System.Windows;
using Shadowsocks.Enums;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace Shadowsocks.Util
{
    /// <summary>
    /// Helpers for applying the WPF-UI (Fluent) theme to the application.
    /// </summary>
    public static class ThemeUtil
    {
        /// <summary>
        /// Append the WPF-UI control and theme resource dictionaries to the application
        /// resources and activate <paramref name="theme"/>.
        /// <para>
        /// The dictionaries are <b>appended</b> (never inserted at the front) so that
        /// <see cref="I18NUtil"/>'s assumption that <c>MergedDictionaries[0]</c> is the
        /// localization dictionary (and <c>[1]</c> the notify-icon dictionary) stays valid.
        /// Call this only after the i18n and notify-icon dictionaries have been added.
        /// </para>
        /// </summary>
        public static void ApplyFluentTheme(Application app, ApplicationTheme theme)
        {
            app.Resources.MergedDictionaries.Add(new ControlsDictionary());
            app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });

            // No FluentWindow exists yet at this stage, so do not request a window backdrop here.
            ApplicationThemeManager.Apply(theme, WindowBackdropType.None, true);
        }

        /// <summary>The theme currently configured in Windows, mapped to a WPF-UI application theme.</summary>
        public static ApplicationTheme GetSystemTheme()
        {
            return ApplicationThemeManager.GetSystemTheme() switch
            {
                SystemTheme.Dark or SystemTheme.Glow or SystemTheme.CapturedMotion => ApplicationTheme.Dark,
                SystemTheme.HCBlack or SystemTheme.HCWhite => ApplicationTheme.HighContrast,
                _ => ApplicationTheme.Light,
            };
        }

        /// <summary>Resolve a stored <see cref="AppThemeMode"/> preference to a concrete WPF-UI theme.</summary>
        public static ApplicationTheme Resolve(AppThemeMode mode)
        {
            return mode switch
            {
                AppThemeMode.Light => ApplicationTheme.Light,
                AppThemeMode.Dark => ApplicationTheme.Dark,
                _ => GetSystemTheme(),
            };
        }
    }
}
