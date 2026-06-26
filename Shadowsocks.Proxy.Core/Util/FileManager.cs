using System;
using System.IO;
using System.IO.Compression;

namespace Shadowsocks.Proxy.Core.Controller
{
    public static class FileManager
    {
        public static byte[] DeflateCompress(byte[] content, int index, int count, out int size)
        {
            size = 0;
            try
            {
                var memStream = new MemoryStream();
                using (var ds = new DeflateStream(memStream, CompressionMode.Compress))
                {
                    ds.Write(content, index, count);
                }
                var buffer = memStream.ToArray();
                size = buffer.Length;
                return buffer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught in process: {ex}");
            }
            return null;
        }

        public static byte[] DeflateDecompress(byte[] content, int index, int count, out int size)
        {
            size = 0;
            try
            {
                var buffer = new byte[16384];
                var ds = new DeflateStream(new MemoryStream(content, index, count), CompressionMode.Decompress);
                while (true)
                {
                    var readSize = ds.Read(buffer, size, buffer.Length - size);
                    if (readSize == 0)
                    {
                        break;
                    }
                    size += readSize;
                    var newBuffer = new byte[buffer.Length * 2];
                    buffer.CopyTo(newBuffer, 0);
                    buffer = newBuffer;
                }
                return buffer;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught in process: {ex}");
            }
            return null;
        }
    }
}
