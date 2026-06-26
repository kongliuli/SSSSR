using System;
using System.Security.Cryptography;

namespace Shadowsocks.Encryption;

internal static class RngHelper
{
    public static void RandBytes(byte[] buf, int length = -1)
    {
        RandomNumberGenerator.Fill(length < 0 ? buf : buf.AsSpan(0, length));
    }
}
