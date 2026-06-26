using System;

namespace Shadowsocks.Proxy.Core.Model.Transfer
{
    /// <summary>Stub for compilation only.</summary>
    public class SpeedTester
    {
        public ServerTransferTotal? Transfer { get; set; }
        public string ServerId = "";

        public DateTime TimeBeginDownload;
        public DateTime TimeBeginUpload;

        public long SizeDownload => 0;
        public long SizeUpload => 0;
        public long SizeRecv => 0;
        public long SizeProtocolRecv => 0;

        public void BeginConnect() { }
        public void EndConnect() { }
        public void BeginUpload() { }
        public bool BeginDownload() => true;
        public long AddDownloadSize(long size) => 0;
        public long AddUploadSize(long size) => 0;
        public void AddRecvSize(long size) { }
        public void AddProtocolRecvSize(long size) { }
        public string TransferLog() => "";
    }
}
