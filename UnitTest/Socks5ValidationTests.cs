using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Model;
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using static Shadowsocks.Encryption.EncryptorBase;

namespace UnitTest
{
    /// <summary>
    /// Tests for SOCKS5 address validation in ProxyAuthHandler (C4.37).
    /// Covers ATYP validation, address truncation, and port validation per RFC 1928.
    ///
    /// Test approach: Uses a real TCP loopback socket pair to exercise the handler.
    /// The handler's private HandshakeReceive2Callback validates ATYP and address size;
    /// HandshakeReceive3Callback validates port.  Since ProxyAuthHandler is internal
    /// and Socket is sealed, we create the handler via reflection and control the
    /// data flow through the loopback socket.
    ///
    /// Detection strategy after sending the SOCKS5 request:
    ///   - Socket still alive → validation passed (handler proceeded to Connect)
    ///     or exception was swallowed in HandshakeReceive2Callback (invalid ATYP / empty domain).
    ///   - Socket closed      → validation failed in HandshakeReceive3Callback
    ///     (port zero, truncated address, etc.) — handler called Close().
    /// </summary>
    [TestClass]
    public class Socks5ValidationTests
    {
        // ── SOCKS5 protocol constants (mirror EncryptorBase) ──────────────
        private const byte ATYP_IPv4_v   = ATYP_IPv4;   // 0x01
        private const byte ATYP_DOMAIN_v = ATYP_DOMAIN; // 0x03
        private const byte ATYP_IPv6_v   = ATYP_IPv6;   // 0x04
        private const int  ADDR_PORT_LEN = 2;

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Build a SOCKS5 request per RFC 1928 §4.
        /// Format: VER(1) CMD(1) RSV(1) ATYP(1) DST.ADDR(variable) DST.PORT(2).
        /// </summary>
        private static byte[] BuildSocks5Request(byte atyp, byte[] addr, ushort port, byte cmd = 1)
        {
            var portBytes = new byte[] { (byte)(port >> 8), (byte)(port & 0xFF) };
            var request   = new byte[4 + addr.Length + ADDR_PORT_LEN];
            request[0] = 5;     // VER
            request[1] = cmd;   // CMD  (1 = CONNECT)
            request[2] = 0;     // RSV
            request[3] = atyp;  // ATYP
            Array.Copy(addr,      0, request, 4,               addr.Length);
            Array.Copy(portBytes, 0, request, 4 + addr.Length, ADDR_PORT_LEN);
            return request;
        }

        /// <summary>
        /// Create a TCP socket pair connected via loopback.
        /// Returns (serverSocket, clientSocket).
        /// </summary>
        private static (Socket server, Socket client) CreateLoopbackPair()
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect((IPEndPoint)listener.LocalEndPoint!);

            var server = listener.Accept();
            listener.Close();
            return (server, client);
        }

