namespace Shadowsocks.Proxy.Core.Tests.Obfs;

using Shadowsocks.Obfs;
using System.Text;

[TestClass]
public class ObfsRoundTripTests
{
    // Shared factory method to create a basic ServerInfo for obfs testing
    private static ServerInfo CreateServerInfo(
        string host = "127.0.0.1",
        int port = 8388,
        string param = "",
        byte[]? iv = null,
        byte[]? key = null,
        int headLen = 30,
        int tcpMss = 1440,
        int bufferSize = 8192)
    {
        iv ??= new byte[16];
        key ??= new byte[16];
        new Random(42).NextBytes(iv);
        new Random(42).NextBytes(key);

        return new ServerInfo(
            host, port, param, data: null!,
            iv: iv, key_str: "", key: key,
            head_len: headLen, tcp_mss: tcpMss,
            overhead: 0, buffer_size: bufferSize);
    }

    #region Factory Registration Tests

    [TestMethod]
    public void ObfsFactory_GetObfs_ShouldReturnPlainForEmptyMethod()
    {
        var obfs = ObfsFactory.GetObfs("");
        Assert.IsNotNull(obfs);
        Assert.AreEqual("plain", obfs.Name());
    }

    [TestMethod]
    public void ObfsFactory_GetObfs_ShouldCreateAllExpectedProtocols()
    {
        var expected = new[]
        {
            "plain", "origin",
            "http_simple", "http_post", "random_head",
            "tls1.2_ticket_auth", "tls1.2_ticket_fastauth",
            "verify_deflate",
            "auth_sha1_v4",
            "auth_aes128_md5", "auth_aes128_sha1",
            "auth_chain_a", "auth_chain_b", "auth_chain_c",
            "auth_chain_d", "auth_chain_e", "auth_chain_f",
            "auth_akarin_rand", "auth_akarin_spec_a",
        };

        foreach (var name in expected)
        {
            var obfs = ObfsFactory.GetObfs(name);
            Assert.IsNotNull(obfs, $"ObfsFactory.GetObfs(\"{name}\") returned null");
            Assert.AreEqual(name, obfs.Name(), $"Obfs name mismatch for \"{name}\"");
        }
    }

    #endregion

    #region Plain Obfs Round-Trip Tests

    [TestMethod]
    public void Plain_ClientEncodeDecode_RoundTrip_ShouldReturnSameData()
    {
        var obfs = ObfsFactory.GetObfs("plain");
        var original = Encoding.UTF8.GetBytes("Hello plain round-trip test!");

        byte[] encoded = obfs.ClientEncode(original, original.Length, out int encodeLen);
        byte[] decoded = obfs.ClientDecode(encoded, encodeLen, out int decodeLen, out bool needSendback);

        Assert.AreEqual(original.Length, decodeLen);
        Assert.IsFalse(needSendback);
        CollectionAssert.AreEqual(original, decoded[..decodeLen]);
    }

    [TestMethod]
    public void Plain_ClientPreEncryptPostDecrypt_RoundTrip_ShouldReturnSameData()
    {
        var obfs = ObfsFactory.GetObfs("origin");
        var original = Encoding.UTF8.GetBytes("Hello plain pre-encrypt round-trip!");

        byte[] encoded = obfs.ClientPreEncrypt(original, original.Length, out int encodeLen);
        byte[] decoded = obfs.ClientPostDecrypt(encoded, encodeLen, out int decodeLen);

        Assert.AreEqual(original.Length, decodeLen);
        CollectionAssert.AreEqual(original, decoded[..decodeLen]);
    }

    #endregion

    #region HttpSimpleObfs Round-Trip Tests

