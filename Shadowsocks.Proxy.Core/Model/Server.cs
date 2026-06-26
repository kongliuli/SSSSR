namespace Shadowsocks.Proxy.Core.Model
{
    /// <summary>
    /// Minimal Server stub for Proxy.Core standalone compilation.
    /// The full Server type lives in the main project (shadowsocks-csharp/Model/Server.cs).
    /// </summary>
    public class Server
    {
        public string server = "";
        public int Server_Port;
        public int Server_Udp_Port;
        public string ProtocolParam = "";
        public string ObfsParam = "";
        public object? ProtocolData;
        public object? ObfsData;
        public string Id = "";
        public string Remarks = "";
        public string Group = "";
        public string Method = "";
        public string Password = "";
        public string Protocol = "";
        public string obfs = "";
        public bool UdpOverTcp;
        public bool Enable = true;
        public DnsBuffer DnsBuffer = new();
        public Connections Connections = new();
        public SpeedLog SpeedLog = new();
    }

    public class DnsBuffer
    {
        public bool force_expired;
        public System.Net.IPAddress? Ip;
        public bool IsExpired(string host) => force_expired;
        public void UpdateDns(string host, System.Net.IPAddress ip) { Ip = ip; force_expired = false; }
        public System.Net.IPAddress? Get(string host) => Ip;
        public void Set(string host, System.Net.IPAddress ip) { Ip = ip; }
        public void Sweep() { }
    }

    public class Connections
    {
        public bool AddRef(object handler) => true;
        public bool DecRef(object handler) => true;
    }

    public class SpeedLog
    {
        public int? ErrorPercent => null;
        public int ConnectError;
        public int ErrorContinuousTimes;
        public int ErrorTimeoutTimes;
        public int ErrorEmptyTimes;
        public void AddConnectTimes() { }
        public void AddDisconnectTimes() { }
        public void AddErrorTimes() { }
        public void AddNoErrorTimes() { }
        public void AddTimeoutTimes() { }
        public void AddErrorDecodeTimes() { }
        public void AddErrorEmptyTimes() { }
        public void ResetErrorDecodeTimes() { }
        public void ResetEmptyTimes() { }
        public void ResetContinuousTimes() { }
        public void AddConnectTime(long pingTime) { }
        public void AddDownloadBytes(int bytes, System.DateTime dt, long size) { }
        public void AddUploadBytes(int bytes, System.DateTime dt, long size) { }
        public void AddDownloadRawBytes(int bytes) { }
    }
}
