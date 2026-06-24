using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shadowsocks.Controller;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Shadowsocks.ViewModel
{
    /// <summary>
    /// Backing view-model for the port-forwarding / port-mapping page. Presents the
    /// <see cref="Configuration.PortMap"/> dictionary (keyed by local port) as an editable
    /// row collection, with add / delete / save commands. Migrated from the legacy
    /// code-behind <c>PortSettingsWindow</c>.
    /// </summary>
    public partial class PortForwardingViewModel : ObservableObject
    {
        private readonly MainController _controller;

        /// <summary>Editable rules shown in the grid.</summary>
        public ObservableCollection<PortMapRow> Rules { get; } = new();

        /// <summary>All selectable mapping types for the Type column ComboBox.</summary>
        public IReadOnlyList<PortMapType> Types { get; } =
            new[] { PortMapType.Forward, PortMapType.ForceProxy, PortMapType.RuleProxy };

        /// <summary>
        /// Server picks for the "server / id" column. The empty entry means "any / group".
        /// Each row exposes <see cref="ServerOption.Id"/> as the stored value and a friendly
        /// display name. Groups are also offered (id == group name) to match the legacy window.
        /// </summary>
        public ObservableCollection<ServerOption> ServerOptions { get; } = new();

        [ObservableProperty] private PortMapRow _selectedRule;
        [ObservableProperty] private string _statusText = string.Empty;

        public PortForwardingViewModel(MainController controller)
        {
            _controller = controller;
            Load();
        }

        /// <summary>(Re)load rules and the server pick list from the on-disk configuration.</summary>
        public void Load()
        {
            var config = Global.Load();

            ServerOptions.Clear();
            ServerOptions.Add(new ServerOption(string.Empty, @"（任意 / 分组）"));
            var seenGroups = new HashSet<string>();
            foreach (var s in config.Configs)
            {
                if (!string.IsNullOrEmpty(s.Group) && seenGroups.Add(s.Group))
                {
                    // Group entries store the group name as id, matching legacy behavior.
                    ServerOptions.Add(new ServerOption(s.Group, $@"#分组 {s.Group}"));
                }
            }
            foreach (var s in config.Configs)
            {
                ServerOptions.Add(new ServerOption(s.Id, GetDisplayText(s)));
            }

            Rules.Clear();
            foreach (var pair in config.PortMap.OrderBy(p => ParsePort(p.Key)))
            {
                Rules.Add(new PortMapRow(ParsePort(pair.Key), pair.Value));
            }

            StatusText = $@"已加载 {Rules.Count} 条规则";
        }

        [RelayCommand]
        private void Add()
        {
            var row = new PortMapRow(0, new PortMapConfig { Enable = true, Type = PortMapType.Forward });
            Rules.Add(row);
            SelectedRule = row;
            StatusText = @"已添加新规则，请填写本地端口后保存";
        }

        [RelayCommand]
        private void Delete()
        {
            if (SelectedRule is null)
            {
                StatusText = @"请先选择要删除的规则";
                return;
            }

            Rules.Remove(SelectedRule);
            SelectedRule = null;
            StatusText = @"已删除选中规则（保存后生效）";
        }

        [RelayCommand]
        private void Save()
        {
            // Validate every row before persisting anything: local ports must be in range
            // 1-65535 and unique. On the first offending row we report which line failed via
            // StatusText and abort, rather than silently dropping invalid/duplicate rows.
            var portMap = new Dictionary<string, PortMapConfig>();
            var seenPorts = new HashSet<int>();
            for (var i = 0; i < Rules.Count; i++)
            {
                var row = Rules[i];
                var lineNo = i + 1;

                if (row.LocalPort < 1 || row.LocalPort > 65535)
                {
                    SelectedRule = row;
                    StatusText = $@"第 {lineNo} 行本地端口非法（需为 1-65535），未保存";
                    return;
                }

                if (!seenPorts.Add(row.LocalPort))
                {
                    SelectedRule = row;
                    StatusText = $@"第 {lineNo} 行本地端口 {row.LocalPort} 重复，未保存";
                    return;
                }

                portMap[row.LocalPort.ToString()] = row.ToConfig();
            }

            // Clone the persisted configuration (like the legacy window) and swap in the new
            // port map, then hand it to the controller which restarts the port-map listeners.
            var config = Global.Load();
            config.PortMap = portMap;

            _controller.SaveServersPortMap(config);
            StatusText = $@"已保存 {portMap.Count} 条规则";

            Load();
        }

        private static int ParsePort(string key) => int.TryParse(key, out var p) ? p : 0;

        private static string GetDisplayText(Server s)
            => (!string.IsNullOrEmpty(s.Group) ? s.Group + @" - " : @"    - ") + s.FriendlyName + @"        #" + s.Id;
    }

    /// <summary>
    /// One selectable server (or group) for the row's server/id column.
    /// <see cref="Id"/> is the stored value; <see cref="Display"/> is what the user sees.
    /// </summary>
    public sealed class ServerOption
    {
        public ServerOption(string id, string display)
        {
            Id = id;
            Display = display;
        }

        public string Id { get; }
        public string Display { get; }
    }

    /// <summary>
    /// Editable grid row wrapping a single <see cref="PortMapConfig"/> together with its local
    /// port (the dictionary key in <see cref="Configuration.PortMap"/>).
    /// </summary>
    public partial class PortMapRow : ObservableObject
    {
        [ObservableProperty] private int _localPort;
        [ObservableProperty] private bool _enable;
        [ObservableProperty] private PortMapType _type;
        [ObservableProperty] private string _id;
        [ObservableProperty] private string _serverAddr;
        [ObservableProperty] private int _serverPort;
        [ObservableProperty] private string _remarks;

        public PortMapRow(int localPort, PortMapConfig config)
        {
            _localPort = localPort;
            _enable = config.Enable;
            _type = config.Type;
            _id = config.Id;
            _serverAddr = config.Server_addr;
            _serverPort = config.Server_port;
            _remarks = config.Remarks;
        }

        /// <summary>Materialize the edited values back into a <see cref="PortMapConfig"/>.</summary>
        public PortMapConfig ToConfig() => new()
        {
            Enable = Enable,
            Type = Type,
            Id = Id,
            Server_addr = ServerAddr,
            Server_port = ServerPort,
            Remarks = Remarks
        };
    }
}