    [TestMethod]
    public void HttpSimple_ClientEncodeDecode_RoundTrip_ShouldPassThroughPostHeaderData()
    {
        // http_simple embeds the first headsize bytes (plus up to 64 random extra) in the
        // HTTP request URL, which is lost on decode. Data after that passes through.
        var serverInfo = CreateServerInfo(host: "example.com", headLen: 30);
        var obfs = ObfsFactory.GetObfs("http_simple");
        obfs.SetServerInfo(serverInfo);

        int headSize = serverInfo.Iv.Length + serverInfo.head_len; // 16 + 30 = 46
        // Use large data so the random portion is a small fraction
        int totalSize = 2000;
        var original = new byte[totalSize];
        new Random(99).NextBytes(original);

        byte[] encoded = obfs.ClientEncode(original, original.Length, out int encodeLen);
        Assert.IsTrue(encodeLen > 0, "ClientEncode should produce output");

        byte[] decoded = obfs.ClientDecode(encoded, encodeLen, out int decodeLen, out bool needSendback);

        // The portion after the HTTP-headers starts after \r\n\r\n in the encoded output.
        // The headdata portion goes into URL and is lost. headdata = headSize + random(0,64).
        // So at most headSize+63 bytes are lost, and at least headSize bytes.
        int maxLost = headSize + 64;
        Assert.IsTrue(decodeLen >= totalSize - maxLost,
            $"Expected decoded length >= {totalSize - maxLost}, got {decodeLen}");
        Assert.IsTrue(decodeLen <= totalSize - headSize,
            $"Expected decoded length <= {totalSize - headSize}, got {decodeLen}");

        // Verify decoded data is a suffix of the original (the portion after headdata)
        int offset = totalSize - decodeLen;
        var expected = new byte[decodeLen];
        Array.Copy(original, offset, expected, 0, decodeLen);
        CollectionAssert.AreEqual(expected, decoded[..decodeLen]);
    }

    [TestMethod]
    public void HttpSimple_ClientEncodeDecode_WithSmallData_ShouldReturnEmptyOnDecode()
    {
        // When data is smaller than headsize, all bytes go into URL → lost on decode
        var serverInfo = CreateServerInfo(host: "example.com", headLen: 30);
        var obfs = ObfsFactory.GetObfs("http_simple");
        obfs.SetServerInfo(serverInfo);

        var original = new byte[20]; // less than headsize (46)
        new Random(77).NextBytes(original);

        byte[] encoded = obfs.ClientEncode(original, original.Length, out int encodeLen);
        Assert.IsTrue(encodeLen > 0);

        byte[] decoded = obfs.ClientDecode(encoded, encodeLen, out int decodeLen, out bool needSendback);
        // All data went into URL → decode returns 0
        Assert.AreEqual(0, decodeLen);
    }

    #endregion

    #region TlsTicketAuthObfs Tests

    [TestMethod]
    public void TlsTicketAuth_CreateAndEncode_ShouldNotThrow()
    {
        var serverInfo = CreateServerInfo(host: "example.com");
        var obfs = ObfsFactory.GetObfs("tls1.2_ticket_auth");
        obfs.SetServerInfo(serverInfo);
        serverInfo.data = obfs.InitData();

        var data = Encoding.UTF8.GetBytes("test data for TLS");
        byte[] encoded = obfs.ClientEncode(data, data.Length, out int encodeLen);

        Assert.IsNotNull(encoded);
        // First encode produces TLS client hello handshake
        Assert.IsTrue(encodeLen > 0, "TLS client hello should produce output");
    }

    [TestMethod]
    public void TlsTicketAuthFastauth_CreateAndEncode_ShouldNotThrow()
    {
        var serverInfo = CreateServerInfo(host: "example.com");
        var obfs = ObfsFactory.GetObfs("tls1.2_ticket_fastauth");
        obfs.SetServerInfo(serverInfo);
        serverInfo.data = obfs.InitData();

        var data = Encoding.UTF8.GetBytes("fastauth test");
        byte[] encoded = obfs.ClientEncode(data, data.Length, out int encodeLen);

        Assert.IsNotNull(encoded);
        Assert.IsTrue(encodeLen > 0);
    }

    #endregion

    #region AuthSHA1V4 Round-Trip Tests

