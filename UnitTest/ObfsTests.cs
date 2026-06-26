using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Obfs;
using System;

namespace UnitTest
{
    [TestClass]
    public class ObfsTests
    {
        private static ServerInfo CreateServerInfo(string host, int port, string param, object data)
        {
            var iv = new byte[16];
            var key = new byte[16];
            new Random(42).NextBytes(iv);
            new Random(123).NextBytes(key);
            return new ServerInfo(host, port, param, data, iv, "test_key", key, head_len: 30, tcp_mss: 1440, overhead: 0, buffer_size: 8192);
        }

        // ============================================================
        // Test 1: Plain obfs — ClientEncode / ClientDecode identity
        // ============================================================
        [TestMethod]
        public void Plain_EncodeDecode_RoundTrip()
        {
            var obfs = ObfsFactory.GetObfs("plain");
            var serverInfo = CreateServerInfo("127.0.0.1", 8388, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            var original = new byte[1024];
            new Random(999).NextBytes(original);

            var encoded = obfs.ClientEncode(original, original.Length, out var encodedLen);
            var decoded = obfs.ClientDecode(encoded, encodedLen, out var decodedLen, out var needSendBack);

            Assert.AreEqual(original.Length, decodedLen, "Decoded length should match original");
            Assert.IsFalse(needSendBack, "Plain should not request send-back");
            for (var i = 0; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], decoded[i], $"Mismatch at byte {i}");
            }
        }

