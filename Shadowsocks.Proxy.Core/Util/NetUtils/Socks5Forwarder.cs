using System.Net.Sockets;

namespace Shadowsocks.Proxy.Core.Util.NetUtils
{
    /// <summary>Stub for compilation only.</summary>
    public class Socks5Forwarder
    {
        private readonly object _config;
        private readonly object _ipRange;

        public Socks5Forwarder(object config, object ipRange)
        {
            _config = config;
            _ipRange = ipRange;
        }

        public bool Handle(byte[] buffer, int length, Socket socket, string? protocol)
        {
            return false;
        }
    }
}