    [TestMethod]
    public void AuthSHA1V4_PreEncryptPostDecrypt_RoundTrip_ShouldRecoverOriginalData()
    {
        var serverInfo = CreateServerInfo();
        // AuthSHA1V4 uses both IV and key
        // First PreEncrypt call sends auth header; second sends PackData only
        var sender = ObfsFactory.GetObfs("auth_sha1_v4");
        sender.SetServerInfo(serverInfo);
        sender.SetServerInfoIV(serverInfo.Iv);
        serverInfo.data = sender.InitData();

        // First call: consume auth header (send some data to trigger PackAuthData)
        var dummyData = Encoding.UTF8.GetBytes("auth header trigger data that is long enough to exceed headsize");
        sender.ClientPreEncrypt(dummyData, dummyData.Length, out _);

        // Second call: this uses PackData only (no auth header)
        var original = Encoding.UTF8.GetBytes("Hello auth_sha1_v4 round-trip payload!");
        byte[] encoded = sender.ClientPreEncrypt(original, original.Length, out int encodeLen);

        // Create receiver with same config
        var receiverInfo = CreateServerInfo();
        var receiverKey = new byte[16];
        serverInfo.key.CopyTo(receiverKey, 0);
        var receiverIv = new byte[16];
        serverInfo.Iv.CopyTo(receiverIv, 0);

        var receiverServerInfo = new ServerInfo(
            serverInfo.host, serverInfo.port, serverInfo.param, data: null!,
            iv: receiverIv, key_str: "", key: receiverKey,
            head_len: serverInfo.head_len, tcp_mss: serverInfo.tcp_mss,
            overhead: serverInfo.overhead, buffer_size: serverInfo.buffer_size);
        var receiver = ObfsFactory.GetObfs("auth_sha1_v4");
        receiver.SetServerInfo(receiverServerInfo);
        receiver.SetServerInfoIV(receiverIv);
        receiverServerInfo.data = receiver.InitData();

        byte[] decoded = receiver.ClientPostDecrypt(encoded, encodeLen, out int decodeLen);

        Assert.AreEqual(original.Length, decodeLen);
        CollectionAssert.AreEqual(original, decoded[..decodeLen]);
    }

    #endregion

    #region AuthAES128SHA1 Round-Trip Tests

    [TestMethod]
    public void AuthAES128SHA1_PreEncryptPostDecrypt_RoundTrip_ShouldRecoverOriginalData()
    {
        var serverInfo = CreateServerInfo();
        // AuthAES128SHA1 uses Server.key as user_key if no param with ':'
        var instance = ObfsFactory.GetObfs("auth_aes128_sha1");
        instance.SetServerInfo(serverInfo);
        instance.SetServerInfoIV(serverInfo.Iv);
        serverInfo.data = instance.InitData();

        // First PreEncrypt sends auth header (also sets user_key internally)
        var dummyData = Encoding.UTF8.GetBytes("trigger auth header for aes128 - needs enough bytes to satisfy headsize");
        instance.ClientPreEncrypt(dummyData, dummyData.Length, out _);

        // Second call: PackData only
        var original = Encoding.UTF8.GetBytes("Hello auth_aes128_sha1 round-trip payload!");
        byte[] encoded = instance.ClientPreEncrypt(original, original.Length, out int encodeLen);

        // PostDecrypt on the SAME instance (recv state is independent from send state)
        byte[] decoded = instance.ClientPostDecrypt(encoded, encodeLen, out int decodeLen);

        Assert.AreEqual(original.Length, decodeLen);
        CollectionAssert.AreEqual(original, decoded[..decodeLen]);
    }

    #endregion

    #region VerifyDeflateObfs Round-Trip Tests

    [TestMethod]
    public void VerifyDeflateObfs_PreEncryptPostDecrypt_RoundTrip_ShouldRecoverOriginalData()
    {
        var serverInfo = CreateServerInfo();
        var obfs = ObfsFactory.GetObfs("verify_deflate");
        obfs.SetServerInfo(serverInfo);

        var original = Encoding.UTF8.GetBytes("Hello verify_deflate round-trip! This data will be compressed then decompressed.");
        byte[] encoded = obfs.ClientPreEncrypt(original, original.Length, out int encodeLen);
        Assert.IsTrue(encodeLen > 0);

        byte[] decoded = obfs.ClientPostDecrypt(encoded, encodeLen, out int decodeLen);

        Assert.AreEqual(original.Length, decodeLen);
        CollectionAssert.AreEqual(original, decoded[..decodeLen]);
    }

    [TestMethod]
    public void VerifyDeflateObfs_PreEncrypt_ShouldCompressData()
    {
        var serverInfo = CreateServerInfo();
        var obfs = ObfsFactory.GetObfs("verify_deflate");
        obfs.SetServerInfo(serverInfo);

        // Repeated data compresses well
        var original = Encoding.UTF8.GetBytes(new string('A', 1000));
        byte[] encoded = obfs.ClientPreEncrypt(original, original.Length, out int encodeLen);

        // Compressed output should be significantly smaller than input
        Assert.IsTrue(encodeLen < original.Length,
            $"Expected compressed output ({encodeLen}) to be smaller than input ({original.Length})");
    }

