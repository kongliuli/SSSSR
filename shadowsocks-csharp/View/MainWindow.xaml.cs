using Shadowsocks.Services;
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
            // Follow the OS light/dark theme and apply the window backdrop (Mica on Win11,
            // gracefully degrades to a solid Fluent surface on Win10).
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);

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