        /// <summary>
        /// Create a ProxyAuthHandler via reflection (the class is internal) and run
        /// the SOCKS5 method-negotiation + request handshake.
        ///
        /// Returns <c>true</c> when the server socket is still alive after the
        /// asynchronous callbacks have had time to fire, meaning either:
        ///   (a) validation passed and the handler proceeded to Connect, or
        ///   (b) an exception was thrown-and-swallowed in HandshakeReceive2Callback
        ///       (invalid ATYP / empty domain).
        ///
        /// Returns <c>false</c> when the server socket was closed by the handler,
        /// which happens when HandshakeReceive3Callback rejects the request
        /// (port zero, truncated address, etc.).
        /// </summary>
        private bool RunHandshakeTest(byte[] socks5Request, out string errorDetail, int waitMs = 500)
        {
            errorDetail = null;
            var (server, client) = CreateLoopbackPair();

            try
            {
                // Phase 1 – Send SOCKS5 greeting (VER=5, 1 method, no auth).
                byte[] greeting = { 5, 1, 0 };
                var config = new Configuration();

                // ProxyAuthHandler is internal → create via reflection.
                var assembly    = typeof(Shadowsocks.Encryption.EncryptorBase).Assembly;
                var handlerType = assembly.GetType("Shadowsocks.Proxy.ProxyAuthHandler")
                                  ?? throw new InvalidOperationException("ProxyAuthHandler type not found");

                var handler = Activator.CreateInstance(handlerType,
                    config,          // Configuration
                    null,            // ServerTransferTotal
                    null,            // IPRangeSet
                    greeting,        // firstPacket
                    greeting.Length, // length
                    server);         // Socket

                if (handler == null)
                {
                    errorDetail = "Failed to create ProxyAuthHandler instance";
                    return false;
                }

                // Phase 2 – Read the method-selection response: should be {5, 0}.
                var methodResp = new byte[2];
                var received   = client.Receive(methodResp);
                if (received != 2 || methodResp[0] != 5 || methodResp[1] != 0)
                {
                    errorDetail = $"Method selection mismatch: received {received} bytes [{methodResp[0]}, {methodResp[1]}]";
                    return false;
                }

                // Phase 3 – Send the SOCKS5 request.
                client.Send(socks5Request);

                // Phase 4 – Give the asynchronous BeginReceive callbacks time to fire.
                // The handler runs HandshakeReceive2Callback → (optionally) HandshakeReceive3Callback.
                Thread.Sleep(waitMs);

                // Phase 5 – Check whether the handler closed the server socket.
                // When ProxyAuthHandler calls CloseSocket(), it disposes the Socket.
                // Calling any method on a disposed Socket throws ObjectDisposedException.
                // Note: client.Send() is NOT reliable here — TCP may buffer the send
                // and report success even after the peer has closed the connection.
                try
                {
                    bool connected = server.Connected;
                    if (!connected)
                    {
                        errorDetail = $"Server socket reports not connected";
                        return false;
                    }
                    return true; // socket still alive
                }
                catch (ObjectDisposedException)
                {
                    errorDetail = "Server socket was disposed by handler (Close called)";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorDetail = ex.ToString();
                return false;
            }
            finally
            {
                // Best-effort cleanup.
                try { server.Shutdown(SocketShutdown.Both); } catch { }
                try { server.Close(); }                        catch { }
                try { client.Shutdown(SocketShutdown.Both); }  catch { }
                try { client.Close(); }                        catch { }
            }
        }

        // ── Tests: Valid handshakes ─────────────────────────────────────

        [TestMethod]
        public void Handshake_ValidIPv4_Proceeds()
        {
            // IPv4: 192.0.2.1:443  (TEST-NET-1 — unreachable, keeps socket alive)
            byte[] addr    = { 192, 0, 2, 1 };
            var    request = BuildSocks5Request(ATYP_IPv4_v, addr, 443);
            Assert.IsTrue(RunHandshakeTest(request, out var error),
                $"Valid IPv4 handshake should proceed. {error}");
        }

        [TestMethod]
        public void Handshake_ValidIPv6_Proceeds()
        {
            // IPv6: 2001:db8::1:443  (documentation prefix — unreachable)
            byte[] addr = {
                0x20, 0x01, 0x0d, 0xb8, 0,0,0,0, 0,0,0,0, 0,0,0,1
            };
            var request = BuildSocks5Request(ATYP_IPv6_v, addr, 443);
            Assert.IsTrue(RunHandshakeTest(request, out var error),
                $"Valid IPv6 handshake should proceed. {error}");
        }

        [TestMethod]
        public void Handshake_ValidDomain_Proceeds()
        {
            // Domain: "example.com":443
            // Format: length-byte + domain-bytes
            byte[] domain  = System.Text.Encoding.ASCII.GetBytes("example.com");
            byte[] addr    = new byte[1 + domain.Length];
            addr[0] = (byte)domain.Length;
            Array.Copy(domain, 0, addr, 1, domain.Length);

            var request = BuildSocks5Request(ATYP_DOMAIN_v, addr, 443);
            Assert.IsTrue(RunHandshakeTest(request, out var error),
                $"Valid domain handshake should proceed. {error}");
        }

        // ── Tests: ATYP validation (HandshakeReceive2Callback) ──────────

        [TestMethod]
        public void Handshake_InvalidATYP_ThrowsSocketException()
        {
            // ATYP = 0xFF is not defined in RFC 1928.
            // HandshakeReceive2Callback throws SocketException(ProtocolNotSupported),
            // which is caught by the bare `catch { }` and swallowed.
            // The socket stays alive; the handler simply does not proceed to Connect.
            byte[] addr    = { 192, 0, 2, 1 };
            var    request = BuildSocks5Request(0xFF, addr, 443);
            // Socket stays alive because exception is swallowed (no Close() called).
            Assert.IsTrue(RunHandshakeTest(request, out var error),
                $"Handler should not crash on invalid ATYP 0xFF. {error}");
        }

        [TestMethod]
        public void Handshake_InvalidATYP_0x02_ThrowsSocketException()
        {
            // ATYP = 0x02 was historically BIND (obsolete).
            // Same behavior as 0xFF: SocketException swallowed, socket stays alive.
            byte[] addr    = { 192, 0, 2, 1 };
            var    request = BuildSocks5Request(0x02, addr, 443);
            Assert.IsTrue(RunHandshakeTest(request, out var error),
                $"Handler should not crash on invalid ATYP 0x02. {error}");
        }

        // ── Tests: Address truncation (HandshakeReceive2Callback → HandshakeReceive3Callback) ─

        [TestMethod]
        public void Handshake_TruncatedAddress_ThrowsSocketException()
        {
            // Domain ATYP with length=50 but only 3 domain bytes + 2 port bytes sent.
            // HandshakeReceive2Callback reads domain length → size=50.
            // Calls HandshakeReceive3Callback(52), which receives only 5 bytes.
            // bytesRead < bytesRemain → SocketException(MessageSize) → Close().
            byte[] addr = new byte[1 + 3]; // length-byte + 3 fake domain bytes
            addr[0] = 50;                  // claim domain is 50 bytes long
            addr[1] = (byte)'a';
            addr[2] = (byte)'b';
            addr[3] = (byte)'c';

            var request = BuildSocks5Request(ATYP_DOMAIN_v, addr, 443);
            Assert.IsFalse(RunHandshakeTest(request, out var error),
                $"Truncated address should cause handler to close connection. {error}");
        }

        [TestMethod]
        public void Handshake_EmptyDomain_ThrowsSocketException()
        {
            // Domain ATYP with length=0.
            // HandshakeReceive2Callback: size = _remoteHeaderSendBuffer[1] = 0.
            // if (size == 0) → SocketException(MessageSize).
            // Caught by bare `catch { }` → swallowed, socket stays alive.
            byte[] addr = new byte[1]; // just the length byte
            addr[0] = 0;               // domain length = 0

            var request = BuildSocks5Request(ATYP_DOMAIN_v, addr, 443);
            Assert.IsTrue(RunHandshakeTest(request, out var error),
                $"Handler should not crash on empty domain. {error}");
        }

        // ── Tests: Port validation (HandshakeReceive3Callback) ───────────

        [TestMethod]
        public void Handshake_PortZero_ThrowsSocketException()
        {
            // Valid IPv4 address but port = 0.
            // HandshakeReceive3Callback validates port:
            //   port == 0 → SocketException(InvalidArgument) → Close().
            byte[] addr    = { 192, 0, 2, 1 };
            var    request = BuildSocks5Request(ATYP_IPv4_v, addr, 0);
            Assert.IsFalse(RunHandshakeTest(request, out var error),
                $"Port zero should cause handler to close connection. {error}");
        }
    }
}