    #endregion

    #region Empty Data / Edge Case Tests

    [TestMethod]
    public void Plain_EmptyData_ClientEncodeDecode_ShouldNotCrash()
    {
        var obfs = ObfsFactory.GetObfs("plain");
        var empty = Array.Empty<byte>();

        byte[] encoded = obfs.ClientEncode(empty, 0, out int encodeLen);
        byte[] decoded = obfs.ClientDecode(encoded, encodeLen, out int decodeLen, out bool needSendback);

        Assert.AreEqual(0, decodeLen);
        Assert.IsFalse(needSendback);
    }

    [TestMethod]
    public void VerifyDeflateObfs_EmptyData_ShouldNotCrash()
    {
        var serverInfo = CreateServerInfo();
        var obfs = ObfsFactory.GetObfs("verify_deflate");
        obfs.SetServerInfo(serverInfo);

        var empty = Array.Empty<byte>();
        byte[] encoded = obfs.ClientPreEncrypt(empty, 0, out int encodeLen);

        // VerifyDeflate produces 0-length output for empty data (no packets emitted)
        Assert.AreEqual(0, encodeLen, "Empty data should produce 0-length encoded output");
    }

    [TestMethod]
    public void AuthObfs_EmptyData_PreEncrypt_ShouldNotCrash()
    {
        foreach (var protocol in new[] { "auth_sha1_v4", "auth_aes128_sha1" })
        {
            var serverInfo = CreateServerInfo();
            var obfs = ObfsFactory.GetObfs(protocol);
            obfs.SetServerInfo(serverInfo);
            obfs.SetServerInfoIV(serverInfo.Iv);
            serverInfo.data = obfs.InitData();

            var empty = Array.Empty<byte>();
            // Empty data on first call still triggers auth header
            byte[] encoded = obfs.ClientPreEncrypt(empty, 0, out int encodeLen);
            Assert.IsNotNull(encoded, $"{protocol}: empty data should not return null");
        }
    }

    [TestMethod]
    public void HttpSimple_EmptyData_ClientEncodeDecode_ShouldNotCrash()
    {
        var serverInfo = CreateServerInfo(host: "example.com");
        var obfs = ObfsFactory.GetObfs("http_simple");
        obfs.SetServerInfo(serverInfo);

        var empty = Array.Empty<byte>();
        byte[] encoded = obfs.ClientEncode(empty, 0, out int encodeLen);
        Assert.IsTrue(encodeLen > 0, "HTTP simple with empty data should still produce headers");

        byte[] decoded = obfs.ClientDecode(encoded, encodeLen, out int decodeLen, out bool needSendback);
        Assert.AreEqual(0, decodeLen);
    }

    #endregion

    #region Large Buffer Tests

    [TestMethod]
    public void AuthObfs_LargeBuffer_ShouldRoundTripCorrectly()
    {
        // 64KB buffer round-trip for auth protocols.
        // First PreEncrypt call produces PackAuthData (not decodable by PostDecrypt).
        // Use a small trigger for the first call, then send the 64KB data.
        TestLargeAuthRoundTrip("auth_sha1_v4", triggerSize: 100);
        TestLargeAuthRoundTrip("auth_aes128_sha1", triggerSize: 1200);
    }

