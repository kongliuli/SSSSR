using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shadowsocks.Controller;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wpf.Ui.Appearance;

namespace Shadowsocks.ViewModel
{
    /// <summary>
    /// Backing view-model for the Fluent "Settings" page. Presents the legacy
    /// <c>SettingsWindow</c> and <c>DnsSettingWindow</c> as section cards on a single
    /// scrolling page: General, Proxy &amp; PAC, DNS and Update.
    /// <para>
    /// Edits are made against an in-memory copy (<see cref="Config"/>) loaded from
    /// <see cref="Global.Load"/>; <see cref="SaveCommand"/> writes the copy back through
    /// <see cref="MainController.SaveServersConfig"/> (or <see cref="Global.SaveConfig"/> as a
    /// fallback) exactly like the old windows did.
    /// </para>
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        /// <summary>The working configuration copy bound by the page. Replaced on each <see cref="Reload"/>.</summary>
        [ObservableProperty] private Configuration _config = new();

        /// <summary>Whether Windows "run at startup" is currently enabled (applied on save).</summary>
        [ObservableProperty] private bool _autoStartup;

        /// <summary>Status / feedback line shown at the bottom of the page.</summary>
        [ObservableProperty] private string _statusText = string.Empty;

        #region General

        /// <summary>Languages offered in the General section. Values are culture names persisted to <c>LangName</c>.</summary>
        public ObservableCollection<string> Languages { get; } = new(new[]
        {
            @"en-US",
            @"zh-CN",
            @"zh-TW"
        });

        /// <summary>Theme options for the General section (follow system / light / dark).</summary>
        public ObservableCollection<ThemeOption> Themes { get; } = new(new[]
        {
            new ThemeOption(@"跟随系统", ApplicationTheme.Unknown),
            new ThemeOption(@"浅色", ApplicationTheme.Light),
            new ThemeOption(@"深色", ApplicationTheme.Dark)
        });

        [ObservableProperty] private ThemeOption _selectedTheme;

        #endregion

        #region Proxy & PAC

        /// <summary>Proxy-rule modes for the Proxy &amp; PAC section combo box.</summary>
        public ObservableCollection<ProxyRuleMode> ProxyRuleModes { get; } =
            new(System.Enum.GetValues<ProxyRuleMode>());

        /// <summary>Second-level proxy types (SOCKS5 / HTTP / TCP tunnel) for the Proxy &amp; PAC combo box.</summary>
        public ObservableCollection<ProxyType> ProxyTypes { get; } =
            new(System.Enum.GetValues<ProxyType>());

        #endregion

        #region Balance

        /// <summary>Load-balancing strategies for the General section combo box.</summary>
        public ObservableCollection<BalanceType> BalanceTypes { get; } =
            new(System.Enum.GetValues<BalanceType>());

        #endregion

        #region DNS

        /// <summary>All configured DNS clients. Currently the page edits the selected one.</summary>
        public ObservableCollection<DnsClient> DnsClients { get; } = new();

        [ObservableProperty] private DnsClient _currentDnsClient;

        /// <summary>Domain queried by the DNS "test" button. Editable so the user can probe any host.</summary>
        [ObservableProperty] private string _dnsTestHost = @"www.google.com";

        /// <summary>Result line shown next to the DNS "test" button (resolved IP or failure message).</summary>
        [ObservableProperty] private string _dnsTestResult = string.Empty;

        /// <summary>DNS type options (Default / DNS-over-TLS) for the radio / combo selection.</summary>
        public ObservableCollection<DnsType> DnsTypes { get; } =
            new(System.Enum.GetValues<DnsType>());

        #endregion

        public SettingsViewModel()
        {
            _selectedTheme = Themes[0];
            Reload();
        }

