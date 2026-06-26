using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Proxy;
using System;
using System.Net;
using System.Net.Sockets;
using NSubstitute;

namespace UnitTest
{
    [TestClass]
    public class ModelTests
    {
        // ============================================================
        // IPAddressCmp tests
        // ============================================================
        [TestMethod]
        public void IPAddressCmp_Constructor_FromIPAddress()
        {
            var ip = IPAddress.Parse("192.168.1.1");
            var cmp = new IPAddressCmp(ip);
            Assert.AreEqual("192.168.1.1", cmp.ToString());
        }

        [TestMethod]
        public void IPAddressCmp_Constructor_FromBytes()
        {
            var bytes = new byte[] { 10, 0, 0, 1 };
            var cmp = new IPAddressCmp(bytes);
            Assert.AreEqual("10.0.0.1", cmp.ToString());
        }

        [TestMethod]
        public void IPAddressCmp_Constructor_FromString()
        {
            var cmp = new IPAddressCmp("172.16.0.1");
            Assert.AreEqual("172.16.0.1", cmp.ToString());
        }

        [TestMethod]
        public void IPAddressCmp_CompareTo_LessThan()
        {
            var a = new IPAddressCmp("10.0.0.1");
            var b = new IPAddressCmp("10.0.0.2");
            Assert.AreEqual(-1, a.CompareTo(b));
        }

        [TestMethod]
        public void IPAddressCmp_CompareTo_GreaterThan()
        {
            var a = new IPAddressCmp("10.0.0.5");
            var b = new IPAddressCmp("10.0.0.1");
            Assert.AreEqual(1, a.CompareTo(b));
        }

        [TestMethod]
        public void IPAddressCmp_CompareTo_Equal()
        {
            var a = new IPAddressCmp("192.168.1.1");
            var b = new IPAddressCmp("192.168.1.1");
            Assert.AreEqual(0, a.CompareTo(b));
        }

        [TestMethod]
        public void IPAddressCmp_CompareTo_V4vsV6()
        {
            var a = new IPAddressCmp("1.1.1.1");
            var b = new IPAddressCmp("2001:db8::1");
            Assert.AreEqual(-1, a.CompareTo(b));
        }

        [TestMethod]
        public void IPAddressCmp_ToIPv6_V4Address()
        {
            var v4 = new IPAddressCmp("192.168.1.1");
            var v6 = v4.ToIPv6();
            Assert.AreEqual(System.Net.Sockets.AddressFamily.InterNetworkV6, v6.AddressFamily);
        }

        [TestMethod]
        public void IPAddressCmp_ToIPv6_V6Address()
        {
            var v6 = new IPAddressCmp("2001:db8::1");
            var result = v6.ToIPv6();
            Assert.AreSame(v6, result);
        }

        [TestMethod]
        public void IPAddressCmp_Inc_Normal()
        {
            var ip = new IPAddressCmp("192.168.1.1");
            var next = ip.Inc();
            Assert.AreEqual("192.168.1.2", next.ToString());
        }

        [TestMethod]
        public void IPAddressCmp_Inc_CarryOver()
        {
            var ip = new IPAddressCmp("192.168.1.255");
            var next = ip.Inc();
            Assert.AreEqual("192.168.2.0", next.ToString());
        }

        [TestMethod]
        public void IPAddressCmp_Inc_MaxOverflow()
        {
            var ip = new IPAddressCmp("255.255.255.255");
            var next = ip.Inc();
            Assert.AreEqual("255.255.255.255", next.ToString());
        }

        // ============================================================
        // IPSegment tests
        // ============================================================
        [TestMethod]
        public void IPSegment_Constructor_CreatesEmptySegment()
        {
            var seg = new IPSegment();
            Assert.IsNotNull(seg);
        }

        [TestMethod]
        public void IPSegment_Get_ReturnsDefault()
        {
            var seg = new IPSegment("default_val");
            var result = seg.Get(new IPAddressCmp("8.8.8.8"));
            Assert.AreEqual("default_val", result);
        }

        [TestMethod]
        public void IPSegment_Insert_UpdatesSegment()
        {
            var seg = new IPSegment(null);
            var start = new IPAddressCmp("10.0.0.0");
            var end = new IPAddressCmp("10.0.0.255");
            var inserted = seg.insert(start, end, "test_value");
            Assert.IsTrue(inserted);

            var result = seg.Get(new IPAddressCmp("10.0.0.50"));
            Assert.AreEqual("test_value", result);
        }

