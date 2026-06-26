using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Model;
using System;

namespace UnitTest
{
    [TestClass]
    public class ServerTest
    {
        [TestMethod]
        public void TestServerFromSSR()
        {
            var server = new Server();
            var nornameCase = "ssr://MTI3LjAuMC4xOjEyMzQ6YXV0aF9hZXMxMjhfbWQ1OmFlcy0xMjgtY2ZiOnRsczEuMl90aWNrZXRfYXV0aDpZV0ZoWW1KaS8_b2Jmc3BhcmFtPVluSmxZV3QzWVRFeExtMXZaUQ";

            server.ServerFromSsr(nornameCase, "");

            Assert.AreEqual(server.server, "127.0.0.1");
            Assert.AreEqual(server.Server_Port, (ushort)1234);
            Assert.AreEqual(server.Protocol, "auth_aes128_md5");
            Assert.AreEqual(server.Method, "aes-128-cfb");
            Assert.AreEqual(server.obfs, "tls1.2_ticket_auth");
            Assert.AreEqual(server.ObfsParam, "breakwa11.moe");
            Assert.AreEqual(server.Password, "aaabbb");

            server = new Server();
            const string normalCaseWithRemark = "ssr://MTI3LjAuMC4xOjEyMzQ6YXV0aF9hZXMxMjhfbWQ1OmFlcy0xMjgtY2ZiOnRsczEuMl90aWNrZXRfYXV0aDpZV0ZoWW1KaS8_b2Jmc3BhcmFtPVluSmxZV3QzWVRFeExtMXZaUSZyZW1hcmtzPTVyV0w2Sy1WNUxpdDVwYUg";

            server.ServerFromSsr(normalCaseWithRemark, "firewallAirport");

            Assert.AreEqual(server.server, "127.0.0.1");
            Assert.AreEqual<ushort>(server.Server_Port, 1234);
            Assert.AreEqual(server.Protocol, "auth_aes128_md5");
            Assert.AreEqual(server.Method, "aes-128-cfb");
            Assert.AreEqual(server.obfs, "tls1.2_ticket_auth");
            Assert.AreEqual(server.ObfsParam, "breakwa11.moe");
            Assert.AreEqual(server.Password, "aaabbb");

            Assert.AreEqual(server.Remarks, "测试中文");
            Assert.AreEqual(server.Group, string.Empty);
            Assert.AreEqual(server.SubTag, "firewallAirport");
        }

        [TestMethod]
        public void TestBadPortNumber()
        {
            var server = new Server();

            const string link = "ssr://MTI3LjAuMC4xOjgwOmF1dGhfc2hhMV92NDpjaGFjaGEyMDpodHRwX3NpbXBsZTplaWZnYmVpd3ViZ3IvP29iZnNwYXJhbT0mcHJvdG9wYXJhbT0mcmVtYXJrcz0mZ3JvdXA9JnVkcHBvcnQ9NDY0MzgxMzYmdW90PTQ2MDA3MTI4";
            try
            {
                server.ServerFromSsr(link, "");
            }
            catch (OverflowException e)
            {
                Console.Write(e.ToString());
            }

        }

        [TestMethod]
        public void SSRUrl_RoundTrip()
        {
            var original = new Server
            {
                server = "192.168.1.100",
                Server_Port = 8888,
                Password = "roundtrip_password",
                Method = "aes-256-cfb",
                Protocol = "auth_aes128_md5",
                obfs = "tls1.2_ticket_auth",
                ObfsParam = "test-obfs-param",
            };

            var ssrLink = original.SsrLink;
            Assert.IsTrue(ssrLink.StartsWith("ssr://"), "SSRLink should produce an ssr:// URL");

            var parsed = new Server();
            parsed.ServerFromSsr(ssrLink, "");

            Assert.AreEqual(original.server, parsed.server, "Server address mismatch");
            Assert.AreEqual(original.Server_Port, parsed.Server_Port, "Port mismatch");
            Assert.AreEqual(original.Password, parsed.Password, "Password mismatch");
            Assert.AreEqual(original.Method, parsed.Method, "Method mismatch");
            Assert.AreEqual(original.Protocol, parsed.Protocol, "Protocol mismatch");
            Assert.AreEqual(original.obfs, parsed.obfs, "Obfs mismatch");
            Assert.AreEqual(original.ObfsParam, parsed.ObfsParam, "ObfsParam mismatch");
        }

        [TestMethod]
        public void SSRUrl_WithAllFields_RoundTrip()
        {
            var original = new Server
            {
                server = "10.0.0.1",
                Server_Port = 443,
                Server_Udp_Port = 444,
                Password = "all_fields_pwd",
                Method = "chacha20-ietf",
                Protocol = "auth_sha1_v4",
                ProtocolParam = "proto-param-value",
                obfs = "http_simple",
                ObfsParam = "obfs-param-value",
                Remarks = "My Test Server",
                Group = "TestGroup",
                UdpOverTcp = true,
            };

            var ssrLink = original.SsrLink;
            Assert.IsTrue(ssrLink.StartsWith("ssr://"), "SSRLink should produce an ssr:// URL");

            var parsed = new Server();
            parsed.ServerFromSsr(ssrLink, "");

            Assert.AreEqual(original.server, parsed.server, "Server address mismatch");
            Assert.AreEqual(original.Server_Port, parsed.Server_Port, "Port mismatch");
            Assert.AreEqual(original.Server_Udp_Port, parsed.Server_Udp_Port, "UDP port mismatch");
            Assert.AreEqual(original.Password, parsed.Password, "Password mismatch");
            Assert.AreEqual(original.Method, parsed.Method, "Method mismatch");
            Assert.AreEqual(original.Protocol, parsed.Protocol, "Protocol mismatch");
            Assert.AreEqual(original.ProtocolParam, parsed.ProtocolParam, "ProtocolParam mismatch");
            Assert.AreEqual(original.obfs, parsed.obfs, "Obfs mismatch");
            Assert.AreEqual(original.ObfsParam, parsed.ObfsParam, "ObfsParam mismatch");
            Assert.AreEqual(original.Remarks, parsed.Remarks, "Remarks mismatch");
            Assert.AreEqual(original.Group, parsed.Group, "Group mismatch");
            Assert.AreEqual(original.UdpOverTcp, parsed.UdpOverTcp, "UdpOverTcp mismatch");
        }

