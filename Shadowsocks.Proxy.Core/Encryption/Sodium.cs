using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Shadowsocks.Encryption
{
    public static class Sodium
    {
        private const string DLLNAME = @"libsscrypto.dll";

        private static readonly bool _initialized;
        private static readonly object _initLock = new();

        /// <summary>
        /// Set this before using any Sodium methods. The byte arrays are the
        /// embedded native DLL resources (libsscrypto.dll / libsscrypto64.dll).
        /// When null (standalone library), native methods will fail at runtime.
        /// </summary>
        public static byte[]? LibsscryptoDllBytes { get; set; }
        public static byte[]? Libsscrypto64DllBytes { get; set; }

        static Sodium()
        {
            var dllBytes = Environment.Is64BitProcess ? Libsscrypto64DllBytes : LibsscryptoDllBytes;
            if (dllBytes == null)
                return;

            var dllPath = Path.Combine(Path.GetTempPath(), DLLNAME);
            try
            {
                File.WriteAllBytes(dllPath, dllBytes);
                LoadLibrary(dllPath);
            }
            catch (IOException)
            {
            }
            catch (System.Exception e)
            {
                Debug.WriteLine(e);
            }

            lock (_initLock)
            {
                if (!_initialized)
                {
                    if (sodium_init() == -1)
                    {
                        throw new System.Exception(@"Failed to initialize sodium");
                    }

                    _initialized = true;
                }
            }
        }

        [DllImport(@"Kernel32.dll")]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sodium_init();

        #region Stream

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int crypto_stream_salsa20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int crypto_stream_xsalsa20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int crypto_stream_chacha20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int crypto_stream_xchacha20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

        [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int crypto_stream_chacha20_ietf_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, uint ic, byte[] k);

        #endregion
    }
}