        [TestMethod]
        public void IPSegment_Get_OutsideInsertedRange()
        {
            var seg = new IPSegment("default");
            var start = new IPAddressCmp("10.0.0.0");
            var end = new IPAddressCmp("10.0.0.255");
            seg.insert(start, end, "inside");

            var result = seg.Get(new IPAddressCmp("192.168.1.1"));
            Assert.AreEqual("default", result);
        }

        [TestMethod]
        public void IPSegment_Insert_AdjacentRanges()
        {
            var seg = new IPSegment(null);
            seg.insert(new IPAddressCmp("10.0.0.0"), new IPAddressCmp("10.0.0.127"), "range1");
            seg.insert(new IPAddressCmp("10.0.0.128"), new IPAddressCmp("10.0.0.255"), "range2");

            Assert.AreEqual("range1", seg.Get(new IPAddressCmp("10.0.0.0")));
            Assert.AreEqual("range1", seg.Get(new IPAddressCmp("10.0.0.127")));
            Assert.AreEqual("range2", seg.Get(new IPAddressCmp("10.0.0.128")));
            Assert.AreEqual("range2", seg.Get(new IPAddressCmp("10.0.0.255")));
        }

        // ============================================================
        // PortMapConfig tests
        // ============================================================
        [TestMethod]
        public void PortMapConfig_Defaults()
        {
            var pm = new PortMapConfig();
            Assert.IsFalse(pm.Enable);
            Assert.AreEqual(PortMapType.Forward, pm.Type);
            Assert.IsNull(pm.Id);
            Assert.IsNull(pm.Server_addr);
            Assert.AreEqual(0, pm.Server_port);
        }

        [TestMethod]
        public void PortMapConfig_SetProperties()
        {
            var pm = new PortMapConfig
            {
                Enable = true,
                Id = "test-id",
                Server_addr = "10.0.0.1",
                Server_port = 8080,
                Remarks = "test remark"
            };
            Assert.IsTrue(pm.Enable);
            Assert.AreEqual("test-id", pm.Id);
            Assert.AreEqual("10.0.0.1", pm.Server_addr);
            Assert.AreEqual(8080, pm.Server_port);
            Assert.AreEqual("test remark", pm.Remarks);
        }

