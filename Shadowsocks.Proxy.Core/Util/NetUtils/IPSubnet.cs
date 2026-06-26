using System.Net;

namespace Shadowsocks.Proxy.Core.Util.NetUtils
{
    /// <summary>Stub for compilation only.</summary>
    public static class IPSubnet
    {
        public static bool IsLoopBack(IPAddress addr) => IPAddress.IsLoopback(addr);
    }
}
