using Shadowsocks.Proxy.Core.Controller.Service;
using Shadowsocks.Proxy.Core.Enums;
using Shadowsocks.Proxy.Core.Util.NetUtils;
using System.Collections.Generic;

namespace Shadowsocks.Proxy.Core.Model
{
    /// <summary>Stub for compilation only. Full Configuration lives in main project.</summary>
    public class Configuration
    {
        public string AuthUser = "";
        public string AuthPass = "";
        public bool ProxyEnable;
        public ProxyType ProxyType;
        public string ProxyHost = "";
        public int ProxyPort;
        public string ProxyAuthUser = "";
        public string ProxyAuthPass = "";
        public string ProxyUserAgent = "";
        public double Ttl;
        public double ConnectTimeout;
        public bool AutoBan;
        public int ReconnectTimes;
        public bool Random;
        public ProxyRuleMode ProxyRuleMode;
        public IReadOnlyList<DnsClient> DnsClients = new List<DnsClient>();
        public Dictionary<int, PortMapConfig> PortMapCache = new();

        public Server GetCurrentServer(int localPort, ServerSelectStrategy.FilterFunc? filter,
            string? targetURI, bool cfgRandom, bool usingRandom, bool forceRandom = false)
        {
            return new Server();
        }

        public void KeepCurrentServer(int localPort, string? targetURI, string id) { }
    }

    public class PortMapConfig
    {
        public PortMapType type;
        public Server? server;
        public string id = "";
        public string server_addr = "";
        public int server_port;
    }
}