        /// <summary>Reload the working copy from the on-disk configuration.</summary>
        public void Reload()
        {
            Config = Global.Load();
            AutoStartup = Shadowsocks.Controller.AutoStartup.Check();

            DnsClients.Clear();
            if (Config.DnsClients != null)
            {
                foreach (var client in Config.DnsClients)
                {
                    DnsClients.Add(client);
                }
            }
            CurrentDnsClient = DnsClients.FirstOrDefault();

            // Resync the theme selection with whatever is currently applied (best effort).
            SelectedTheme = Themes[0];
            StatusText = string.Empty;
        }

        partial void OnSelectedThemeChanged(ThemeOption value)
        {
            if (value is null)
            {
                return;
            }

            // Apply the theme immediately for instant feedback. Persistence is not yet wired up.
            // TODO: persist the chosen theme (no theme field exists on Configuration yet).
            var theme = value.Theme == ApplicationTheme.Unknown
                ? ThemeUtil.GetSystemTheme()
                : value.Theme;
            ApplicationThemeManager.Apply(theme);
        }

        [RelayCommand]
        private void AddDns()
        {
            var dns = new DnsClient(DnsType.Default);
            DnsClients.Add(dns);
            CurrentDnsClient = dns;
        }

        [RelayCommand]
        private void DeleteDns()
        {
            if (CurrentDnsClient is null)
            {
                return;
            }

            DnsClients.Remove(CurrentDnsClient);
            CurrentDnsClient = DnsClients.LastOrDefault();
        }

        /// <summary>
        /// Resolve <see cref="DnsTestHost"/> through the currently selected DNS client and
        /// show the resulting IP (or a failure message) in <see cref="DnsTestResult"/>.
        /// </summary>
        [RelayCommand]
        private async Task TestDnsAsync()
        {
            var client = CurrentDnsClient;
            if (client is null)
            {
                DnsTestResult = @"请先选择 DNS 客户端";
                return;
            }

            var host = string.IsNullOrWhiteSpace(DnsTestHost) ? @"www.google.com" : DnsTestHost.Trim();
            DnsTestResult = @"查询中…";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(client.Timeout, 1000)));
                var ip = await client.QueryIpAddressAsync(host, cts.Token);
                DnsTestResult = ip is null ? @"查询失败" : $@"{host} -> {ip}";
            }
            catch (OperationCanceledException)
            {
                DnsTestResult = @"查询超时";
            }
            catch (Exception ex)
            {
                DnsTestResult = $@"查询出错: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Save()
        {
            if (Config is null)
            {
                StatusText = @"配置不可用";
                return;
            }

            // Flush the edited DNS client list back into the working copy before persisting.
            Config.DnsClients = new List<DnsClient>(DnsClients);

            var langChanged = Config.LangName != Global.GuiConfig?.LangName;

            // Persist + reload through the controller, mirroring the old SettingsWindow.SaveConfig.
            if (Global.Controller is not null)
            {
                Global.Controller.SaveServersConfig(Config, true);
            }
            else
            {
                Global.GuiConfig?.CopyFrom(Config);
                Global.SaveConfig();
            }

            // Apply the Windows auto-startup flag if it changed.
            if (AutoStartup != Shadowsocks.Controller.AutoStartup.Check())
            {
                if (!Shadowsocks.Controller.AutoStartup.Set(AutoStartup))
                {
                    StatusText = @"设置开机启动失败";
                    return;
                }
            }

            StatusText = langChanged ? @"已保存，语言更改将在重启后生效" : @"已保存";
        }

        [RelayCommand]
        private void Reset() => Reload();

        [RelayCommand]
        private void ResetReconnect()
        {
            if (Config is null)
            {
                return;
            }

            Config.ReconnectTimes = 4;
            Config.ConnectTimeout = Config.ProxyEnable ? 10 : 5;
            Config.Ttl = 60;
        }
    }

    /// <summary>A selectable theme entry: a localized display name plus the WPF-UI theme to apply.</summary>
    public sealed class ThemeOption
    {
        public ThemeOption(string name, ApplicationTheme theme)
        {
            Name = name;
            Theme = theme;
        }

        public string Name { get; }

        public ApplicationTheme Theme { get; }
    }
}