        // ============================================================
        // Test 2: AuthSHA1V4 — ClientPreEncrypt / ClientPostDecrypt
        //          round-trip for data packets (skip auth header)
        // ============================================================
        [TestMethod]
        public void AuthSHA1V4_PreEncryptPostDecrypt_RoundTrip()
        {
            var obfs = ObfsFactory.GetObfs("auth_sha1_v4");
            var serverInfo = CreateServerInfo("10.0.0.1", 443, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            // First call: feed a small payload so it all fits inside the
            // auth header — no PackData-style packets are emitted.
            var seedData = new byte[10];
            new Random(1).NextBytes(seedData);
            obfs.ClientPreEncrypt(seedData, seedData.Length, out _);

            // Second call: feed the real test payload.  Since has_sent_header
            // is now true, this goes straight to PackData and produces only
            // packets that ClientPostDecrypt can decode.
            var original = new byte[500];
            new Random(42).NextBytes(original);
            var packed = obfs.ClientPreEncrypt(original, original.Length, out var packedLen);

            Assert.IsTrue(packedLen > 0, "PackData should produce output");
            Assert.IsTrue(packedLen > original.Length, "Packed data should have overhead");

            var recovered = obfs.ClientPostDecrypt(packed, packedLen, out var recoveredLen);

            Assert.AreEqual(original.Length, recoveredLen, "Recovered length should match original");
            for (var i = 0; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], recovered[i], $"Mismatch at byte {i}");
            }
        }

        // ============================================================
        // Test 3: AuthAES128SHA1 — protocol packing round-trip
        // ============================================================
        [TestMethod]
        public void AuthAES128SHA1_PreEncryptPostDecrypt_RoundTrip()
        {
            var obfs = ObfsFactory.GetObfs("auth_aes128_sha1");
            var serverInfo = CreateServerInfo("10.0.0.1", 8388, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            // First call: consume auth header with a small payload.
            var seedData = new byte[10];
            new Random(7).NextBytes(seedData);
            obfs.ClientPreEncrypt(seedData, seedData.Length, out _);

            // Second call: send the real payload which will be PackData-
            // formatted and therefore decodable by ClientPostDecrypt.
            var original = new byte[300];
            new Random(77).NextBytes(original);
            var packed = obfs.ClientPreEncrypt(original, original.Length, out var packedLen);

            Assert.IsTrue(packedLen > 0, "PackData should produce output");

            var recovered = obfs.ClientPostDecrypt(packed, packedLen, out var recoveredLen);

            Assert.AreEqual(original.Length, recoveredLen, "Recovered length mismatch");
            for (var i = 0; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], recovered[i], $"Mismatch at byte {i}");
            }
        }

        // ============================================================
        // Test 4: VerifyDeflateObfs — deflate-compression round-trip
        // ============================================================
        [TestMethod]
        public void VerifyDeflate_PreEncryptPostDecrypt_RoundTrip()
        {
            var obfs = ObfsFactory.GetObfs("verify_deflate");
            var serverInfo = CreateServerInfo("127.0.0.1", 9999, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            // Use data that compresses well (repeating pattern) and some
            // random data to exercise both code paths.
            var original = new byte[2000];
            for (var i = 0; i < original.Length; i++)
            {
                original[i] = (byte)(i % 251);
            }

            var packed = obfs.ClientPreEncrypt(original, original.Length, out var packedLen);
            Assert.IsTrue(packedLen > 0, "PreEncrypt should produce output");

            var recovered = obfs.ClientPostDecrypt(packed, packedLen, out var recoveredLen);

            Assert.AreEqual(original.Length, recoveredLen, "Recovered length mismatch");
            for (var i = 0; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], recovered[i], $"Mismatch at byte {i}");
            }
        }

        // ============================================================
        // Test 5: HttpSimpleObfs — ClientEncode / ClientDecode
        //          HTTP-wrapping round-trip
        // ============================================================
        [TestMethod]
        public void HttpSimple_EncodeDecode_RoundTrip()
        {
            var obfs = ObfsFactory.GetObfs("http_simple");
            var serverInfo = CreateServerInfo("example.com", 80, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            // Provide enough data so that part of it survives as raw bytes
            // after the URL-encoded headdata inside the HTTP request.
            var original = new byte[2000];
            new Random(12345).NextBytes(original);

            var encoded = obfs.ClientEncode(original, original.Length, out var encodedLen);
            Assert.IsTrue(encodedLen > 0, "ClientEncode should produce output");

            var decoded = obfs.ClientDecode(encoded, encodedLen, out var decodedLen, out var needSendBack);

            // HttpSimple URL-encodes a prefix of the input; the remaining
            // suffix is appended verbatim after the HTTP headers.  The
            // decoder strips headers and returns that raw suffix.
            Assert.IsTrue(decodedLen > 0, "Should recover the raw suffix");
            Assert.IsTrue(decodedLen < original.Length, "Prefix is URL-encoded so recovered < original");

            // The decoded bytes must match the tail of the original.
            var offset = original.Length - decodedLen;
            for (var i = 0; i < decodedLen; i++)
            {
                Assert.AreEqual(original[offset + i], decoded[i], $"Mismatch at byte {i}");
            }
        }

        // ============================================================
        // Test 6: AuthSHA1V4 — multiple consecutive packet round-trips
        // ============================================================
        [TestMethod]
        public void AuthSHA1V4_MultiplePacket_RoundTrips()
        {
            var obfs = ObfsFactory.GetObfs("auth_sha1_v4");
            var serverInfo = CreateServerInfo("192.168.1.1", 1080, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            // Consume auth header with minimal payload.
            var seed = new byte[5];
            new Random(3).NextBytes(seed);
            obfs.ClientPreEncrypt(seed, seed.Length, out _);

            // Now send three distinct payloads back-to-back on the same
            // instance.  Each ClientPreEncrypt produces PackData output;
            // ClientPostDecrypt accumulates via recv_buf and extracts
            // the payloads correctly regardless of framing.
            var payloads = new byte[3][];
            for (var p = 0; p < 3; p++)
            {
                payloads[p] = new byte[200 + p * 100];
                new Random(100 + p).NextBytes(payloads[p]);
            }

            // Encode all payloads into one concatenated buffer.
            var totalPackedLen = 0;
            var packedBuf = new byte[65536];
            foreach (var payload in payloads)
            {
                var pkt = obfs.ClientPreEncrypt(payload, payload.Length, out var pktLen);
                Assert.IsTrue(pktLen > 0, $"Payload {payload.Length} should pack");
                Array.Copy(pkt, 0, packedBuf, totalPackedLen, pktLen);
                totalPackedLen += pktLen;
            }

            // Decode the concatenated stream.  ClientPostDecrypt processes
            // all packets from its internal recv_buf in one call.
            var recovered = obfs.ClientPostDecrypt(packedBuf, totalPackedLen, out var recoveredLen);

            var expectedTotal = 0;
            foreach (var p in payloads) expectedTotal += p.Length;
            Assert.AreEqual(expectedTotal, recoveredLen, "Total recovered length mismatch");

            // Verify each original payload appears in order.
            var cursor = 0;
            for (var p = 0; p < 3; p++)
            {
                for (var i = 0; i < payloads[p].Length; i++)
                {
                    Assert.AreEqual(payloads[p][i], recovered[cursor + i],
                        $"Payload {p} mismatch at byte {i}");
                }
                cursor += payloads[p].Length;
            }
        }

        // ============================================================
        // Test 7: TlsTicketAuthObfs — verify constructibility and InitData
        // ============================================================
        [TestMethod]
        public void TlsTicketAuth_Constructible()
        {
            var obfs = ObfsFactory.GetObfs("tls1.2_ticket_auth");
            Assert.IsNotNull(obfs);
            var data = obfs.InitData();
            Assert.IsNotNull(data);
        }

        [TestMethod]
        public void TlsTicketFastauth_Constructible()
        {
            var obfs = ObfsFactory.GetObfs("tls1.2_ticket_fastauth");
            Assert.IsNotNull(obfs);
            var data = obfs.InitData();
            Assert.IsNotNull(data);
        }

        // ============================================================
        // Test 8: HttpSimpleObfs — http_post variant round-trip
        // ============================================================
        [TestMethod]
        public void HttpPost_EncodeDecode_RoundTrip()
        {
            var obfs = ObfsFactory.GetObfs("http_post");
            var serverInfo = CreateServerInfo("example.com", 80, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            var original = new byte[2000];
            new Random(777).NextBytes(original);

            var encoded = obfs.ClientEncode(original, original.Length, out var encodedLen);
            Assert.IsTrue(encodedLen > 0, "HttpPost Encode should produce output");

            var decoded = obfs.ClientDecode(encoded, encodedLen, out var decodedLen, out var needSendBack);
            Assert.IsTrue(decodedLen > 0, "Should recover raw suffix from HTTP POST response");
        }

        // ============================================================
        // Test 9: Verify all registered Obfs types can be constructed
        // ============================================================
        [TestMethod]
        public void AllObfsTypes_Constructible()
        {
            var methods = new[]
            {
                "plain", "http_simple", "http_post", "random_head",
                "tls1.2_ticket_auth", "tls1.2_ticket_fastauth",
                "verify_deflate",
                "auth_sha1", "auth_sha1_v2", "auth_sha1_v4",
                "auth_aes128_md5", "auth_aes128_sha1",
                "auth_chain_a", "auth_chain_b", "auth_chain_c", "auth_chain_d", "auth_chain_e", "auth_chain_f",
                "auth_akarin_rand", "auth_akarin_spec_a"
            };

            foreach (var method in methods)
            {
                var obfs = ObfsFactory.GetObfs(method);
                Assert.IsNotNull(obfs, $"Obfs for '{method}' should not be null");
                Assert.AreEqual(method, obfs.Name(), $"Name should be '{method}'");

                var initData = obfs.InitData();
                // plain returns null for InitData — that's expected
                if (method != "plain")
                {
                    Assert.IsNotNull(initData, $"InitData for '{method}' should not be null");
                }

                var overhead = obfs.GetOverhead();
                Assert.IsTrue(overhead >= 0, $"Overhead for '{method}' should be >= 0");

                var serverInfo = CreateServerInfo("127.0.0.1", 8388, "", initData);
                obfs.SetServerInfo(serverInfo);

                var sentLength = obfs.GetSentLength();
                Assert.AreEqual(0L, sentLength);
            }
        }

        // ============================================================
        // Test 10: AuthSHA1 — construct + SetServerInfo smoke test
        // ============================================================
        [TestMethod]
        public void AuthSHA1_ConstructAndSetServerInfo()
        {
            var obfs = ObfsFactory.GetObfs("auth_sha1");
            var data = obfs.InitData();
            var serverInfo = CreateServerInfo("10.0.0.1", 8888, "", data);
            obfs.SetServerInfo(serverInfo);
            Assert.IsNotNull(obfs);
            Assert.IsNotNull(data);
        }

        // ============================================================
        // Test 11: AuthSHA1V2 — construct + smoke test
        // ============================================================
        [TestMethod]
        public void AuthSHA1V2_ConstructAndSetServerInfo()
        {
            var obfs = ObfsFactory.GetObfs("auth_sha1_v2");
            var data = obfs.InitData();
            var serverInfo = CreateServerInfo("10.0.0.1", 8888, "", data);
            obfs.SetServerInfo(serverInfo);
            Assert.IsNotNull(obfs);
            Assert.IsTrue(obfs.isAlwaysSendback());
        }

        // ============================================================
        // Test 12: AuthChain_a — construct + SetServerInfo smoke test
        // ============================================================
        [TestMethod]
        public void AuthChainA_ConstructAndSetServerInfo()
        {
            var obfs = ObfsFactory.GetObfs("auth_chain_a");
            var data = obfs.InitData();
            var serverInfo = CreateServerInfo("10.0.0.1", 8888, "", data);
            obfs.SetServerInfo(serverInfo);
            Assert.IsNotNull(obfs);
            Assert.IsTrue(obfs.GetOverhead() > 0);
        }

        // ============================================================
        // Test 13: AuthChain_b — construct + SetServerInfo smoke test
        // ============================================================
        [TestMethod]
        public void AuthChainB_ConstructAndSetServerInfo()
        {
            var obfs = ObfsFactory.GetObfs("auth_chain_b");
            var data = obfs.InitData();
            var serverInfo = CreateServerInfo("10.0.0.1", 8888, "", data);
            obfs.SetServerInfo(serverInfo);
            Assert.IsNotNull(obfs);
        }

        // ============================================================
        // Test 14: AuthAkarin — construct + SetServerInfo smoke test
        // ============================================================
        [TestMethod]
        public void AuthAkarin_ConstructAndSetServerInfo()
        {
            var obfs = ObfsFactory.GetObfs("auth_akarin_rand");
            var data = obfs.InitData();
            var serverInfo = CreateServerInfo("10.0.0.1", 8888, "", data);
            obfs.SetServerInfo(serverInfo);
            Assert.IsNotNull(obfs);
        }

        // ============================================================
        // Test 15: VerifyDeflate — compressed data round-trip (large payload)
        // ============================================================
        [TestMethod]
        public void VerifyDeflate_LargePayload_RoundTrip()
        {
            var obfs = ObfsFactory.GetObfs("verify_deflate");
            var serverInfo = CreateServerInfo("127.0.0.1", 9999, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            var original = new byte[4096];
            new Random(666).NextBytes(original);
            for (var i = 0; i < 100; i++) original[i] = 0x41;

            var packed = obfs.ClientPreEncrypt(original, original.Length, out var packedLen);
            Assert.IsTrue(packedLen > 0, "VerifyDeflate large PreEncrypt should produce output");

            var recovered = obfs.ClientPostDecrypt(packed, packedLen, out var recoveredLen);
            Assert.AreEqual(original.Length, recoveredLen, "Recovered length mismatch");
            for (var i = 0; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], recovered[i], $"VerifyDeflate large mismatch at byte {i}");
            }
        }

        // ============================================================
        // Test 16: Plain — multiple encode/decode cycles on same instance
        // ============================================================
        [TestMethod]
        public void Plain_MultipleCycles()
        {
            var obfs = ObfsFactory.GetObfs("plain");
            var serverInfo = CreateServerInfo("127.0.0.1", 8388, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            for (var cycle = 0; cycle < 5; cycle++)
            {
                var original = new byte[256];
                new Random(1000 + cycle).NextBytes(original);

                var encoded = obfs.ClientEncode(original, original.Length, out var encodedLen);
                Assert.IsTrue(encodedLen > 0, $"Cycle {cycle}: Encode should produce output");

                var decoded = obfs.ClientDecode(encoded, encodedLen, out var decodedLen, out var needSendBack);
                Assert.AreEqual(original.Length, decodedLen, $"Cycle {cycle}: length mismatch");
                for (var i = 0; i < original.Length; i++)
                {
                    Assert.AreEqual(original[i], decoded[i], $"Cycle {cycle} byte {i} mismatch");
                }
            }
        }

        // ============================================================
        // Test 17: AuthSHA1V4 — empty payload edge case
        // ============================================================
        [TestMethod]
        public void AuthSHA1V4_EmptyPayload()
        {
            var obfs = ObfsFactory.GetObfs("auth_sha1_v4");
            var serverInfo = CreateServerInfo("10.0.0.1", 443, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            var seedData = new byte[1];
            new Random(777).NextBytes(seedData);
            obfs.ClientPreEncrypt(seedData, seedData.Length, out _);

            var original = new byte[0];
            var packed = obfs.ClientPreEncrypt(original, original.Length, out var packedLen);
            Assert.IsTrue(packedLen >= 0, "Empty payload should not crash");
        }

        // ============================================================
        // Test 18: AuthAES128SHA1 — multiple packet round-trips
        // ============================================================
        [TestMethod]
        public void AuthAES128SHA1_MultiplePacket_RoundTrips()
        {
            var obfs = ObfsFactory.GetObfs("auth_aes128_sha1");
            var serverInfo = CreateServerInfo("10.0.0.1", 8388, "", obfs.InitData());
            obfs.SetServerInfo(serverInfo);

            var seed = new byte[5];
            new Random(888).NextBytes(seed);
            obfs.ClientPreEncrypt(seed, seed.Length, out _);

            var payloads = new byte[3][];
            for (var p = 0; p < 3; p++)
            {
                payloads[p] = new byte[150 + p * 50];
                new Random(900 + p).NextBytes(payloads[p]);
            }

            var totalPackedLen = 0;
            var packedBuf = new byte[65536];
            foreach (var payload in payloads)
            {
                var pkt = obfs.ClientPreEncrypt(payload, payload.Length, out var pktLen);
                Assert.IsTrue(pktLen > 0, $"Payload {payload.Length} should pack");
                Array.Copy(pkt, 0, packedBuf, totalPackedLen, pktLen);
                totalPackedLen += pktLen;
            }

            var recovered = obfs.ClientPostDecrypt(packedBuf, totalPackedLen, out var recoveredLen);

            var expectedTotal = 0;
            foreach (var p in payloads) expectedTotal += p.Length;
            Assert.AreEqual(expectedTotal, recoveredLen, "Total recovered length mismatch");

            var cursor = 0;
            for (var p = 0; p < 3; p++)
            {
                for (var i = 0; i < payloads[p].Length; i++)
                {
                    Assert.AreEqual(payloads[p][i], recovered[cursor + i],
                        $"Payload {p} mismatch at byte {i}");
                }
                cursor += payloads[p].Length;
            }
        }
    }
}