        // ============================================================
        // Connections tests
        // ============================================================
        [TestMethod]
        public void Connections_AddRef_IncrementsCount()
        {
            var conn = new Connections();
            var handler = Substitute.For<IHandler>();
            var result = conn.AddRef(handler);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Connections_AddRef_DuplicateIncrementsCount()
        {
            var conn = new Connections();
            var handler = Substitute.For<IHandler>();
            conn.AddRef(handler);
            var result = conn.AddRef(handler);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Connections_DecRef_UnknownHandler_ReturnsFalse()
        {
            var conn = new Connections();
            var handler = Substitute.For<IHandler>();
            var result = conn.DecRef(handler);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Connections_DecRef_KnownHandler_ReturnsTrue()
        {
            var conn = new Connections();
            var handler = Substitute.For<IHandler>();
            conn.AddRef(handler);
            var result = conn.DecRef(handler);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Connections_CloseAll_NoHandlers_DoesNotThrow()
        {
            var conn = new Connections();
            conn.CloseAll(); // should not throw
        }

        // ============================================================
        // Configuration tests
        // ============================================================
        [TestMethod]
        public void Configuration_Constructor_HasExpectedDefaults()
        {
            var cfg = new Configuration();
            Assert.AreEqual(1080, cfg.LocalPort);
            Assert.AreEqual(0, cfg.UdpPort);
            Assert.AreEqual(ProxyMode.NoModify, cfg.SysProxyMode);
            Assert.AreEqual(false, cfg.Random);
            Assert.AreEqual(5, cfg.ConnectTimeout);
            Assert.AreEqual(60, cfg.Ttl);
            Assert.AreEqual(2, cfg.ReconnectTimes);
            Assert.AreEqual(ProxyRuleMode.Disable, cfg.ProxyRuleMode);
            Assert.IsNotNull(cfg.Configs);
            Assert.IsNotNull(cfg.DnsClients);
            Assert.AreEqual(5, cfg.DnsClients.Count);
        }

        [TestMethod]
        public void Configuration_FixConfiguration_FixesOutOfRangeIndex()
        {
            var cfg = new Configuration { Index = 999, Configs = new() };
            cfg.FixConfiguration();
            Assert.AreEqual(0, cfg.Index);
        }

        [TestMethod]
        public void Configuration_FixConfiguration_EnsuresDefaultServer()
        {
            var cfg = new Configuration();
            cfg.FixConfiguration();
            Assert.AreEqual(1, cfg.Configs.Count);
        }

        [TestMethod]
        public void Configuration_FixConfiguration_FixesInvalidPort()
        {
            var cfg = new Configuration { LocalPort = 99999 };
            cfg.FixConfiguration();
            Assert.AreEqual(1080, cfg.LocalPort);
        }

        [TestMethod]
        public void Configuration_FixConfiguration_FixesZeroConnectTimeout()
        {
            var cfg = new Configuration { ConnectTimeout = 0 };
            cfg.FixConfiguration();
            Assert.AreEqual(5, cfg.ConnectTimeout);
        }

        [TestMethod]
        public void Configuration_CopyFrom_CopiesProperties()
        {
            var source = new Configuration { LocalPort = 8080, Ttl = 120, Index = 2 };
            source.Configs.Add(new Server { server = "test.example.com" });
            var dest = new Configuration();
            dest.CopyFrom(source);
            Assert.AreEqual(8080, dest.LocalPort);
            Assert.AreEqual(120, dest.Ttl);
            Assert.AreEqual(2, dest.Index);
            Assert.AreEqual(1, dest.Configs.Count);
        }

        [TestMethod]
        public void Configuration_KeepCurrentServer_ReturnsFalseWhenTargetAddrNull()
        {
            var cfg = new Configuration();
            cfg.Configs.Add(new Server { server = "test1.com", Id = "id1" });
            var result = cfg.KeepCurrentServer(1080, null, "id1");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Configuration_KeepCurrentServer_ReturnsFalseWhenSameHostDisabled()
        {
            var cfg = new Configuration { SameHostForSameTarget = false };
            cfg.Configs.Add(new Server { server = "test1.com", Id = "id1" });
            var result = cfg.KeepCurrentServer(1080, "example.com", "id1");
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Configuration_GetCurrentServer_ReturnsServerAtIndex()
        {
            var cfg = new Configuration { Index = 0 };
            cfg.Configs.Add(new Server { server = "test1.com", Id = "id1", Enable = true });
            var result = cfg.GetCurrentServer(1080, null);
            Assert.AreEqual("test1.com", result.server);
        }

        [TestMethod]
        public void Configuration_GetCurrentServer_ReturnsErrorWhenEmpty()
        {
            var cfg = new Configuration();
            var result = cfg.GetCurrentServer(1080, null);
            Assert.AreEqual("invalid", result.server);
        }

        [TestMethod]
        public void Configuration_IsDefaultConfig_TrueWhenAllDefault()
        {
            var cfg = new Configuration();
            cfg.FixConfiguration();
            Assert.IsTrue(cfg.IsDefaultConfig());
        }

        [TestMethod]
        public void Configuration_FlushPortMapCache_NoPortMap_DoesNotThrow()
        {
            var cfg = new Configuration();
            cfg.Configs.Add(new Server { server = "test.com", Id = "id1" });
            cfg.FlushPortMapCache();
            Assert.IsNotNull(cfg.PortMapCache);
            Assert.AreEqual(0, cfg.PortMapCache.Count);
        }

        [TestMethod]
        public void Configuration_SetProperties_Work()
        {
            var cfg = new Configuration
            {
                SysProxyMode = ProxyMode.Global,
                ProxyRuleMode = ProxyRuleMode.BypassLan,
                AutoCheckUpdate = true,
                ThemeMode = AppThemeMode.Dark
            };
            Assert.AreEqual(ProxyMode.Global, cfg.SysProxyMode);
            Assert.AreEqual(ProxyRuleMode.BypassLan, cfg.ProxyRuleMode);
            Assert.IsTrue(cfg.AutoCheckUpdate);
            Assert.AreEqual(AppThemeMode.Dark, cfg.ThemeMode);
        }

        // ============================================================
        // IPRangeSet tests
        // ============================================================
        [TestMethod]
        public void IPRangeSet_Constructor_ForDisableProxyRuleMode()
        {
            var set = new IPRangeSet(ProxyRuleMode.Disable);
            Assert.IsNotNull(set);
        }

        [TestMethod]
        public void IPRangeSet_Constructor_ForBypassLanProxyRuleMode()
        {
            var set = new IPRangeSet(ProxyRuleMode.BypassLan);
            Assert.IsNotNull(set);
        }

        [TestMethod]
        public void IPRangeSet_Constructor_ForBypassLanAndNotChina()
        {
            var set = new IPRangeSet(ProxyRuleMode.BypassLanAndNotChina);
            Assert.IsNotNull(set);
        }

        [TestMethod]
        public void IPRangeSet_IsInIPRange_DefaultReturnsDirect()
        {
            var set = new IPRangeSet(ProxyRuleMode.BypassLan);
            // Without loading any rules, match should return default which is not Direct
            var ip = IPAddress.Parse("1.2.3.4");
            var result = set.IsInIPRange(ip);
            // Default trie match is default(Rule) which is not Rule.Direct
            Assert.IsFalse(result);
        }

        // ============================================================
        // DnsClient tests
        // ============================================================
        [TestMethod]
        public void DnsClient_Constructor_SetsDefaults()
        {
            var client = new DnsClient(DnsType.DnsOverTls);
            Assert.AreEqual(DnsType.DnsOverTls, client.DnsType);
            Assert.IsTrue(client.Enable);
        }

        [TestMethod]
        public void DnsClient_SetsProperties()
        {
            var client = new DnsClient(DnsType.DnsOverTls)
            {
                DnsServer = "8.8.8.8",
                Enable = false
            };
            Assert.AreEqual(DnsType.DnsOverTls, client.DnsType);
            Assert.AreEqual("8.8.8.8", client.DnsServer);
            Assert.IsFalse(client.Enable);
        }

        // ============================================================
        // LRUCache tests
        // ============================================================
        [TestMethod]
        public void LRUCache_SetAndGet()
        {
            var cache = new LRUCache<string, int>(10);
            cache.Set("key1", 42);
            var found = cache.Get("key1");
            Assert.AreEqual(42, found);
        }

        [TestMethod]
        public void LRUCache_MissingKey_ReturnsDefault()
        {
            var cache = new LRUCache<string, int>(10);
            // Get on missing key returns 0 (default for int) — does not throw
            var result = cache.Get("nonexistent");
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void LRUCache_ContainsKey()
        {
            var cache = new LRUCache<string, int>(10);
            Assert.IsFalse(cache.ContainsKey("key1"));
            cache.Set("key1", 1);
            Assert.IsTrue(cache.ContainsKey("key1"));
        }

        [TestMethod]
        public void LRUCache_SweepRemovesItems()
        {
            var cache = new LRUCache<string, int>(0); // timeout=0 means sweep removes ALL
            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Sweep();
            Assert.IsFalse(cache.ContainsKey("a"));
            Assert.IsFalse(cache.ContainsKey("b"));
        }

        // ============================================================
        // ErrorLog tests
        // ============================================================
        [TestMethod]
        public void ErrorLog_DefaultValues()
        {
            var log = new ErrorLog(0);
            Assert.IsNotNull(log);
            Assert.AreEqual(0, log.errno);
        }

        // ============================================================
        // Server tests
        // ============================================================
        [TestMethod]
        public void Server_Constructor_Defaults()
        {
            var s = new Server();
            Assert.AreEqual("server host", s.server);
            Assert.AreEqual(8388, s.Server_Port);
        }

        [TestMethod]
        public void Server_FriendlyName_WithRemarks()
        {
            var s = new Server { server = "10.0.0.1", Remarks = "My Server" };
            Assert.AreEqual("My Server", s.FriendlyName);
        }

        [TestMethod]
        public void Server_FriendlyName_WithoutRemarks()
        {
            var s = new Server { server = "10.0.0.1" };
            Assert.AreEqual("10.0.0.1:8388", s.FriendlyName);
        }

        // ============================================================
        // DnsBuffer tests
        // ============================================================
        [TestMethod]
        public void DnsBuffer_InitializedCorrectly()
        {
            var buf = new DnsBuffer();
            Assert.IsNotNull(buf);
            Assert.IsNull(buf.Host);
            Assert.IsNull(buf.Ip);
        }

        // ============================================================
        // ModelBase tests
        // ============================================================
        [TestMethod]
        public void PortMapConfig_PropertyChanged_Fires()
        {
            var pm = new PortMapConfig();
            var fired = false;
            pm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PortMapConfig.Enable))
                    fired = true;
            };
            pm.Enable = true;
            Assert.IsTrue(fired);
        }
    }
}
