using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Controller.Service;
using Shadowsocks.Model;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UnitTest
{
    [TestClass]
    public class UdpListenerTests
    {
        // Ports used across tests — spread across the ephemeral range to avoid conflicts.
        private const int UdpPort1 = 54321;
        private const int TcpPort2 = 54322;
        private const int UdpPort3 = 54323;
        private const int UdpPort4 = 54324;
        private const int UdpPort5 = 54325;
        private const int UdpPort6 = 54326;
        private const int UdpPort7a = 54327;
        private const int UdpPort7b = 54328;

        /// <summary>Minimal service stub so the Listener has at least one handler.</summary>
        private sealed class StubService : Listener.IService
        {
            public bool Handle(byte[] firstPacket, int length, Socket socket) => false;
            public void Stop() { }
        }

        /// <summary>Create a Listener with a stub service and start it, returning the instance.</summary>
        private static Listener CreateAndStart(Configuration config, int port)
        {
            var listener = new Listener(new List<Listener.IService> { new StubService() });
            listener.Start(config, port);
            return listener;
        }

        // ── 1. StartListener_WithUdpPort_StartsUdpSocket ──────────────────────

        [TestMethod]
        public void StartListener_WithUdpPort_StartsUdpSocket()
        {
            // Arrange: explicit UDP port, TCP on a random port
            var config = new Configuration
            {
                LocalPort = 0,
                ShareOverLan = false,
                UdpPort = UdpPort1
            };

            Listener listener = null;
            try
            {
                // Act
                listener = CreateAndStart(config, port: 0);

                // Assert
                Assert.IsNotNull(listener.UdpSocket,
                    "UdpSocket should be created when UdpPort is explicitly set.");
                Assert.IsTrue(listener.UdpSocket.IsBound,
                    "UdpSocket should be bound after Start().");
            }
            finally
            {
                listener?.Stop();
            }
        }

        // ── 2. StartListener_WithUdpPortZero_UsesTcpPort ──────────────────────

        [TestMethod]
        public void StartListener_WithUdpPortZero_UsesTcpPort()
        {
            // Arrange: UdpPort=0 means "use the same port as TCP"
            var config = new Configuration
            {
                LocalPort = 0,
                ShareOverLan = false,
                UdpPort = 0
            };

            Listener listener = null;
            try
            {
                // Pass a specific port so we can verify UDP matches it.
                listener = CreateAndStart(config, port: TcpPort2);

                // Assert
                Assert.IsNotNull(listener.UdpSocket,
                    "UdpSocket should be created even when UdpPort=0.");
                Assert.IsTrue(listener.UdpSocket.IsBound,
                    "UdpSocket should be bound.");
                var udpEndPoint = (IPEndPoint)listener.UdpSocket.LocalEndPoint;
                Assert.AreEqual(TcpPort2, udpEndPoint.Port,
                    "When UdpPort=0, the UDP socket should bind to the same port as TCP.");
            }
            finally
            {
                listener?.Stop();
            }
        }

        // ── 3. UdpSocket_BindsToCorrectPort ───────────────────────────────────

        [TestMethod]
        public void UdpSocket_BindsToCorrectPort()
        {
            // Arrange
            var config = new Configuration
            {
                LocalPort = 0,
                ShareOverLan = false,
                UdpPort = UdpPort3
            };

            Listener listener = null;
            try
            {
                // Act
                listener = CreateAndStart(config, port: 0);

                // Assert
                Assert.IsNotNull(listener.UdpSocket,
                    "UdpSocket should be created.");
                var endPoint = (IPEndPoint)listener.UdpSocket.LocalEndPoint;
                Assert.AreEqual(UdpPort3, endPoint.Port,
                    $"UdpSocket.LocalEndPoint.Port should equal the configured UdpPort ({UdpPort3}).");
                Assert.AreEqual(IPAddress.Loopback, endPoint.Address,
                    "When ShareOverLan=false, UdpSocket should bind to Loopback.");
            }
            finally
            {
                listener?.Stop();
            }
        }

        // ── 4. StopListener_ClosesUdpSocket ───────────────────────────────────

        [TestMethod]
        public void StopListener_ClosesUdpSocket()
        {
            // Arrange
            var config = new Configuration
            {
                LocalPort = 0,
                ShareOverLan = false,
                UdpPort = UdpPort4
            };

            var listener = CreateAndStart(config, port: 0);
            Assert.IsNotNull(listener.UdpSocket,
                "Precondition: UdpSocket should be created before Stop().");

            // Act
            listener.Stop();

            // Assert: the property returns null after Stop() closes and nulls the field.
            Assert.IsNull(listener.UdpSocket,
                "After Stop(), UdpSocket property should return null (socket closed and released).");
        }

        // ── 5. StartListener_WithPortConflict_DoesNotThrow ────────────────────

        [TestMethod]
        public void StartListener_WithPortConflict_DoesNotThrow()
        {
            // Arrange: pre-bind a UDP socket WITHOUT ReuseAddress.
            // The Listener sets ReuseAddress on its socket, but on Windows a
            // non-reuse-address bind wins — subsequent ReuseAddress binds to the
            // same port will fail with AddressInUse.
            using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            blocker.Bind(new IPEndPoint(IPAddress.Loopback, UdpPort5));

            var config = new Configuration
            {
                LocalPort = 0,
                ShareOverLan = false,
                UdpPort = UdpPort5
            };

            Listener listener = null;
            try
            {
                // Act: starting the listener must NOT throw even though UDP port is taken.
                listener = new Listener(new List<Listener.IService> { new StubService() });
                listener.Start(config, port: 0);

                // Assert: TCP listener still works (Start returned without exception).
                // The UDP binding should have failed silently and set _udpSocket to null.
                Assert.IsNull(listener.UdpSocket,
                    "UdpSocket should be null when UDP binding fails due to port conflict.");
            }
            finally
            {
                listener?.Stop();
            }
        }

        // ── 6. UdpSocket_SendsAndReceivesDatagram_Loopback ────────────────────

        [TestMethod]
        public void UdpSocket_SendsAndReceivesDatagram_Loopback()
        {
            // Arrange
            var config = new Configuration
            {
                LocalPort = 0,
                ShareOverLan = false,
                UdpPort = UdpPort6
            };

            Listener listener = null;
            try
            {
                listener = CreateAndStart(config, port: 0);
                Assert.IsNotNull(listener.UdpSocket,
                    "Precondition: UdpSocket should be created.");

                var testMessage = Encoding.UTF8.GetBytes("Hello UDP!");

                // Act: send a datagram from a separate socket to the listener's UDP socket.
                using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                var targetEndPoint = new IPEndPoint(IPAddress.Loopback, UdpPort6);
                sender.SendTo(testMessage, targetEndPoint);

                // Receive the datagram on the listener's bound UDP socket.
                var buffer = new byte[1024];
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                listener.UdpSocket.ReceiveTimeout = 2000;
                var received = listener.UdpSocket.ReceiveFrom(buffer, ref remoteEndPoint);

                // Assert
                Assert.AreEqual(testMessage.Length, received,
                    "Should receive the same number of bytes as sent.");
                for (var i = 0; i < testMessage.Length; i++)
                {
                    Assert.AreEqual(testMessage[i], buffer[i],
                        $"Byte at index {i} should match the sent datagram.");
                }
            }
            finally
            {
                listener?.Stop();
            }
        }

        // ── 7. UdpListener_SurvivesStopAndRestart ─────────────────────────────

        [TestMethod]
        public void UdpListener_SurvivesStopAndRestart()
        {
            // Arrange
            var config = new Configuration
            {
                LocalPort = 0,
                ShareOverLan = false,
                UdpPort = UdpPort7a
            };

            var listener = new Listener(new List<Listener.IService> { new StubService() });
            try
            {
                // ── First cycle ──
                listener.Start(config, port: 0);
                Assert.IsNotNull(listener.UdpSocket,
                    "First start: UdpSocket should be created.");
                Assert.IsTrue(listener.UdpSocket.IsBound,
                    "First start: UdpSocket should be bound.");

                listener.Stop();
                Assert.IsNull(listener.UdpSocket,
                    "After first stop: UdpSocket should be null.");

                // ── Second cycle with a different UDP port ──
                config.UdpPort = UdpPort7b;
                listener.Start(config, port: 0);
                Assert.IsNotNull(listener.UdpSocket,
                    "Restart: UdpSocket should be created again.");
                Assert.IsTrue(listener.UdpSocket.IsBound,
                    "Restart: UdpSocket should be bound.");
                var endPoint = (IPEndPoint)listener.UdpSocket.LocalEndPoint;
                Assert.AreEqual(UdpPort7b, endPoint.Port,
                    "Restart: UdpSocket should bind to the updated UdpPort.");
            }
            finally
            {
                listener?.Stop();
            }
        }
    }
}
