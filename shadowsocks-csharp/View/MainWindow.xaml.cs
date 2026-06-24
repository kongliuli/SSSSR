using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Services;
using Shadowsocks.Util;
using Shadowsocks.View.Pages;
using System;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Shadowsocks.View
{
    public partial class MainWindow : FluentWindow
    {
        /// <summary>
        /// Page to land on when the window first opens. Set by the tray before <see cref="Window.Show"/>;
        /// falls back to the dashboard. Keeps the first navigation deterministic (avoids racing the
        /// Loaded handler).
        /// </summary>
        public Type InitialPage { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            // Pages are resolved from the DI container when the NavigationView navigates to them.
            RootNavigation.SetServiceProvider(AppHost.Services);

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Honor the persisted theme preference. When "follow system" is chosen, watch the OS
            // theme so the window tracks it live; otherwise apply the fixed theme + backdrop.
            // Mica on Win11 gracefully degrades to a solid Fluent surface on Win10.
            var mode = Global.GuiConfig?.ThemeMode ?? AppThemeMode.System;
            if (mode == AppThemeMode.System)
            {
                SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);
            }
            else
            {
                ApplicationThemeManager.Apply(ThemeUtil.Resolve(mode), WindowBackdropType.Mica, true);
            }

            RootNavigation.Navigate(InitialPage ?? typeof(DashboardPage));
        }

        /// <summary>Navigate the shell to the given page type (used by the tray entry points).</summary>
        public void NavigateTo(Type pageType)
        {
            if (pageType is not null)
            {
                RootNavigation.Navigate(pageType);
            }
        }
    }
}
