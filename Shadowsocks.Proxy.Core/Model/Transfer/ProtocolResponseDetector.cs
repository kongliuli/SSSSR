namespace Shadowsocks.Proxy.Core.Model.Transfer
{
    /// <summary>Stub for compilation only.</summary>
    public class ProtocolResponseDetector
    {
        public bool Pass => true;
        public void OnSend(byte[] buffer, int length) { }
        public int OnRecv(byte[] buffer, int length) => 0;
    }
}