        [TestMethod]
        public void FriendlyName_Returns_ServerNameOrAddress()
        {
            // With Remarks set, FriendlyName returns the Remarks value
            var serverWithRemarks = new Server
            {
                server = "10.0.0.1",
                Server_Port = 8388,
                Remarks = "My Custom Name",
            };

            Assert.AreEqual("My Custom Name", serverWithRemarks.FriendlyName,
                "FriendlyName should return Remarks when set");

            // Without Remarks, FriendlyName returns server:port
            var serverWithoutRemarks = new Server
            {
                server = "192.168.1.1",
                Server_Port = 443,
            };

            Assert.AreEqual("192.168.1.1:443", serverWithoutRemarks.FriendlyName,
                "FriendlyName should return server:port when Remarks is empty");

            // IPv6 address should be wrapped in brackets
            var serverIPv6 = new Server
            {
                server = "::1",
                Server_Port = 1080,
            };

            Assert.AreEqual("[::1]:1080", serverIPv6.FriendlyName,
                "FriendlyName should wrap IPv6 addresses in brackets");
        }

        [TestMethod]
        public void ForwardServer_ReturnsCorrectClone()
        {
            var forward = Server.ForwardServer;

            Assert.IsNotNull(forward, "ForwardServer should not be null");
            Assert.AreEqual("server host", forward.server, "ForwardServer should have default server address");
            Assert.AreEqual((ushort)8388, forward.Server_Port, "ForwardServer should have default port 8388");
            Assert.AreEqual("aes-256-cfb", forward.Method, "ForwardServer should have default method");
            Assert.AreEqual("origin", forward.Protocol, "ForwardServer should have default protocol");
            Assert.AreEqual("plain", forward.obfs, "ForwardServer should have default obfs");
            Assert.AreEqual("0", forward.Password, "ForwardServer should have default password");
            Assert.AreEqual("Default Group", forward.Group, "ForwardServer should have default group");
            Assert.IsTrue(forward.Enable, "ForwardServer should be enabled by default");
            Assert.IsFalse(forward.UdpOverTcp, "ForwardServer should have UdpOverTcp disabled by default");

            // Verify the static property returns the same instance on each access
            var forward2 = Server.ForwardServer;
            Assert.AreSame(forward, forward2, "ForwardServer should return the same static instance");
        }

        [TestMethod]
        public void ConfigParse_ValidSSRUrl_ReturnsServer()
        {
            const string ssrUrl =
                "ssr://MTI3LjAuMC4xOjEyMzQ6YXV0aF9hZXMxMjhfbWQ1OmFlcy0xMjgtY2ZiOnRsczEuMl90aWNrZXRfYXV0aDpZV0ZoWW1KaS8_b2Jmc3BhcmFtPVluSmxZV3QzWVRFeExtMXZaUQ";

            var server = new Server(ssrUrl, "");

            Assert.AreEqual("127.0.0.1", server.server);
            Assert.AreEqual((ushort)1234, server.Server_Port);
            Assert.AreEqual("auth_aes128_md5", server.Protocol);
            Assert.AreEqual("aes-128-cfb", server.Method);
            Assert.AreEqual("tls1.2_ticket_auth", server.obfs);
            Assert.AreEqual("breakwa11.moe", server.ObfsParam);
            Assert.AreEqual("aaabbb", server.Password);
        }

        [TestMethod]
        public void ConfigParse_MalformedUrl_ThrowsOrReturnsNull()
        {
            // A URL that starts with ssr:// but contains invalid base64 data
            const string malformedUrl = "ssr://!!!invalid_base64!!!/";

            Assert.ThrowsException<FormatException>(() =>
            {
                _ = new Server(malformedUrl, "");
            }, "Malformed SSR URL should throw FormatException");

            // A URL that looks like ssr:// but has no parsable content after base64 decode
            const string malformedUrl2 = "ssr://dGVzdA"; // "test" in base64 — decodes but won't match the expected format

            Assert.ThrowsException<FormatException>(() =>
            {
                _ = new Server(malformedUrl2, "");
            }, "SSR URL with non-matching decoded content should throw FormatException");
        }

        [TestMethod]
        public void ConfigParse_EmptyUrl_ThrowsOrReturnsNull()
        {
            Assert.ThrowsException<FormatException>(() =>
            {
                _ = new Server("", "TestGroup");
            }, "Empty URL should throw FormatException");

            Assert.ThrowsException<FormatException>(() =>
            {
                _ = new Server("   ", "TestGroup");
            }, "Whitespace-only URL should throw FormatException");
        }
    }
}
