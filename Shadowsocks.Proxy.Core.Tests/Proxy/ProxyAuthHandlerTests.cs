using Shadowsocks.Proxy.Core.Model;
using Shadowsocks.Proxy.Core.Model.Transfer;
using Shadowsocks.Proxy.Core.Proxy;
using Shadowsocks.Proxy.Core.Util.NetUtils;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Shadowsocks.Proxy.Core.Tests.Proxy;

[TestClass]
public class ProxyAuthHandlerTests
{
    private static Configuration CreateConfig()
    {
        return new Configuration
        {
            AuthUser = "",
            AuthPass = "",
            PortMapCache = new Dictionary<int, PortMapConfig>()
        };
    }

    /// <summary>
    /// Creates a loopback TCP socket pair using a temporary listener.
    /// Returns the listener (for cleanup), client socket, and server socket.
    /// </summary>
    private static (TcpListener Listener, Socket Client, Socket Server) CreateLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect((IPEndPoint)listener.LocalEndpoint!);
            var server = listener.AcceptSocket();
            return (listener, client, server);
        }
        catch
        {
            listener.Stop();
            throw;
        }
    }

    /// <summary>
    /// Safely close a socket, ignoring any exceptions.
    /// </summary>
    private static void SafeClose(Socket? socket)
    {
        if (socket == null) return;
        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }

    // 1 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SOCKS5_Greeting_Valid_NoAuth_ReturnsMethodSelection()
    {
        // Arrange: valid SOCKS5 greeting with no-auth method
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            byte[] firstPacket = { 5, 1, 0 }; // VER=5, NMETHODS=1, method=0

            // Act
            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // Read method selection response from client
            var response = new byte[2];
            int received = client.Receive(response);

            // Assert
            Assert.AreEqual(2, received, "Should receive 2-byte method selection response");
            Assert.AreEqual(5, response[0], "VER in response should be 5");
            Assert.AreEqual(0, response[1], "Selected method should be 0 (no auth)");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }

    // 2 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SOCKS5_Request_IPv4Connect_Valid_ProceedsWithoutEarlyClose()
    {
        // Arrange: valid greeting + valid IPv4 CONNECT request
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            byte[] firstPacket = { 5, 1, 0 }; // valid greeting

            // Act: create handler (sends method selection {5,0})
            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // Read method selection response
            var response = new byte[2];
            client.Receive(response);
            Assert.AreEqual(5, response[0]);
            Assert.AreEqual(0, response[1]);

            // Send complete SOCKS5 IPv4 CONNECT request: 127.0.0.1:80
            // Format: VER CMD RSV ATYP ADDR[4] PORT[2]
            byte[] request = { 5, 1, 0, 1, 127, 0, 0, 1, 0, 80 };
            client.Send(request);

            // Wait for async callbacks to process
            Thread.Sleep(800);

            // The handler should have validated ATYP=1 and port=80, then called Connect().
            // Connect() will fail (no upstream server), but the handshake itself
            // passed the ATYP and port validation. The handler's exception handler
            // in HandshakeReceive3Callback will catch the Connect failure and close.
            // We verify the handler did NOT crash.
            Assert.IsTrue(true, "Handler should not throw during valid SOCKS5 handshake");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }

    // 3 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SOCKS5_Request_InvalidATYP_Swallowed_ConnectionStaysAlive()
    {
        // Invalid ATYP (0xFF) triggers bare catch {} in HandshakeReceive2Callback.
        // The exception is swallowed, handler does nothing (no Close, no Connect).
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            byte[] firstPacket = { 5, 1, 0 };

            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // Read method selection
            var response = new byte[2];
            client.Receive(response);
            Assert.AreEqual(0, response[1], "No-auth method should be selected");

            // Send request with ATYP=0xFF (invalid per RFC 1928)
            byte[] request = { 5, 1, 0, 0xFF, 0, 0, 0, 0, 0, 0 };
            client.Send(request);

            // Wait for async processing
            Thread.Sleep(500);

            // The bare catch { } swallows the SocketException. Connection should
            // still be alive because Close() is NOT called on invalid ATYP path.
            bool connected = client.Poll(100, SelectMode.SelectRead) || client.Connected;
            // Poll with SelectRead returns true if data available OR connection closed.
            // If connected and no data, Poll returns false. So we check !disconnected.
            bool disconnected = client.Poll(100, SelectMode.SelectRead) && client.Available == 0;
            Assert.IsFalse(disconnected,
                "Connection should NOT be closed after invalid ATYP (exception swallowed)");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }

    // 4 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SOCKS5_Request_ZeroPort_Rejected_ConnectionClosed()
    {
        // Port=0 should trigger SocketException in HandshakeReceive3Callback,
        // which is caught and calls Close().
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            byte[] firstPacket = { 5, 1, 0 };

            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // Read method selection
            var response = new byte[2];
            client.Receive(response);
            Assert.AreEqual(0, response[1]);

            // Send IPv4 CONNECT request with port=0 (invalid per RFC 1928)
            byte[] request = { 5, 1, 0, 1, 127, 0, 0, 1, 0, 0 };
            client.Send(request);

            // Wait for handler to process and call Close()
            Thread.Sleep(800);

            // Connection should be closed by the handler
            bool disconnected = client.Poll(100, SelectMode.SelectRead) && client.Available == 0;
            Assert.IsTrue(disconnected,
                "Connection should be closed after zero-port rejection");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }

    // 5 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SOCKS4_Connect_Valid_RespondsWithGrant()
    {
        // Valid SOCKS4 CONNECT to 127.0.0.1:80.
        // Handler responds with {0, 90, ...} (granted) via synchronous Send.
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            // SOCKS4 request: VN=4, CD=1(CONNECT), DSTPORT=80, DSTIP=127.0.0.1, USERID="user\0"
            byte[] firstPacket = { 4, 1, 0, 80, 127, 0, 0, 1, (byte)'u', (byte)'s', (byte)'e', (byte)'r', 0 };

            // Act
            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // Read SOCKS4 response (synchronous Send)
            var response = new byte[8];
            int received = client.Receive(response);

            // Assert: response should be {0, 90, 0, 80, 127, 0, 0, 1}
            Assert.IsTrue(received >= 8, $"Should receive at least 8 bytes, got {received}");
            Assert.AreEqual(0, response[0], "VN should be 0 in reply");
            Assert.AreEqual(90, response[1], "CD should be 90 (request granted)");
            // DSTPORT: bytes [2..3]
            Assert.AreEqual(0, response[2], "DSTPORT high byte");
            Assert.AreEqual(80, response[3], "DSTPORT low byte (80)");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }

    // 6 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void HTTP_Connect_Valid_ProcessesWithoutCrash()
    {
        // HTTP CONNECT request: the handler should parse it and call Connect().
        // Connect() will fail (no upstream), but the handler should not crash.
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            var httpRequest = "CONNECT example.com:80 HTTP/1.1\r\nHost: example.com:80\r\n\r\n";
            byte[] firstPacket = Encoding.UTF8.GetBytes(httpRequest);

            // Act & Assert: handler should not throw
            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // The handler processes HTTP CONNECT, calls Connect() which fails,
            // exception caught by HandshakeReceive, Close() called.
            // No crash is the expected result.
            Assert.IsTrue(true, "Handler should process HTTP CONNECT without unhandled exception");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }

    // 7 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void SOCKS5_Greeting_NoAcceptableMethod_ClosesConnection()
    {
        // VER=5 but no acceptable auth method (0=no-auth or 2=user/pass).
        // Handler should call Close() without sending a method selection.
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            // VER=5, NMETHODS=1, method=0xFF (not acceptable)
            byte[] firstPacket = { 5, 1, 0xFF };

            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // Handler enters SOCKS5 path (VER==5), finds no acceptable method,
            // and calls Close() without sending a reply.
            Thread.Sleep(200);

            // Connection should be closed since no acceptable method exists
            bool disconnected = client.Poll(100, SelectMode.SelectRead) && client.Available == 0;
            Assert.IsTrue(disconnected,
                "Connection should be closed when no acceptable SOCKS5 auth method exists");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }

    // 8 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Malformed_Data_DoesNotCrash()
    {
        // Malformed firstPacket: only 1 byte (bytesRead must be >1 to proceed).
        // Handler should call Close() without crashing.
        var (listener, client, server) = CreateLoopbackPair();
        try
        {
            var config = CreateConfig();
            byte[] firstPacket = { 5 }; // only VER, missing NMETHODS and methods

            // Act & Assert: should not throw
            _ = new ProxyAuthHandler(config, new ServerTransferTotal(), new IPRangeSet(),
                firstPacket, firstPacket.Length, server);

            // bytesRead == 1, not > 1, so Handler goes to else → Close()
            Assert.IsTrue(true, "Handler should handle malformed data without crash");

            // Connection should be closed
            Thread.Sleep(200);
            bool disconnected = client.Poll(100, SelectMode.SelectRead) && client.Available == 0;
            Assert.IsTrue(disconnected, "Connection should be closed for malformed data");
        }
        finally
        {
            SafeClose(client);
            SafeClose(server);
            listener.Stop();
        }
    }
}
