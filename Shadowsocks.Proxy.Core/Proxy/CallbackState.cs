namespace Shadowsocks.Proxy.Core.Proxy
{
    public class CallbackState
    {
        public byte[] buffer;
        public int size;
        public int protocol_size;
        public object state;
    }
}
