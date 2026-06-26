using Shadowsocks.Proxy.Core.Model;

namespace Shadowsocks.Proxy.Core.Util.NetUtils
{
    /// <summary>Stub for compilation only.</summary>
    public static class DnsUtil
    {
        public static readonly DnsBuffer DnsBuffer = new();

        public static System.Threading.Tasks.Task<System.Net.IPAddress?> QueryDnsAsync(
            string host, System.Collections.Generic.IReadOnlyList<DnsClient>? clients)
        {
            return System.Threading.Tasks.Task.FromResult<System.Net.IPAddress?>(null);
        }
    }
}
