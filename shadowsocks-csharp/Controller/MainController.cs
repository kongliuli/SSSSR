using Shadowsocks.Controller.HttpRequest;
using Shadowsocks.Controller.Service;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Model.Transfer;
using Shadowsocks.Services;
using Shadowsocks.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;

namespace Shadowsocks.Controller
{
    public class MainController
    {
        // controller:
        // handle user actions
        // manipulates UI
        // interacts with low level logic

        private readonly Configuration _config;
        private readonly UpdateNode _updateNodeChecker;
        private readonly UpdateSubscribeManager _updateSubscribeManager;
        private readonly IConfigPersistenceService _configPersistence;
        private Listener _listener;
        private List<Listener> _portMapListener;
        private PACDaemon _pacDaemon;
        private PACServer _pacServer;

        private readonly ServerTransferTotal _transfer;
        private HostDaemon _hostDaemon;
        private IPRangeSet _chnRangeSet;
        private HttpProxyRunner _httpProxyRunner;
        private GfwListUpdater _gfwListUpdater;
        private bool _stopped;

        public class PathEventArgs : EventArgs
        {
            public string Path;
        }

        #region Event

        public event EventHandler ConfigChanged;
        public event EventHandler ShowConfigFormEvent;
        public event EventHandler ShowSubscribeWindowEvent;

        // when user clicked Edit PAC, and PAC file has already created
        public event EventHandler<PathEventArgs> PACFileReadyToOpen;
        public event EventHandler<PathEventArgs> UserRuleFileReadyToOpen;

        public event EventHandler<GfwListUpdater.ResultEventArgs> UpdatePACFromGFWListCompleted;

        public event ErrorEventHandler UpdatePACFromGFWListError;

        public event ErrorEventHandler Errored;

        #endregion

        /// <summary>Expose the live configuration singleton for consumers that need
        /// read-only access (e.g. to compare a working copy against the live state).</summary>
        public Configuration LiveConfig => _config;

        public MainController(Configuration config, UpdateNode updateNodeChecker, UpdateSubscribeManager updateSubscribeManager, IConfigPersistenceService configPersistence)
        {
            _config = config;
            _updateNodeChecker = updateNodeChecker;
            _updateSubscribeManager = updateSubscribeManager;
            _configPersistence = configPersistence;
            _transfer = ServerTransferTotal.Load();

            foreach (var server in _config.Configs)
            {
                if (_transfer.Servers.TryGetValue(server.Id, out var st))
                {
                    var log = new ServerSpeedLog(st.TotalUploadBytes, st.TotalDownloadBytes);
                    server.SpeedLog = log;
                }
            }
        }

        private void ReportError(Exception e)
        {
            Errored?.Invoke(this, new ErrorEventArgs(e));
        }

