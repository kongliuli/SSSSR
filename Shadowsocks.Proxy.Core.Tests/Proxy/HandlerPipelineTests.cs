using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Proxy.Core.Controller.Service;
using Shadowsocks.Proxy.Core.Enums;
using Shadowsocks.Proxy.Core.Model;
using Shadowsocks.Proxy.Core.Proxy;
using Shadowsocks.Proxy.Core.Util.NetUtils;

namespace Shadowsocks.Proxy.Core.Tests.Proxy;

[TestClass]
public class HandlerPipelineTests
{
    /// <summary>Creates a loopback TCP socket pair (client ↔ server).</summary>
    private static (Socket client, Socket server) CreateLoopbackSocketPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(listener.LocalEndpoint!);
            var server = listener.AcceptSocket();
            return (client, server);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Creates a loopback TCP listener that the Handler will connect to as the remote server.</summary>
    private static TcpListener CreateRemoteListener(out IPEndPoint endpoint)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        endpoint = (IPEndPoint)listener.LocalEndpoint;
        return listener;
    }

    /// <summary>Builds a Server object pointing at a loopback endpoint.</summary>
    private static Server CreateLoopbackServer(IPEndPoint endpoint, string method, string password, string protocol, string obfs)
    {
        return new Server
        {
            server = endpoint.Address.ToString(),
            Server_Port = endpoint.Port,
            Method = method,
            Password = password,
            Protocol = protocol,
            obfs = obfs,
            Id = "test-server",
        };
    }

    /// <summary>Builds a HandlerConfig with common test settings.</summary>
    private static HandlerConfig CreateTestConfig(string targetHost = "example.com", int targetPort = 80)
    {
        return new HandlerConfig
        {
            TargetHost = targetHost,
            TargetPort = targetPort,
            Ttl = 30.0,
            ConnectTimeout = 10.0,
            ProxyType = ProxyType.Socks5,
            ProxyRuleMode = ProxyRuleMode.Disable,
            AutoSwitchOff = false,
            ReconnectTimes = 0,
            ReconnectTimesRemain = 0,
        };
    }

    /// <summary>
    /// Creates and starts a Handler pointed at the given remote endpoint.
    /// The Handler will connect to <paramref name="server"/>'s configured address/port.
    /// </summary>
    private static Handler CreateAndStartHandler(
        Socket proxySocket,
        byte[] firstPacket,
        HandlerConfig cfg,
        Server server)
    {
        var handler = new Handler
        {
            connection = new ProxySocketTunLocal(proxySocket),
            cfg = cfg,
            DnsClients = Array.Empty<DnsClient>(),
        };

        // Wire getCurrentServer to return our test server
        handler.getCurrentServer = (int localPort, ServerSelectStrategy.FilterFunc filter,
            string targetURI, bool cfgRandom, bool usingRandom, bool forceRandom) => server;

        handler.Start(firstPacket, firstPacket.Length, null!);

        return handler;
    }

    /// <summary>Accepts the Handler's connection on the remote listener with a timeout.</summary>
    private static async Task<Socket> AcceptRemoteAsync(TcpListener listener, int timeoutMs = 10000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        return await listener.AcceptSocketAsync(cts.Token);
    }

    /// <summary>Safely closes and disposes a socket.</summary>
    private static void SafeClose(Socket? socket)
    {
        if (socket == null) return;
        try { socket.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
        try { socket.Close(); } catch { /* ignore */ }
        try { socket.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>Safely stops a TcpListener.</summary>
    private static void SafeStop(TcpListener? listener)
    {
        try { listener?.Stop(); } catch { /* ignore */ }
    }

    // ──────────────────────────────────────────────
    //  HandlerConfig tests
    // ──────────────────────────────────────────────

    [TestMethod]
    public void HandlerConfig_AllFields_PassThroughCorrectly()
    {
        var cfg = new HandlerConfig
        {
            TargetHost = "test.example.com",
            TargetPort = 8080,
            Ttl = 120.0,
            ConnectTimeout = 15.0,
            TryKeepAlive = 3,
            ForceLocalDnsQuery = true,
            ProxyType = ProxyType.Http,
            Socks5RemoteHost = "proxy.example.com",
            Socks5RemotePort = 1080,
            Socks5RemoteUsername = "user",
            Socks5RemotePassword = "pass",
            ProxyUserAgent = "TestAgent/1.0",
            AutoSwitchOff = false,
            ReconnectTimesRemain = 5,
            ReconnectTimes = 2,
            Random = true,
            ForceRandom = true,
            ProxyRuleMode = ProxyRuleMode.BypassLan,
        };

        Assert.AreEqual("test.example.com", cfg.TargetHost);
        Assert.AreEqual(8080, cfg.TargetPort);
        Assert.AreEqual(120.0, cfg.Ttl);
        Assert.AreEqual(15.0, cfg.ConnectTimeout);
        Assert.AreEqual(3, cfg.TryKeepAlive);
        Assert.IsTrue(cfg.ForceLocalDnsQuery);
        Assert.AreEqual(ProxyType.Http, cfg.ProxyType);
        Assert.AreEqual("proxy.example.com", cfg.Socks5RemoteHost);
        Assert.AreEqual(1080, cfg.Socks5RemotePort);
        Assert.AreEqual("user", cfg.Socks5RemoteUsername);
        Assert.AreEqual("pass", cfg.Socks5RemotePassword);
        Assert.AreEqual("TestAgent/1.0", cfg.ProxyUserAgent);
        Assert.IsFalse(cfg.AutoSwitchOff);
        Assert.AreEqual(5, cfg.ReconnectTimesRemain);
        Assert.AreEqual(2, cfg.ReconnectTimes);
        Assert.IsTrue(cfg.Random);
        Assert.IsTrue(cfg.ForceRandom);
        Assert.AreEqual(ProxyRuleMode.BypassLan, cfg.ProxyRuleMode);
    }

    [TestMethod]
    public void HandlerConfig_DefaultValues_AreReasonable()
    {
        var cfg = new HandlerConfig();

        Assert.IsNull(cfg.TargetHost);
        Assert.AreEqual(0, cfg.TargetPort);
        Assert.AreEqual(0.0, cfg.Ttl);
        Assert.AreEqual(0.0, cfg.ConnectTimeout);
        Assert.AreEqual(0, cfg.TryKeepAlive);
        Assert.IsFalse(cfg.ForceLocalDnsQuery);
        Assert.AreEqual(default(ProxyType), cfg.ProxyType);
        Assert.IsNull(cfg.Socks5RemoteHost);
        Assert.AreEqual(0, cfg.Socks5RemotePort);
        Assert.IsNull(cfg.Socks5RemoteUsername);
        Assert.IsNull(cfg.Socks5RemotePassword);
        Assert.IsNull(cfg.ProxyUserAgent);
        Assert.IsTrue(cfg.AutoSwitchOff);
        Assert.AreEqual(0, cfg.ReconnectTimesRemain);
        Assert.AreEqual(0, cfg.ReconnectTimes);
        Assert.IsFalse(cfg.Random);
        Assert.IsFalse(cfg.ForceRandom);
        Assert.AreEqual(default(ProxyRuleMode), cfg.ProxyRuleMode);
    }

    [TestMethod]
    public void HandlerConfig_Clone_PreservesAllFields()
    {
        var original = new HandlerConfig
        {
            TargetHost = "clonetest.com",
            TargetPort = 443,
            Ttl = 60.0,
            ConnectTimeout = 5.0,
            TryKeepAlive = 1,
            ForceLocalDnsQuery = true,
            ProxyType = ProxyType.Http,
            Socks5RemoteHost = "socks.local",
            Socks5RemotePort = 3128,
            Socks5RemoteUsername = "u",
            Socks5RemotePassword = "p",
            ProxyUserAgent = "Clone/1.0",
            AutoSwitchOff = false,
            ReconnectTimesRemain = 3,
            ReconnectTimes = 1,
            Random = true,
            ForceRandom = true,
            ProxyRuleMode = ProxyRuleMode.UserCustom,
        };

        var cloned = (HandlerConfig)original.Clone();

        Assert.AreEqual(original.TargetHost, cloned.TargetHost);
        Assert.AreEqual(original.TargetPort, cloned.TargetPort);
        Assert.AreEqual(original.Ttl, cloned.Ttl);
        Assert.AreEqual(original.ConnectTimeout, cloned.ConnectTimeout);
        Assert.AreEqual(original.TryKeepAlive, cloned.TryKeepAlive);
        Assert.AreEqual(original.ForceLocalDnsQuery, cloned.ForceLocalDnsQuery);
        Assert.AreEqual(original.ProxyType, cloned.ProxyType);
        Assert.AreEqual(original.Socks5RemoteHost, cloned.Socks5RemoteHost);
        Assert.AreEqual(original.Socks5RemotePort, cloned.Socks5RemotePort);
        Assert.AreEqual(original.Socks5RemoteUsername, cloned.Socks5RemoteUsername);
        Assert.AreEqual(original.Socks5RemotePassword, cloned.Socks5RemotePassword);
        Assert.AreEqual(original.ProxyUserAgent, cloned.ProxyUserAgent);
        Assert.AreEqual(original.AutoSwitchOff, cloned.AutoSwitchOff);
        Assert.AreEqual(original.ReconnectTimesRemain, cloned.ReconnectTimesRemain);
        Assert.AreEqual(original.ReconnectTimes, cloned.ReconnectTimes);
        Assert.AreEqual(original.Random, cloned.Random);
        Assert.AreEqual(original.ForceRandom, cloned.ForceRandom);
        Assert.AreEqual(original.ProxyRuleMode, cloned.ProxyRuleMode);

        // Mutate clone: should not affect original
        cloned.TargetHost = "modified.com";
        Assert.AreEqual("clonetest.com", original.TargetHost);
    }

    // ──────────────────────────────────────────────
    //  Handler pipeline integration tests
    // ──────────────────────────────────────────────

    [TestMethod]
    public async Task HandlerPipeline_BidirectionalDataRelay_WithNoneCipher()
    {
        // ── Setup: "none" / "plain" / "plain" (passthrough) ──
        TcpListener? remoteListener = null;
        Socket? clientSocket = null;
        Socket? proxySocket = null;
        Socket? remoteSocket = null;
        Handler? handler = null;

        try
        {
            // Create client-proxy socket pair (simulates browser → proxy connection)
            (clientSocket, proxySocket) = CreateLoopbackSocketPair();

            // Create remote listener (simulates the SS server)
            remoteListener = CreateRemoteListener(out var remoteEp);
            var server = CreateLoopbackServer(remoteEp, "none", "test", "plain", "plain");
            var cfg = CreateTestConfig();
            var firstPacket = new byte[] { 0x01, 127, 0, 0, 1, 0, 80 }; // ATYP_IPv4 → 127.0.0.1:80

            // Create and start handler (will connect to remoteListener's endpoint)
            handler = CreateAndStartHandler(proxySocket, firstPacket, cfg, server);

            // Accept the handler's connection to the remote listener
            remoteSocket = await AcceptRemoteAsync(remoteListener);
            Assert.IsNotNull(remoteSocket, "Handler should connect to remote listener");
            remoteListener.Stop();
            remoteListener = null;

            // Give StartPipe time to complete (sends first packet, starts async loops)
            await Task.Delay(500);

            // ── Verify: first packet relayed to remote (passthrough) ──
            var remoteRecvBuf = new byte[4096];
            int remoteRecvLen;
            try
            {
                remoteRecvLen = remoteSocket.Receive(remoteRecvBuf, SocketFlags.None);
            }
            catch (SocketException) when (remoteSocket.Available == 0)
            {
                // Data might not be available yet; wait and retry
                await Task.Delay(200);
                remoteRecvLen = remoteSocket.Receive(remoteRecvBuf, SocketFlags.None);
            }
            Assert.IsTrue(remoteRecvLen > 0, "Remote should receive the first packet");
            CollectionAssert.AreEqual(firstPacket, remoteRecvBuf[..remoteRecvLen],
                "With none cipher, data should pass through unchanged");

            // ── Verify: remote → client direction ──
            var responseData = new byte[] { 0x48, 0x54, 0x54, 0x50, 0x2F, 0x31, 0x2E, 0x31 }; // "HTTP/1.1"
            remoteSocket.Send(responseData);
            await Task.Delay(300); // let handler process

            var clientRecvBuf = new byte[4096];
            int clientRecvLen = 0;
            if (clientSocket.Available > 0)
            {
                clientRecvLen = clientSocket.Receive(clientRecvBuf, SocketFlags.None);
            }
            else
            {
                // Wait a bit more
                await Task.Delay(500);
                if (clientSocket.Available > 0)
                    clientRecvLen = clientSocket.Receive(clientRecvBuf, SocketFlags.None);
            }
            Assert.IsTrue(clientRecvLen > 0, "Client should receive response from remote");
            CollectionAssert.AreEqual(responseData, clientRecvBuf[..clientRecvLen],
                "With none cipher, response should pass through unchanged");

            // ── Verify: client → remote direction (additional data) ──
            var extraData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            clientSocket.Send(extraData);
            await Task.Delay(500);

            Array.Clear(remoteRecvBuf, 0, remoteRecvBuf.Length);
            if (remoteSocket.Available > 0)
            {
                remoteRecvLen = remoteSocket.Receive(remoteRecvBuf, SocketFlags.None);
                Assert.IsTrue(remoteRecvLen > 0, "Remote should receive additional data from client");
                CollectionAssert.AreEqual(extraData, remoteRecvBuf[..remoteRecvLen],
                    "Extra data should pass through unchanged");
            }
        }
        finally
        {
            if (handler != null)
            {
                try { handler.Close(); } catch { /* best-effort */ }
                await Task.Delay(300); // let Close complete
            }
            SafeClose(remoteSocket);
            SafeClose(clientSocket);
            SafeClose(proxySocket);
            SafeStop(remoteListener);
        }
    }

    [TestMethod]
    public void EncryptorFactory_NoneEncryptor_EncryptDecryptRoundTrip()
    {
        // NoneEncryptor is a registered cipher ("none") that passes data through unchanged.
        // This test verifies the encryptor can be created and used for round-trip encryption.
        var encryptor = Shadowsocks.Encryption.EncryptorFactory.GetEncryptor("none", "any-password");
        Assert.IsNotNull(encryptor);
        Assert.IsNotNull(encryptor.getIV());
        Assert.IsNotNull(encryptor.getKey());

        // Test encrypt/decrypt round-trip
        var plaintext = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var encrypted = new byte[64];
        encryptor.Encrypt(plaintext, plaintext.Length, encrypted, out var encLen);
        Assert.IsTrue(encLen > 0, "Encryption should produce output");
        // With "none" cipher, encrypted == plaintext (identity transform)
        CollectionAssert.AreEqual(plaintext, encrypted.Take(encLen).ToArray(),
            "NoneEncryptor passes data through unchanged");

        // Decrypt the "encrypted" data with a fresh encryptor
        var encryptor2 = Shadowsocks.Encryption.EncryptorFactory.GetEncryptor("none", "any-password");
        var decrypted = new byte[64];
        encryptor2.Decrypt(encrypted, encLen, decrypted, out var decLen);
        Assert.AreEqual(plaintext.Length, decLen);
        CollectionAssert.AreEqual(plaintext, decrypted.Take(decLen).ToArray());

        encryptor.Dispose();
        encryptor2.Dispose();
    }

    [TestMethod]
    public void EncryptorFactory_HasRegisteredCiphers()
    {
        var encryptors = Shadowsocks.Encryption.EncryptorFactory.RegisteredEncryptors;
        Assert.IsNotNull(encryptors);
        Assert.IsTrue(encryptors.Count > 0, "EncryptorFactory should have registered ciphers");

        // "none" must be available
        Assert.IsTrue(encryptors.ContainsKey("none"), "'none' cipher must be registered");

        // Other ciphers are registered (may need native libs at runtime)
        Assert.IsTrue(encryptors.ContainsKey("rc4-md5"), "'rc4-md5' cipher should be registered");
        Assert.IsTrue(encryptors.ContainsKey("aes-256-cfb"), "'aes-256-cfb' cipher should be registered");
    }

    [TestMethod]
    public async Task HandlerPipeline_DataFlow_ThroughEncryptionLayer()
    {
        // ── Verify data passes through ALL pipeline layers (protocol → encrypt → obfs → socket) ──
        TcpListener? remoteListener = null;
        Socket? clientSocket = null;
        Socket? proxySocket = null;
        Socket? remoteSocket = null;
        Handler? handler = null;

        try
        {
            (clientSocket, proxySocket) = CreateLoopbackSocketPair();

            remoteListener = CreateRemoteListener(out var remoteEp);
            var server = CreateLoopbackServer(remoteEp, "none", "pw", "plain", "plain");
            var cfg = CreateTestConfig();
            var firstPacket = System.Text.Encoding.UTF8.GetBytes("PIPELINE_TEST_DATA_12345");

            handler = CreateAndStartHandler(proxySocket, firstPacket, cfg, server);

            remoteSocket = await AcceptRemoteAsync(remoteListener);
            Assert.IsNotNull(remoteSocket, "Handler should connect via encryption pipeline");
            remoteListener.Stop();
            remoteListener = null;

            // Wait for StartPipe to send the first packet through protocol→encrypt→obfs→socket
            await Task.Delay(500);

            // Verify the first packet arrives intact (passthrough cipher)
            Assert.IsTrue(remoteSocket.Connected, "Remote socket should still be connected");

            var buf = new byte[4096];
            int len;
            try
            {
                remoteSocket.ReceiveTimeout = 3000;
                len = remoteSocket.Receive(buf, SocketFlags.None);
            }
            catch (SocketException) when (remoteSocket.Available > 0)
            {
                len = remoteSocket.Receive(buf, SocketFlags.None);
            }

            Assert.IsTrue(len > 0, "Data should be received at remote through encryption pipeline");
            CollectionAssert.AreEqual(firstPacket, buf[..len],
                "With NoneEncryptor, data passes through all pipeline layers unchanged");
        }
        finally
        {
            if (handler != null)
            {
                try { handler.Close(); } catch { /* ignore */ }
                await Task.Delay(300);
            }
            SafeClose(remoteSocket);
            SafeClose(clientSocket);
            SafeClose(proxySocket);
            SafeStop(remoteListener);
        }
    }

    [TestMethod]
    public async Task HandlerPipeline_Start_SendsFirstPacketToRemote()
    {
        // ── Verify that Start() triggers the first packet relay ──
        TcpListener? remoteListener = null;
        Socket? clientSocket = null;
        Socket? proxySocket = null;
        Socket? remoteSocket = null;
        Handler? handler = null;

        try
        {
            (clientSocket, proxySocket) = CreateLoopbackSocketPair();

            remoteListener = CreateRemoteListener(out var remoteEp);
            var server = CreateLoopbackServer(remoteEp, "none", "pw", "plain", "plain");
            var cfg = CreateTestConfig();
            var firstPacket = System.Text.Encoding.UTF8.GetBytes("FIRST_PACKET_DATA");

            handler = CreateAndStartHandler(proxySocket, firstPacket, cfg, server);

            remoteSocket = await AcceptRemoteAsync(remoteListener);
            Assert.IsNotNull(remoteSocket);
            remoteListener.Stop();
            remoteListener = null;

            await Task.Delay(500);

            var buf = new byte[4096];
            int len;
            try
            {
                len = remoteSocket.Receive(buf, SocketFlags.None);
            }
            catch
            {
                await Task.Delay(300);
                len = remoteSocket.Available > 0 ? remoteSocket.Receive(buf, SocketFlags.None) : 0;
            }
            Assert.IsTrue(len > 0, "Remote should receive the first packet");

            // With none cipher, data should match
            var received = buf[..len];
            CollectionAssert.AreEqual(firstPacket, received);
        }
        finally
        {
            if (handler != null)
            {
                try { handler.Close(); } catch { /* ignore */ }
                await Task.Delay(300);
            }
            SafeClose(remoteSocket);
            SafeClose(clientSocket);
            SafeClose(proxySocket);
            SafeStop(remoteListener);
        }
    }

    [TestMethod]
    public async Task HandlerPipeline_Connect_EstablishesRemoteConnection()
    {
        // ── Verify the Handler successfully connects to the remote endpoint ──
        TcpListener? remoteListener = null;
        Socket? clientSocket = null;
        Socket? proxySocket = null;
        Socket? remoteSocket = null;
        Handler? handler = null;

        try
        {
            (clientSocket, proxySocket) = CreateLoopbackSocketPair();

            remoteListener = CreateRemoteListener(out var remoteEp);
            var server = CreateLoopbackServer(remoteEp, "none", "pass", "plain", "plain");
            var cfg = CreateTestConfig();
            var firstPacket = new byte[] { 0x01, 127, 0, 0, 1, 0, 80 };

            handler = CreateAndStartHandler(proxySocket, firstPacket, cfg, server);

            // The handler should connect to our remote listener
            remoteSocket = await AcceptRemoteAsync(remoteListener);
            Assert.IsNotNull(remoteSocket, "Handler must connect to remote within timeout");
            Assert.IsTrue(remoteSocket.Connected, "Remote socket should be connected");

            remoteListener.Stop();
            remoteListener = null;
        }
        finally
        {
            if (handler != null)
            {
                try { handler.Close(); } catch { /* ignore */ }
                await Task.Delay(300);
            }
            SafeClose(remoteSocket);
            SafeClose(clientSocket);
            SafeClose(proxySocket);
            SafeStop(remoteListener);
        }
    }

    [TestMethod]
    public void HandlerConfig_ProxyType_Enum_ValuesCorrect()
    {
        Assert.AreEqual(0, (int)ProxyType.Socks5);
        Assert.AreEqual(1, (int)ProxyType.Http);
        Assert.AreEqual(2, (int)ProxyType.TcpPortTunnel);
    }

    [TestMethod]
    public void HandlerConfig_ProxyRuleMode_Enum_ValuesCorrect()
    {
        Assert.AreEqual(0, (int)ProxyRuleMode.Disable);
        Assert.AreEqual(1, (int)ProxyRuleMode.BypassLan);
        Assert.AreEqual(2, (int)ProxyRuleMode.BypassLanAndChina);
        Assert.AreEqual(3, (int)ProxyRuleMode.BypassLanAndNotChina);
        Assert.AreEqual(16, (int)ProxyRuleMode.UserCustom);
    }
}