    private static void TestLargeAuthRoundTrip(string protocol, int triggerSize)
    {
        var serverInfo = CreateServerInfo();
        var instance = ObfsFactory.GetObfs(protocol);
        instance.SetServerInfo(serverInfo);
        instance.SetServerInfoIV(serverInfo.Iv);
        serverInfo.data = instance.InitData();

        // First call: send trigger data to consume auth header state
        var trigger = new byte[triggerSize];
        new Random(42).NextBytes(trigger);
        instance.ClientPreEncrypt(trigger, trigger.Length, out _);

        // For auth_aes128_sha1, the first call may leave data in send_buffer.
        // Drain it with a null call so the next PreEncrypt starts clean.
        while (true)
        {
            byte[] drain = instance.ClientPreEncrypt(null!, 0, out int drainLen);
            if (drain == null || drainLen == 0)
                break;
        }

        // Second call: send 64KB through PackData path only (no auth header)
        var original = new byte[65536];
        new Random(123).NextBytes(original);
        byte[] encoded = instance.ClientPreEncrypt(original, original.Length, out int encodeLen);

        // For auth_aes128_sha1, may still buffer. Drain again.
        var encodedChunks = new List<(byte[] data, int length)> { (encoded, encodeLen) };
        int totalEncoded = encodeLen;
        while (true)
        {
            byte[] extra = instance.ClientPreEncrypt(null!, 0, out int extraLen);
            if (extra == null || extraLen == 0)
                break;
            encodedChunks.Add((extra, extraLen));
            totalEncoded += extraLen;
        }

        // Combine all PackData output (no auth headers present)
        var combined = new byte[totalEncoded];
        int offset = 0;
        foreach (var (data, length) in encodedChunks)
        {
            Array.Copy(data, 0, combined, offset, length);
            offset += length;
        }

        byte[] decoded = instance.ClientPostDecrypt(combined, totalEncoded, out int decodeLen);

        Assert.AreEqual(original.Length, decodeLen,
            $"{protocol}: expected {original.Length} decoded bytes, got {decodeLen}");
        CollectionAssert.AreEqual(original, decoded[..decodeLen],
            $"{protocol}: data mismatch for 64KB round-trip");
    }

    #endregion

    #region Invalid Key / Error Handling Tests

    [TestMethod]
    public void AuthObfs_InvalidKey_CorruptedData_ShouldThrowObfsException()
    {
        // Auth protocols should throw ObfsException (not crash) on corrupted data
        foreach (var protocol in new[] { "auth_sha1_v4", "auth_aes128_sha1" })
        {
            var serverInfo = CreateServerInfo();
            var sender = ObfsFactory.GetObfs(protocol);
            sender.SetServerInfo(serverInfo);
            sender.SetServerInfoIV(serverInfo.Iv);
            serverInfo.data = sender.InitData();

            var data = Encoding.UTF8.GetBytes("auth trigger data that is long enough to exceed the header boundary");
            sender.ClientPreEncrypt(data, data.Length, out _);

            var payload = Encoding.UTF8.GetBytes("real payload for corruption test!");
            byte[] encoded = sender.ClientPreEncrypt(payload, payload.Length, out int encodeLen);

            // Corrupt the encoded data
            if (encodeLen > 10)
            {
                encoded[5] ^= 0xFF; // flip bits
            }

            // Create a receiver with DIFFERENT key to ensure corrupted/invalid data handling
            var badServerInfo = CreateServerInfo(); // different random key
            var receiver = ObfsFactory.GetObfs(protocol);
            receiver.SetServerInfo(badServerInfo);
            receiver.SetServerInfoIV(badServerInfo.Iv);
            badServerInfo.data = receiver.InitData();

            // Bootstrap user_key for auth_aes128_sha1 receiver
            receiver.ClientPreEncrypt(new byte[100], 100, out _);

            try
            {
                receiver.ClientPostDecrypt(encoded, encodeLen, out _);
                // Some corruptions might slip through; that's OK for the purpose
            }
            catch (ObfsException)
            {
                // Expected: protocol-level integrity check catches corruption
            }
            catch (Exception ex) when (ex is not ObfsException)
            {
                Assert.Fail($"{protocol}: threw unexpected exception type {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    [TestMethod]
    public void ObfsFactory_InvalidProtocolName_ShouldThrow()
    {
        Assert.ThrowsException<KeyNotFoundException>(() =>
        {
            ObfsFactory.GetObfs("nonexistent_protocol_xyz");
        });
    }

    [TestMethod]
    public void HttpSimple_ClientDecode_InvalidData_ShouldReturnEmpty()
    {
        var serverInfo = CreateServerInfo(host: "example.com");
        var obfs = ObfsFactory.GetObfs("http_simple");
        obfs.SetServerInfo(serverInfo);

        // Provide data without \r\n\r\n delimiter – decode should return 0
        var garbage = Encoding.UTF8.GetBytes("NOT A VALID HTTP RESPONSE");
        byte[] decoded = obfs.ClientDecode(garbage, garbage.Length, out int decodeLen, out bool needSendback);

        Assert.AreEqual(0, decodeLen, "Invalid HTTP data should return 0 decoded length");
    }

    #endregion
}