        private static int FindFirstMatchServer(Server server, IReadOnlyList<Server> servers)
        {
            for (var i = 0; i < servers.Count; ++i)
            {
                if (server.IsMatchServer(servers[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        private static void AppendConfiguration(Configuration mergeConfig, IReadOnlyList<Server> servers)
        {
            if (servers != null)
            {
                _ = Application.Current.Dispatcher?.InvokeAsync(() =>
                {
                    foreach (var server in servers)
                    {
                        if (FindFirstMatchServer(server, mergeConfig.Configs) == -1)
                        {
                            mergeConfig.Configs.Add(server);
                        }
                    }
                });
            }
        }

        private static IEnumerable<Server> MergeConfiguration(Configuration mergeConfig, IReadOnlyList<Server> servers)
        {
            if (servers != null)
            {
                foreach (var server in servers)
                {
                    var i = FindFirstMatchServer(server, mergeConfig.Configs);
                    if (i != -1)
                    {
                        var enable = server.Enable;
                        server.CopyServer(mergeConfig.Configs[i]);
                        server.Enable = enable;
                    }
                }
            }

            return from t in mergeConfig.Configs let j = FindFirstMatchServer(t, servers) where j == -1 select t;
        }

        private Configuration MergeGetConfiguration(Configuration mergeConfig)
        {
            var ret = _configPersistence.Load();
            if (mergeConfig != null)
            {
                MergeConfiguration(mergeConfig, ret.Configs);
            }
            return ret;
        }

        /// <summary>
        /// 从配置文件导入服务器
        /// </summary>
        /// <param name="mergeConfig"></param>
        public void MergeConfiguration(Configuration mergeConfig)
        {
            AppendConfiguration(_config, mergeConfig.Configs);
            SaveAndReload();
        }

        public void SaveServersConfig(Configuration config, bool reload)
        {
            var missingServers = MergeConfiguration(_config, config.Configs);
            _config.CopyFrom(config);
            foreach (var s in missingServers)
            {
                s.Connections.CloseAll();
            }

            if (reload)
            {
                SaveAndReload();
            }
            else
            {
                SaveAndNotifyChanged();
            }
        }

        public void SaveServersPortMap(Configuration config)
        {
            StopPortMap();
            _config.PortMap = config.PortMap;
            _config.FlushPortMapCache();
            LoadPortMap();
            SaveAndNotifyChanged();
        }

        /// <summary>
        /// 选择指定服务器
        /// </summary>
        public void SelectServerIndex(int index)
        {
            _config.Index = index;
            SaveAndNotifyChanged();
        }

        /// <summary>
        /// 导入服务器链接
        /// </summary>
        public bool AddServerBySsUrl(string ssUrLs, string force_group = null, bool toLast = false)
        {
            try
            {
                var urls = ssUrLs.GetLines().Reverse();
                var i = 0;
                foreach (var url in urls.Select(url => url.Trim('/')).Where(url => url.StartsWith(@"ss://", StringComparison.OrdinalIgnoreCase) || url.StartsWith(@"ssr://", StringComparison.OrdinalIgnoreCase)))
                {
                    ++i;
                    var server = new Server(url, force_group);
                    if (toLast)
                    {
                        _config.Configs.Add(server);
                    }
                    else
                    {
                        var index = _config.Index + 1;
                        if (index < 0 || index > _config.Configs.Count)
                        {
                            index = _config.Configs.Count;
                        }

                        _config.Configs.Insert(index, server);
                    }
                }
                if (i > 0)
                {
                    SaveAndReload();
                    return true;
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                return false;
            }
            return false;
        }

        /// <summary>
        /// 导入订阅链接
        /// </summary>
        public bool AddSubscribeUrl(string str)
        {
            try
            {
                var urls = str.GetLines();
                var newSubscribes = new List<ServerSubscribe>();
                var existSubscribes = new List<ServerSubscribe>();
                foreach (var url in urls.Where(url => url.StartsWith(@"sub://", StringComparison.OrdinalIgnoreCase)))
                {
                    var sub = Regex.Match(url, "sub://([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
                    if (sub.Success)
                    {
                        var res = Base64.DecodeUrlSafeBase64(sub.Groups[1].Value);
                        if (_config.ServerSubscribes.All(serverSubscribe => serverSubscribe.Url != res))
                        {
                            var newSub = new ServerSubscribe { Url = res };
                            newSubscribes.Add(newSub);
                            _config.ServerSubscribes.Add(newSub);
                        }
                        else
                        {
                            existSubscribes.Add(_config.ServerSubscribes.Find(serverSubscribe => serverSubscribe.Url == res));
                        }
                    }
                }
                if (newSubscribes.Count > 0)
                {
                    SaveAndNotifyChanged();
                    _updateSubscribeManager.CreateTask(_config, _updateNodeChecker, true, newSubscribes);
                    return true;
                }
                if (existSubscribes.Count > 0)
                {
                    _updateSubscribeManager.CreateTask(_config, _updateNodeChecker, true, existSubscribes);
                    return false;
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                return false;
            }
            return false;
        }

        /// <summary>
        /// 切换系统代理模式
        /// </summary>
        public void ToggleMode(ProxyMode mode)
        {
            ProxyMode oldMode = _config.SysProxyMode;
            _config.SysProxyMode = mode;
            ReloadPacServer();
            if (oldMode is not ProxyMode.NoModify && mode is ProxyMode.NoModify)
            {
                SystemProxy.Restore();
            }
            else
            {
                UpdateSystemProxy();
            }
            SaveAndNotifyChanged();
        }

        /// <summary>
        /// 切换代理规则
        /// </summary>
        /// <param name="mode"></param>
        public void ToggleRuleMode(ProxyRuleMode mode)
        {
            _config.ProxyRuleMode = mode;
            SaveAndNotifyChanged();
        }

        public void ToggleSelectRandom(bool enabled)
        {
            _config.Random = enabled;
            if (!enabled)
            {
                DisconnectAllConnections(true);
            }
            SaveAndNotifyChanged();
        }

        public void ToggleSameHostForSameTargetRandom(bool enabled)
        {
            _config.SameHostForSameTarget = enabled;
            SaveAndNotifyChanged();
        }

        public void ToggleSelectAutoCheckUpdate(bool enabled)
        {
            _config.AutoCheckUpdate = enabled;
            _configPersistence.Save(_config);
        }

        public void ToggleSelectAllowPreRelease(bool enabled)
        {
            _config.IsPreRelease = enabled;
            _configPersistence.Save(_config);
        }

        /// <summary>
        /// 保存配置文件并通知配置改变
        /// </summary>
        public void SaveAndNotifyChanged()
        {
            _configPersistence.Save(_config);
            _ = Application.Current.Dispatcher?.InvokeAsync(() => { ConfigChanged?.Invoke(this, EventArgs.Empty); });
        }

        /// <summary>
        /// 保存配置文件并重载
        /// </summary>
        private void SaveAndReload()
        {
            _configPersistence.Save(_config);
            Reload();
        }

        private void StopPortMap()
        {
            if (_portMapListener != null)
            {
                foreach (var l in _portMapListener)
                {
                    l.Stop();
                }

                _portMapListener = null;
            }
        }

        private void LoadPortMap()
        {
            _portMapListener = new List<Listener>();
            foreach (var pair in _config.PortMapCache)
            {
                try
                {
                    var local = new Local(_config, _transfer, _chnRangeSet);
                    var services = new List<Listener.IService> { local };
                    var listener = new Listener(services);
                    listener.Start(_config, pair.Key);
                    _portMapListener.Add(listener);
                }
                catch (Exception e)
                {
                    ThrowSocketException(ref e);
                    Logging.LogUsefulException(e);
                    ReportError(e);
                }
            }
        }

        public void Stop()
        {
            if (_stopped)
            {
                return;
            }
            _stopped = true;

            StopPortMap();

            _listener?.Stop();
            _httpProxyRunner?.Stop();
            if (_config.SysProxyMode is not ProxyMode.NoModify)
            {
                SystemProxy.Restore();
            }
            ServerTransferTotal.Save(_transfer, _config.Configs);
        }

        public void ClearTransferTotal(string serverId)
        {
            _transfer.Clear(serverId);
            var server = _config.Configs.Find(s => s.Id == serverId);
            server?.SpeedLog.ClearTrans();
        }

        public void TouchPACFile()
        {
            PACFileReadyToOpen?.Invoke(this, new PathEventArgs { Path = _pacDaemon.TouchPACFile() });
        }

        public void TouchUserRuleFile()
        {
            UserRuleFileReadyToOpen?.Invoke(this, new PathEventArgs { Path = _pacDaemon.TouchUserRuleFile() });
        }

        public void UpdatePACFromGFWList()
        {
            _ = _gfwListUpdater?.UpdatePacFromGfwList(_config);
        }

        public void UpdatePACFromOnlinePac(string url)
        {
            _ = _gfwListUpdater?.UpdateOnlinePac(_config, url);
        }

        private void ReloadPacServer()
        {
            if (_pacDaemon == null)
            {
                _pacDaemon = new PACDaemon();
                _pacDaemon.PACFileChanged += (o, args) =>
                {
                    _pacServer?.UpdatePacUrl(_config);
                    UpdateSystemProxy();
                };
                _pacDaemon.UserRuleFileChanged += PacDaemon_UserRuleFileChanged;
            }

            if (_pacServer == null)
            {
                _pacServer = new PACServer(_pacDaemon);
            }

            _pacServer.UpdatePacUrl(_config);
        }

        private void ReloadIPRange()
        {
            _chnRangeSet = new IPRangeSet(_config.ProxyRuleMode);
            _chnRangeSet.LoadChn();
        }

        private void ReloadProxyRule()
        {
            if (_hostDaemon == null)
            {
                _hostDaemon = new HostDaemon();
                _hostDaemon.ChnIpChanged += (o, args) => ReloadIPRange();
                _hostDaemon.UserRuleChanged += (o, args) => HostMap.Reload();
            }

            ReloadIPRange();
            HostMap.Reload();
        }

        public void Reload()
        {
            StopPortMap();
            // some logic in configuration updated the config when saving, we need to read it again
            // The DI singleton is the authoritative in-memory copy; refresh from disk and
            // overlay in-memory server runtime state (SpeedLog, Connections, etc.).
            var reloaded = MergeGetConfiguration(_config);
            if (reloaded != null)
            {
                _config.CopyFrom(reloaded);
            }
            _config.FlushPortMapCache();
            Logging.SaveToFile = _config.LogEnable;
            Logging.OpenLogFile();

            ReloadProxyRule();

            _httpProxyRunner ??= new HttpProxyRunner();
            ReloadPacServer();
            if (_gfwListUpdater == null)
            {
                _gfwListUpdater = new GfwListUpdater();
                _gfwListUpdater.UpdateCompleted += (o, args) => UpdatePACFromGFWListCompleted?.Invoke(o, args);
                _gfwListUpdater.Error += (o, args) => UpdatePACFromGFWListError?.Invoke(o, args);
            }

            _listener?.Stop();
            _httpProxyRunner.Stop();
            try
            {
                _httpProxyRunner.Start(_config);

                var local = new Local(_config, _transfer, _chnRangeSet);
                var services = new List<Listener.IService>
                {
                    local,
                    _pacServer,
                    new HttpPortForwarder(_httpProxyRunner.RunningPort, _config)
                };
                _listener = new Listener(services);
                _listener.Start(_config, 0);
            }
            catch (Exception e)
            {
                ThrowSocketException(ref e);
                Logging.LogUsefulException(e);
                ReportError(e);
            }

            LoadPortMap();

            _ = Application.Current.Dispatcher?.InvokeAsync(() => { ConfigChanged?.Invoke(this, EventArgs.Empty); });

            UpdateSystemProxy();
        }

        private static readonly Dictionary<SocketError, string> ErrorMessages = new()
        {
            [SocketError.AddressAlreadyInUse] = "端口已被占用",
            [SocketError.ConnectionRefused] = "连接被拒绝",
            [SocketError.NetworkUnreachable] = "网络不可达",
            [SocketError.HostUnreachable] = "主机不可达",
            [SocketError.ConnectionReset] = "连接被重置",
            [SocketError.TimedOut] = "连接超时",
            [SocketError.AccessDenied] = "权限不足，请以管理员身份运行",
            [SocketError.NotConnected] = "未连接到服务器",
            [SocketError.Shutdown] = "连接已关闭",
        };

        private void ThrowSocketException(ref Exception e)
        {
            if (e is not SocketException se)
            {
                return;
            }

            if (ErrorMessages.TryGetValue(se.SocketErrorCode, out var translation))
            {
                e = new Exception($"{se.Message} ({translation})", se);
            }
        }

        private void UpdateSystemProxy()
        {
            SystemProxy.Update(_config, _pacServer);
        }

        private void PacDaemon_UserRuleFileChanged(object sender, EventArgs e)
        {
            if (!Utils.IsGFWListPAC(PACDaemon.PAC_FILE))
            {
                return;
            }
            if (!File.Exists(Utils.GetTempPath(PACServer.gfwlist_FILE)))
            {
                UpdatePACFromGFWList();
            }
            else
            {
                GfwListUpdater.MergeAndWritePacFile(FileManager.NonExclusiveReadAllText(Utils.GetTempPath(PACServer.gfwlist_FILE)));
            }

            UpdateSystemProxy();
        }

        public void ShowConfigForm(int? index = null)
        {
            ShowConfigFormEvent?.Invoke(index, EventArgs.Empty);
        }

        public void ShowSubscribeWindow()
        {
            ShowSubscribeWindowEvent?.Invoke(default, EventArgs.Empty);
        }

        /// <summary>
        /// Disconnect all connections from the remote host.
        /// </summary>
        public void DisconnectAllConnections(bool checkSwitchAutoCloseAll = false)
        {
            var config = _config;
            if (checkSwitchAutoCloseAll && !config.CheckSwitchAutoCloseAll)
            {
                Console.WriteLine(@"config.checkSwitchAutoCloseAll:False");
                return;
            }
            foreach (var server in config.Configs)
            {
                server.Connections.CloseAll();
            }
        }

        public void CopyPacUrl()
        {
            Clipboard.SetDataObject(_pacServer.PacUrl);
        }
    }
}
