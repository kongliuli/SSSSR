using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Shadowsocks.Encryption;

namespace Shadowsocks.Proxy.Core.Tests.Encryption;

[TestClass]
public class EncryptorRoundTripTests
{
    private const string TestPassword = "test-password-12345";

    private static readonly string[] MustHaveCiphers =
    [
        // NoneEncryptor
        "none",
        // StreamOpenSSLEncryptor (commonly used)
        "aes-256-cfb",
        "aes-128-ctr",
        "aes-256-ctr",
        "aes-128-cfb",
        "rc4-md5",
        "rc4",
        "bf-cfb",
        // StreamSodiumEncryptor
        "salsa20",
        "chacha20",
        "chacha20-ietf",
        "xsalsa20",
        "xchacha20"
    ];

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    static EncryptorRoundTripTests()
    {
        LoadNativeDll();
    }

    private static void LoadNativeDll()
    {
        string dllFileName = Environment.Is64BitProcess ? "libsscrypto64.dll.gz" : "libsscrypto.dll.gz";
        const string dllName = "libsscrypto.dll";

        // Locate gzip resource
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "shadowsocks-csharp", "Data", dllFileName));

        byte[]? dllBytes = TryDecompress(candidate);

        if (dllBytes == null)
        {
            candidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "shadowsocks-csharp", "Data", dllFileName));
            dllBytes = TryDecompress(candidate);
        }

        if (dllBytes == null)
        {
            candidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "shadowsocks-csharp", "Data", dllFileName));
            dllBytes = TryDecompress(candidate);
        }

        // Set properties in case the static constructors haven't fired yet
        Shadowsocks.Encryption.OpenSSL.LibsscryptoDllBytes = dllBytes;
        Shadowsocks.Encryption.OpenSSL.Libsscrypto64DllBytes = dllBytes;
        Shadowsocks.Encryption.Sodium.LibsscryptoDllBytes = dllBytes;
        Shadowsocks.Encryption.Sodium.Libsscrypto64DllBytes = dllBytes;

        // Directly extract and load the native DLL, bypassing static constructors
        // (which may have already run with null bytes)
        if (dllBytes != null)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), dllName);
            try
            {
                File.WriteAllBytes(tempPath, dllBytes);
                var handle = LoadLibrary(tempPath);
                if (handle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[EncryptorTests] LoadLibrary failed: {Marshal.GetLastWin32Error()}");
                }
                else
                {
                    Debug.WriteLine($"[EncryptorTests] Native DLL loaded at {tempPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EncryptorTests] DLL extraction failed: {ex.Message}");
            }
        }
        else
        {
            Debug.WriteLine($"[EncryptorTests] Native DLL not found. BaseDir={AppContext.BaseDirectory}, CWD={Environment.CurrentDirectory}");
        }
    }

    private static byte[]? TryDecompress(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var compressed = File.OpenRead(path);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gzip.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EncryptorTests] Decompress failed for {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Performs encrypt→decrypt round-trip and asserts the original data is recovered.
    /// </summary>
    private static void RoundTripCore(string method, byte[] plaintext)
    {
        using var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
        using var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);

        byte[] encrypted = new byte[plaintext.Length + 256];
        byte[] decrypted = new byte[plaintext.Length + 256];

        encryptor.Encrypt(plaintext, plaintext.Length, encrypted, out int encLen);
        decryptor.Decrypt(encrypted, encLen, decrypted, out int decLen);

        Assert.AreEqual(plaintext.Length, decLen,
            $"Round-trip length mismatch for {method}: expected {plaintext.Length}, got {decLen}");

        Assert.IsTrue(plaintext.AsSpan().SequenceEqual(decrypted.AsSpan(0, decLen)),
            $"Round-trip data mismatch for {method}");
    }

    // ══════════════════════════════════════════════════
    // 1. Factory Registration
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void Factory_RegisteredEncryptors_ShouldContainAllExpectedCiphers()
    {
        foreach (var cipher in MustHaveCiphers)
        {
            Assert.IsTrue(
                EncryptorFactory.RegisteredEncryptors.ContainsKey(cipher),
                $"Cipher '{cipher}' should be registered in EncryptorFactory");
        }

        Assert.IsTrue(EncryptorFactory.RegisteredEncryptors.Count >= 25,
            $"Expected at least 25 ciphers, found {EncryptorFactory.RegisteredEncryptors.Count}");
    }

    [TestMethod]
    public void Factory_CreateEncryptor_ShouldReturnNonNullForEachRegisteredCipher()
    {
        foreach (var kvp in EncryptorFactory.RegisteredEncryptors)
        {
            using var encryptor = EncryptorFactory.GetEncryptor(kvp.Key, TestPassword);
            Assert.IsNotNull(encryptor, $"Encryptor for '{kvp.Key}' should not be null");
            Assert.IsInstanceOfType(encryptor, typeof(IEncryptor),
                $"Encryptor for '{kvp.Key}' should implement IEncryptor");
        }
    }

    // ══════════════════════════════════════════════════
    // 2. Round-trip Tests (7 cipher types, 3 families)
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void RoundTrip_Aes256Cfb_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, ShadowsocksR! AES-256-CFB round-trip test.");
        RoundTripCore("aes-256-cfb", plaintext);
    }

    [TestMethod]
    public void RoundTrip_Chacha20_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, ShadowsocksR! Chacha20 round-trip test.");
        RoundTripCore("chacha20", plaintext);
    }

    [TestMethod]
    public void RoundTrip_Rc4Md5_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, ShadowsocksR! RC4-MD5 round-trip test.");
        RoundTripCore("rc4-md5", plaintext);
    }

    [TestMethod]
    public void RoundTrip_Salsa20_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, ShadowsocksR! Salsa20 round-trip test.");
        RoundTripCore("salsa20", plaintext);
    }

    [TestMethod]
    public void RoundTrip_None_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Hello, ShadowsocksR! None cipher round-trip test.");
        RoundTripCore("none", plaintext);
    }

    [TestMethod]
    public void RoundTrip_Aes128Ctr_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("AES-128-CTR stream cipher round-trip.");
        RoundTripCore("aes-128-ctr", plaintext);
    }

    [TestMethod]
    public void RoundTrip_Chacha20Ietf_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Chacha20-IETF variant round-trip test.");
        RoundTripCore("chacha20-ietf", plaintext);
    }

    [TestMethod]
    public void RoundTrip_MultipleCalls_ShouldProduceOriginalData()
    {
        const string method = "aes-256-cfb";
        using var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
        using var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);

        using var encStream = new MemoryStream();
        var parts = new[]
        {
            Encoding.UTF8.GetBytes("First chunk. "),
            Encoding.UTF8.GetBytes("Second chunk. "),
            Encoding.UTF8.GetBytes("Third chunk."),
        };

        var totalPlainLen = parts.Sum(p => p.Length);
        var encBuf = new byte[4096];

        foreach (var part in parts)
        {
            encryptor.Encrypt(part, part.Length, encBuf, out int encLen);
            encStream.Write(encBuf, 0, encLen);
        }

        var allEncrypted = encStream.ToArray();
        var decBuf = new byte[totalPlainLen + 256];
        decryptor.Decrypt(allEncrypted, allEncrypted.Length, decBuf, out int decLen);

        var expected = Encoding.UTF8.GetBytes("First chunk. Second chunk. Third chunk.");
        Assert.AreEqual(expected.Length, decLen);
        Assert.IsTrue(expected.AsSpan().SequenceEqual(decBuf.AsSpan(0, decLen)));
    }

    // ══════════════════════════════════════════════════
    // 3. Edge Cases
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void Encrypt_EmptyBuffer_ShouldReturnZeroLength()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);
        var emptyInput = Array.Empty<byte>();
        var output = new byte[256];

        encryptor.Encrypt(emptyInput, 0, output, out int outLen);
        Assert.AreEqual(0, outLen, "Encrypting empty buffer should produce zero-length output");
    }

    [TestMethod]
    public void Encrypt_EmptyBufferForNone_ShouldReturnZeroLength()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("none", TestPassword);
        var output = new byte[256];

        encryptor.Encrypt(Array.Empty<byte>(), 0, output, out int outLen);
        Assert.AreEqual(0, outLen);
    }

    [TestMethod]
    public void RoundTrip_SingleByte_ShouldProduceOriginalData()
    {
        RoundTripCore("aes-256-cfb", new byte[] { 0x42 });
        RoundTripCore("none", new byte[] { 0x42 });
    }

    [TestMethod]
    public void RoundTrip_LargeBuffer_ShouldProduceOriginalData()
    {
        var plaintext = new byte[30000];
        for (int i = 0; i < plaintext.Length; i++)
            plaintext[i] = (byte)(i % 251);

        RoundTripCore("aes-256-cfb", plaintext);
    }

    [TestMethod]
    public void RoundTrip_MaxInputSize_ShouldProduceOriginalData()
    {
        var plaintext = new byte[EncryptorBase.MAX_INPUT_SIZE];
        for (int i = 0; i < plaintext.Length; i++)
            plaintext[i] = (byte)((i * 7 + 13) % 256);

        RoundTripCore("aes-256-cfb", plaintext);
    }

    [TestMethod]
    public void RoundTrip_BinaryDataWithNullBytes_ShouldProduceOriginalData()
    {
        var data = new byte[] { 0x00, 0xFF, 0x00, 0x01, 0x7F, 0x80, 0x00, 0x00 };
        RoundTripCore("aes-256-cfb", data);
        RoundTripCore("none", data);
    }

    [TestMethod]
    public void RoundTrip_Random1024Bytes_ShouldProduceOriginalData()
    {
        var plaintext = new byte[1024];
        System.Security.Cryptography.RandomNumberGenerator.Fill(plaintext);
        RoundTripCore("aes-256-cfb", plaintext);
    }

    // ══════════════════════════════════════════════════
    // 4. None Cipher Specifics
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void None_Encrypt_ShouldBeIdentityTransform()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("none", TestPassword);
        var plaintext = Encoding.UTF8.GetBytes("No-op test data for none cipher.");
        var output = new byte[plaintext.Length + 64];

        encryptor.Encrypt(plaintext, plaintext.Length, output, out int outLen);

        // None cipher has IV size 0, output = input (identity transform)
        Assert.AreEqual(plaintext.Length, outLen);
        Assert.IsTrue(plaintext.AsSpan().SequenceEqual(output.AsSpan(0, outLen)),
            "None cipher encrypt should produce identity output");
    }

    [TestMethod]
    public void None_Decrypt_ShouldBeIdentityTransform()
    {
        using var decryptor = EncryptorFactory.GetEncryptor("none", TestPassword);
        var ciphertext = Encoding.UTF8.GetBytes("Direct decrypt test for none cipher.");
        var output = new byte[ciphertext.Length + 64];

        decryptor.Decrypt(ciphertext, ciphertext.Length, output, out int outLen);

        Assert.AreEqual(ciphertext.Length, outLen);
        Assert.IsTrue(ciphertext.AsSpan().SequenceEqual(output.AsSpan(0, outLen)),
            "None cipher decrypt should produce identity output");
    }

    // ══════════════════════════════════════════════════
    // 5. Key Derivation
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void GetKey_Aes256Cfb_ShouldReturn32ByteKey()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);
        var key = encryptor.getKey();

        Assert.IsNotNull(key);
        Assert.AreEqual(32, key.Length, "AES-256-CFB requires 32-byte key");
        Assert.IsFalse(key.All(b => b == 0), "Derived key should not be all zeros");
    }

    [TestMethod]
    public void GetKey_SamePassword_ShouldProduceSameKey()
    {
        using var e1 = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);
        using var e2 = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);

        Assert.IsTrue(e1.getKey().AsSpan().SequenceEqual(e2.getKey()),
            "Same password should derive the same key");
    }

    [TestMethod]
    public void GetKey_DifferentMethods_ShouldHaveCorrectSizes()
    {
        using var aes256 = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);
        using var aes128 = EncryptorFactory.GetEncryptor("aes-128-cfb", TestPassword);
        using var none = EncryptorFactory.GetEncryptor("none", TestPassword);
        using var rc4md5 = EncryptorFactory.GetEncryptor("rc4-md5", TestPassword);

        Assert.AreEqual(32, aes256.getKey().Length);
        Assert.AreEqual(16, aes128.getKey().Length);
        Assert.AreEqual(16, none.getKey().Length);
        Assert.AreEqual(16, rc4md5.getKey().Length);
    }

    [TestMethod]
    public void GetKey_SodiumCiphers_ShouldReturn32ByteKey()
    {
        string[] sodiumCiphers = ["salsa20", "chacha20", "chacha20-ietf", "xsalsa20", "xchacha20"];

        foreach (var method in sodiumCiphers)
        {
            using var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
            var key = encryptor.getKey();
            Assert.IsNotNull(key);
            Assert.AreEqual(32, key.Length, $"{method} requires 32-byte key");
        }
    }

    // ══════════════════════════════════════════════════
    // 6. getInfo() Tests
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void GetInfo_ForAllRegisteredCiphers_ShouldReturnValidEncryptorInfo()
    {
        foreach (var kvp in EncryptorFactory.RegisteredEncryptors)
        {
            using var encryptor = EncryptorFactory.GetEncryptor(kvp.Key, TestPassword);
            var info = encryptor.getInfo();

            Assert.IsNotNull(info, $"EncryptorInfo for '{kvp.Key}' should not be null");
            Assert.IsTrue(info.KeySize > 0, $"KeySize for '{kvp.Key}' should be positive, got {info.KeySize}");
            Assert.IsTrue(info.IvSize >= 0, $"IvSize for '{kvp.Key}' should be non-negative, got {info.IvSize}");
            Assert.IsTrue(info.Type != 0, $"Type for '{kvp.Key}' should be non-zero, got {info.Type}");
        }
    }

    [TestMethod]
    public void GetInfo_Aes256Cfb_ShouldHaveCorrectKeyAndIvSizes()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);
        var info = encryptor.getInfo();

        Assert.AreEqual(32, info.KeySize);
        Assert.AreEqual(16, info.IvSize);
    }

    [TestMethod]
    public void GetInfo_Rc4Md5_ShouldHaveCorrectSizesAndInnerName()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("rc4-md5", TestPassword);
        var info = encryptor.getInfo();

        Assert.AreEqual(16, info.KeySize);
        Assert.AreEqual(16, info.IvSize);
        Assert.AreEqual("RC4", info.InnerLibName);
    }

    [TestMethod]
    public void GetInfo_None_ShouldHaveZeroIvSize()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("none", TestPassword);
        var info = encryptor.getInfo();

        Assert.AreEqual(16, info.KeySize);
        Assert.AreEqual(0, info.IvSize, "None cipher should have zero IV size");
    }

    [TestMethod]
    public void GetInfo_Chacha20_ShouldHaveCorrectSizes()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("chacha20", TestPassword);
        var info = encryptor.getInfo();

        Assert.AreEqual(32, info.KeySize);
        Assert.AreEqual(8, info.IvSize);
    }

    [TestMethod]
    public void GetInfo_Salsa20_ShouldHaveCorrectSizes()
    {
        using var encryptor = EncryptorFactory.GetEncryptor("salsa20", TestPassword);
        var info = encryptor.getInfo();

        Assert.AreEqual(32, info.KeySize);
        Assert.AreEqual(8, info.IvSize);
    }

    [TestMethod]
    public void GetInfo_FactoryGetEncryptorInfo_ShouldMatchEncryptorGetInfo()
    {
        foreach (var kvp in EncryptorFactory.RegisteredEncryptors)
        {
            using var encryptor = EncryptorFactory.GetEncryptor(kvp.Key, TestPassword);
            var encryptorInfo = encryptor.getInfo();
            var factoryInfo = EncryptorFactory.GetEncryptorInfo(kvp.Key);

            Assert.AreEqual(encryptorInfo.KeySize, factoryInfo.KeySize,
                $"KeySize mismatch for {kvp.Key}");
            Assert.AreEqual(encryptorInfo.IvSize, factoryInfo.IvSize,
                $"IvSize mismatch for {kvp.Key}");
            Assert.AreEqual(encryptorInfo.Type, factoryInfo.Type,
                $"Type mismatch for {kvp.Key}");
        }
    }

    // ══════════════════════════════════════════════════
    // 7. IV & Lifecycle Tests
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void GetIV_AfterConstruction_ShouldReturnCorrectLength()
    {
        using var aes256 = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);
        using var chacha20 = EncryptorFactory.GetEncryptor("chacha20", TestPassword);
        using var none = EncryptorFactory.GetEncryptor("none", TestPassword);

        Assert.AreEqual(16, aes256.getIV().Length);
        Assert.AreEqual(8, chacha20.getIV().Length);
        Assert.AreEqual(0, none.getIV().Length);
    }

    [TestMethod]
    public void ResetEncrypt_ShouldAllowMultipleEncryptCycles()
    {
        const string method = "aes-256-cfb";
        using var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
        using var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);

        var plaintext1 = Encoding.UTF8.GetBytes("First encryption cycle data.");
        var plaintext2 = Encoding.UTF8.GetBytes("Second encryption cycle data after reset.");

        var encBuf = new byte[4096];
        var decBuf = new byte[4096];

        encryptor.Encrypt(plaintext1, plaintext1.Length, encBuf, out int encLen1);
        decryptor.Decrypt(encBuf, encLen1, decBuf, out int decLen1);
        Assert.IsTrue(plaintext1.AsSpan().SequenceEqual(decBuf.AsSpan(0, decLen1)));

        encryptor.ResetEncrypt();
        decryptor.ResetDecrypt();

        encryptor.Encrypt(plaintext2, plaintext2.Length, encBuf, out int encLen2);
        decryptor.Decrypt(encBuf, encLen2, decBuf, out int decLen2);
        Assert.IsTrue(plaintext2.AsSpan().SequenceEqual(decBuf.AsSpan(0, decLen2)));
    }

    [TestMethod]
    public void Dispose_ShouldReleaseResources()
    {
        var encryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);
        var decryptor = EncryptorFactory.GetEncryptor("aes-256-cfb", TestPassword);

        // Should not throw
        encryptor.Dispose();
        decryptor.Dispose();
    }

    // ══════════════════════════════════════════════════
    // 8. All-Cipher Round-trip Smoke Test
    // ══════════════════════════════════════════════════

    [TestMethod]
    public void RoundTrip_AllRegisteredCiphers_ShouldProduceOriginalData()
    {
        var plaintext = Encoding.UTF8.GetBytes("Quick round-trip smoke test for all registered ciphers.");
        var failures = new List<string>();

        foreach (var kvp in EncryptorFactory.RegisteredEncryptors)
        {
            try
            {
                RoundTripCore(kvp.Key, plaintext);
            }
            catch (Exception ex)
            {
                failures.Add($"{kvp.Key}: {ex.GetType().Name} - {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"Round-trip failures ({failures.Count}):\n{string.Join("\n", failures)}");
        }
    }
}
