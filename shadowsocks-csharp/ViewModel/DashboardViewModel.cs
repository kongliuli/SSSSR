using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Shadowsocks.ViewModel
{
    /// <summary>
    /// Backing view-model for the dashboard home page: live connection status, real-time
    /// up/down speed chart, traffic totals and quick proxy-mode switches.
    /// </summary>
    public partial class DashboardViewModel : ObservableObject
    {
        private const int MaxPoints = 60;

        private readonly ObservableCollection<double> _downValues = new();
        private readonly ObservableCollection<double> _upValues = new();
        private readonly DispatcherTimer _timer;

        [ObservableProperty] private string _statusText = string.Empty;
        [ObservableProperty] private string _currentNodeName = string.Empty;
        [ObservableProperty] private string _downSpeedText = @"0 B/s";
        [ObservableProperty] private string _upSpeedText = @"0 B/s";
        [ObservableProperty] private string _totalDownText = @"0 B";
        [ObservableProperty] private string _totalUpText = @"0 B";
        [ObservableProperty] private bool _isDirect;
        [ObservableProperty] private bool _isPac;
        [ObservableProperty] private bool _isGlobal;

        /// <summary>Two-line live speed series (download / upload) for the LiveCharts chart.</summary>
        public ISeries[] SpeedSeries { get; }

        public DashboardViewModel()
        {
            SpeedSeries = new ISeries[]
            {
                new LineSeries<double> { Name = @"下行", Values = _downValues, GeometrySize = 0, LineSmoothness = 0.6 },
                new LineSeries<double> { Name = @"上行", Values = _upValues, GeometrySize = 0, LineSmoothness = 0.6 },
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Refresh();
        }

        /// <summary>Begin polling live stats. Call when the page is shown.</summary>
        public void Start()
        {
            Refresh();
            _timer.Start();
        }

        /// <summary>Stop polling. Call when the page is hidden to avoid background work.</summary>
        public void Stop() => _timer.Stop();

        private void Refresh()
        {
            var config = Global.GuiConfig;
            if (config is null)
            {
                return;
            }

            IsDirect = config.SysProxyMode == ProxyMode.Direct;
            IsPac = config.SysProxyMode == ProxyMode.Pac;
            IsGlobal = config.SysProxyMode == ProxyMode.Global;

            var enabled = config.SysProxyMode is not ProxyMode.NoModify and not ProxyMode.Direct;
            StatusText = enabled ? @"已连接" : @"未启用系统代理";

            var server = GetCurrentServer(config);
            if (server is null)
            {
                CurrentNodeName = @"无节点";
                return;
            }

            CurrentNodeName = server.FriendlyName;

            var log = server.SpeedLog;
            if (log is null)
            {
                return;
            }

            DownSpeedText = log.AvgDownloadBytesText;
            UpSpeedText = log.AvgUploadBytesText;
            TotalDownText = log.TotalDownloadBytesText;
            TotalUpText = log.TotalUploadBytesText;

            Append(_downValues, log.AvgDownloadBytes);
            Append(_upValues, log.AvgUploadBytes);
        }

        private static void Append(ObservableCollection<double> values, double value)
        {
            values.Add(value);
            while (values.Count > MaxPoints)
            {
                values.RemoveAt(0);
            }
        }

        private static Server GetCurrentServer(Configuration config)
        {
            if (config.Configs is null || config.Configs.Count == 0)
            {
                return null;
            }

            var index = config.Index;
            if (index < 0 || index >= config.Configs.Count)
            {
                index = 0;
            }

            return config.Configs[index];
        }

        [RelayCommand]
        private void SetModeDirect() => _ = Task.Run(() => Global.Controller?.ToggleMode(ProxyMode.Direct));

        [RelayCommand]
        private void SetModePac() => _ = Task.Run(() => Global.Controller?.ToggleMode(ProxyMode.Pac));

        [RelayCommand]
        private void SetModeGlobal() => _ = Task.Run(() => Global.Controller?.ToggleMode(ProxyMode.Global));
    }
}
