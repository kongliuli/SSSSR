using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shadowsocks.Model;
using Shadowsocks.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Shadowsocks.ViewModel
{
    /// <summary>
    /// Backing view-model for the modern Fluent subscription-management page.
    /// Wraps each <see cref="ServerSubscribe"/> in a card that exposes friendly display text,
    /// a live node count, an enable toggle (which flips every <see cref="Server.Enable"/> in the
    /// subscription's group) and per-item update/edit/delete commands. Migrated from the legacy
    /// <c>SubscribeWindow</c>.
    /// </summary>
    public partial class SubscriptionsViewModel : ObservableObject
    {
        private readonly Configuration _config;

        /// <summary>Cards currently rendered, one per <see cref="ServerSubscribe"/>.</summary>
        public ObservableCollection<SubscriptionItemViewModel> Subscriptions { get; } = new();

        [ObservableProperty] private bool _isEmpty;

        public SubscriptionsViewModel(Configuration config)
        {
            _config = config;
            Reload();
        }

        /// <summary>Rebuild the card list from <see cref="_config"/>.</summary>
        public void Reload()
        {
            Subscriptions.Clear();

            var config = _config;
            if (config?.ServerSubscribes != null)
            {
                foreach (var sub in config.ServerSubscribes)
                {
                    Subscriptions.Add(new SubscriptionItemViewModel(sub, _config));
                }
            }

            IsEmpty = Subscriptions.Count == 0;
        }

        [RelayCommand]
        private void Add()
        {
            var config = _config;
            if (config?.ServerSubscribes == null)
            {
                return;
            }

            var sub = new ServerSubscribe();
            config.ServerSubscribes.Add(sub);
            Subscriptions.Add(new SubscriptionItemViewModel(sub, _config));
            IsEmpty = false;

            Global.SaveConfig();
        }

        [RelayCommand]
        private void Delete(SubscriptionItemViewModel item)
        {
            if (item is null)
            {
                return;
            }

            var config = _config;
            if (config?.ServerSubscribes == null)
            {
                return;
            }

            var tag = item.Model.Tag;

            config.ServerSubscribes.Remove(item.Model);
            // 同步移除该订阅分组下的所有节点
            config.Configs?.RemoveAll(server => server.SubTag == tag);
            Subscriptions.Remove(item);
            IsEmpty = Subscriptions.Count == 0;

            Global.SaveConfig();
        }

        /// <summary>
        /// Toggle every server belonging to the subscription's group on/off,
        /// mirroring the subscription card's enable switch.
        /// </summary>
        public void SetEnabled(SubscriptionItemViewModel item, bool enabled)
        {
            if (item is null)
            {
                return;
            }

            var config = _config;
            if (config?.Configs == null)
            {
                return;
            }

            var tag = item.Model.Tag;
            foreach (var server in config.Configs.Where(server => server.SubTag == tag))
            {
                server.Enable = enabled;
            }

            Global.SaveConfig();
        }

        [RelayCommand]
        private void UpdateOne(SubscriptionItemViewModel item)
        {
            if (item is null)
            {
                return;
            }

            // 照 MenuViewController / SubscribeWindow 的方式触发单个订阅更新
            _ = Task.Run(() =>
            {
                try
                {
                    Global.UpdateSubscribeManager?.CreateTask(
                        _config,
                        Global.UpdateNodeChecker,
                        true,
                        new List<ServerSubscribe> { item.Model });
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                }
            });
        }

        [RelayCommand]
        private void UpdateAll()
        {
            // 照 MenuViewController.CheckNodeUpdate_Click 的方式触发全部订阅更新
            _ = Task.Run(() =>
            {
                try
                {
                    Global.UpdateSubscribeManager?.CreateTask(
                        _config,
                        Global.UpdateNodeChecker,
                        true);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                }
            });
        }
    }

    /// <summary>
    /// Per-card view-model wrapping a single <see cref="ServerSubscribe"/>.
    /// Exposes editable Name/Url plus derived display values (node count, last-update text)
    /// and forwards the enable toggle to the parent so it can flip the group's servers.
    /// </summary>
    public partial class SubscriptionItemViewModel : ObservableObject
    {
        private readonly Configuration _config;

        public ServerSubscribe Model { get; }

        public SubscriptionItemViewModel(ServerSubscribe model, Configuration config)
        {
            Model = model;
            _config = config;
            _enable = model.Enable;
        }

        /// <summary>订阅名称（分组标签）。</summary>
        public string Name
        {
            get => Model.Tag;
            set
            {
                if (Model.Tag != value)
                {
                    Model.Tag = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>订阅 URL。</summary>
        public string Url
        {
            get => Model.Url;
            set
            {
                if (Model.Url != value)
                {
                    Model.Url = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NodeCountText));
                }
            }
        }

        private bool _enable;

        /// <summary>启用开关。切换会停用/启用该订阅分组下的所有节点并持久化。</summary>
        public bool Enable
        {
            get => _enable;
            set
            {
                if (SetProperty(ref _enable, value))
                {
                    Model.Enable = value;
                    EnableChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>Raised when <see cref="Enable"/> flips so the parent can update grouped servers.</summary>
        public event EventHandler<bool> EnableChanged;

        /// <summary>上次更新时间（本地时间字符串）。</summary>
        public string LastUpdateText
        {
            get
            {
                if (Model.LastUpdateTime == 0)
                {
                    return @"从未更新";
                }

                return DateTimeOffset.FromUnixTimeSeconds(Model.LastUpdateTime).ToLocalTime().ToString();
            }
        }

        /// <summary>该订阅分组下的节点数量。</summary>
        public int NodeCount
        {
            get
            {
                var cfg = _config;
                if (cfg?.Configs == null)
                {
                    return 0;
                }

                var tag = Model.Tag;
                return cfg.Configs.Count(server => server.SubTag == tag);
            }
        }

        public string NodeCountText => $@"{NodeCount} 个节点";

        /// <summary>
        /// 该订阅分组（<see cref="ServerSubscribe.Tag"/> == <see cref="Server.SubTag"/>）下
        /// 所有节点的累计下载/上传流量之和。
        /// </summary>
        private (long down, long up) AggregateTraffic()
        {
            var cfg = _config;
            if (cfg?.Configs == null)
            {
                return (0, 0);
            }

            var tag = Model.Tag;
            long down = 0, up = 0;
            foreach (var server in cfg.Configs.Where(server => server.SubTag == tag))
            {
                var log = server.SpeedLog;
                if (log == null)
                {
                    continue;
                }

                down += log.TotalDownloadBytes;
                up += log.TotalUploadBytes;
            }

            return (down, up);
        }

        /// <summary>该订阅分组的累计流量，格式「↓X ↑Y」。</summary>
        public string TrafficText
        {
            get
            {
                var (down, up) = AggregateTraffic();
                return $@"↓{Utils.FormatBytes(down)}  ↑{Utils.FormatBytes(up)}";
            }
        }

        /// <summary>Refresh derived display values after an external change (e.g. an update finished).</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Url));
            OnPropertyChanged(nameof(LastUpdateText));
            OnPropertyChanged(nameof(NodeCount));
            OnPropertyChanged(nameof(NodeCountText));
            OnPropertyChanged(nameof(TrafficText));
        }
    }
}
