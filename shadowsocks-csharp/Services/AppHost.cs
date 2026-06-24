using Microsoft.Extensions.DependencyInjection;
using Shadowsocks.Controller;
using System;

namespace Shadowsocks.Services
{
    /// <summary>
    /// Application composition root: owns the dependency-injection container.
    /// <para>
    /// During the migration off the static <see cref="Shadowsocks.Model.Global"/> service
    /// locator, the container is the authoritative owner of the singletons while
    /// <c>Global</c> still mirrors the key instances. Consumers are moved onto constructor
    /// injection incrementally; once the last <c>Global.*</c> reference is gone the bridge
    /// in <c>Global</c> can be deleted.
    /// </para>
    /// </summary>
    public static class AppHost
    {
        private static IServiceProvider _services;

        /// <summary>The built service provider. Throws if <see cref="Init"/> has not run.</summary>
        public static IServiceProvider Services =>
            _services ?? throw new InvalidOperationException(@"AppHost is not initialized; call AppHost.Init() first.");

        /// <summary>Build the DI container. Call once, after the configuration has been loaded.</summary>
        public static void Init()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Core singletons. MenuViewController depends on MainController; the container
            // resolves the same MainController instance for both.
            services.AddSingleton<MainController>();
            services.AddSingleton<MenuViewController>();

            // Single-window Fluent shell and its navigation pages. Pages are transient: the
            // NavigationView resolves a fresh instance from the container on each navigation.
            services.AddTransient<View.MainWindow>();
            services.AddTransient<ViewModel.DashboardViewModel>();
            services.AddTransient<ViewModel.SubscriptionsViewModel>();
            services.AddTransient<ViewModel.ServersViewModel>();
            services.AddTransient<ViewModel.StatisticsViewModel>();
            services.AddTransient<ViewModel.SettingsViewModel>();
            services.AddTransient<ViewModel.PortForwardingViewModel>();
            services.AddTransient<ViewModel.LogsViewModel>();
            services.AddTransient<View.Pages.DashboardPage>();
            services.AddTransient<View.Pages.ServersPage>();
            services.AddTransient<View.Pages.SubscriptionsPage>();
            services.AddTransient<View.Pages.StatisticsPage>();
            services.AddTransient<View.Pages.PortForwardingPage>();
            services.AddTransient<View.Pages.LogsPage>();
            services.AddTransient<View.Pages.SettingsPage>();
            services.AddTransient<View.Pages.AboutPage>();
        }

        /// <summary>Resolve a required service from the container.</summary>
        public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    }
}
