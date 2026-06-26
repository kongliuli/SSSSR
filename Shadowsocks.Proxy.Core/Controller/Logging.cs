using System;

namespace Shadowsocks.Proxy.Core.Controller
{
    /// <summary>Stub – logging is wired by the main project.</summary>
    public static class Logging
    {
        public static void Log(Shadowsocks.Proxy.Core.Enums.LogLevel level, string message) { }
        public static void Debug(string msg) { }
        public static void Info(string msg) { }
        public static void LogUsefulException(Exception e) { }
        public static bool LogSocketException(string remarks, string serverUrl, Exception e) => false;
        public static void LogBin(Shadowsocks.Proxy.Core.Enums.LogLevel level, string tag, byte[] data, int length) { }
    }
}
