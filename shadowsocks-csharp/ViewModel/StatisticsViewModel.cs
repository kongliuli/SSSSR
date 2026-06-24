using CommunityToolkit.Mvvm.ComponentModel;
using Shadowsocks.Model;
using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace Shadowsocks.ViewModel
{
    /// <summary>
    /// Backing view-model for the connection statistics page: shows a live per-node
    /// statistics table (latency, up/down speed, connections, error rate, totals, group)
    /// migrated from the legacy <c>ServerLogWindow</c>.
    /// </summary>
    public partial class StatisticsViewModel : ObservableObject
    {
        private readonly Configuration _config;
        private readonly DispatcherTimer _timer;

        /// <summary>Servers shown in the grid. Each <see cref="Server"/> already raises
        /// change notifications for its own <c>SpeedLog</c> properties; the timer below is
        /// only used to keep the collection in sync with the live configuration.</summary>
        public ObservableCollection<Server> Servers { get; } = new();

        public StatisticsViewModel(Configuration config)
        {
            _config = config;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Refresh();
        }

        /// <summary>Begin syncing the server list. Call when the page is shown.</summary>
        public void Start()
        {
            Refresh();
            _timer.Start();
        }

        /// <summary>Stop syncing. Call when the page is hidden to avoid background work.</summary>
        public void Stop() => _timer.Stop();

        private void Refresh()
        {
            var config = _config;
            var configs = config?.Configs;
            if (configs is null)
            {
                if (Servers.Count > 0)
                {
                    Servers.Clear();
                }
                return;
            }

            // Rebuild only when the underlying set of servers changed (added / removed /
            // reordered). Speed columns refresh on their own via the Server property
            // change notifications, so we avoid churning the collection every tick.
            var changed = Servers.Count != configs.Count;
            if (!changed)
            {
                for (var i = 0; i < configs.Count; ++i)
                {
                    if (!ReferenceEquals(Servers[i], configs[i]))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
            {
                return;
            }

            Servers.Clear();
            var index = 1;
            foreach (var server in configs)
            {
                server.Index = index;
                server.IsSelected = config.Index == index - 1;
                ++index;
                Servers.Add(server);
            }
        }
    }
}
