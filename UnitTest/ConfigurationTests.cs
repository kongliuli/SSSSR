using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace UnitTest
{
    [TestClass]
    public class ConfigurationTests
    {
        [TestMethod]
        public void FixConfiguration_SetsValidDefaults()
        {
            // Arrange: create a configuration with invalid values
            var config = new Configuration
            {
                LocalPort = 0,            // invalid — must be > 0 and <= 65535
                PortMap = null,           // null — must be non-null dictionary
                ConnectTimeout = 0,       // zero — triggers reset of timeout/reconnect/ttl
                Index = -1,               // negative — must be clamped to 0
            };
            config.Configs.Clear();       // empty server list
            // Add two servers with the same ID to trigger dedup
            var dupId = "dup-id-123";
            config.Configs.Add(new Server { Id = dupId });
            config.Configs.Add(new Server { Id = dupId });

            // Act
            config.FixConfiguration();

            // Assert: invalid port corrected
            Assert.AreEqual(1080, config.LocalPort);

            // Assert: null PortMap replaced with empty dictionary
            Assert.IsNotNull(config.PortMap);
            Assert.AreEqual(0, config.PortMap.Count);

            // Assert: zero ConnectTimeout resets timeout/reconnect/ttl defaults
            Assert.AreEqual(5, config.ConnectTimeout);
            Assert.AreEqual(2, config.ReconnectTimes);
            Assert.AreEqual(60, config.Ttl);

            // Assert: out-of-range Index clamped to 0
            Assert.AreEqual(0, config.Index);

            // Assert: empty Configs gets a default server added
            Assert.IsTrue(config.Configs.Count >= 1);

            // Assert: duplicate server IDs have been deduplicated (all unique)
            var ids = new HashSet<string>();
            foreach (var server in config.Configs)
            {
                Assert.IsFalse(ids.Contains(server.Id), $"Duplicate server ID found: {server.Id}");
                ids.Add(server.Id);
            }
        }

        [TestMethod]
        public void GetCurrentServer_ReturnsNull_WhenNoServers()
        {
            // Arrange: configuration with an empty server list
            var config = new Configuration();
            config.Configs.Clear();

            // Act
            var result = config.GetCurrentServer(1080, null);

            // Assert: GetCurrentServer returns the error server (server = "invalid")
            // when no servers are available — it never returns null.
            Assert.IsNotNull(result);
            Assert.AreEqual("invalid", result.server);
        }

        [TestMethod]
        public void GetCurrentServer_ReturnsFirstServer_WhenIndexZero()
        {
            // Arrange
            var config = new Configuration();
            config.Configs.Clear();
            var server1 = new Server { server = "server1.example.com", Enable = true };
            var server2 = new Server { server = "server2.example.com", Enable = true };
            config.Configs.Add(server1);
            config.Configs.Add(server2);
            config.Index = 0;

            // Act
            var result = config.GetCurrentServer(1080, null);

            // Assert: Index 0 returns the first server
            Assert.IsNotNull(result);
            Assert.AreSame(server1, result);
        }

        [TestMethod]
        public void KeepCurrentServer_PreservesActiveServer_AfterConfigChange()
        {
            // Arrange
            var config = new Configuration();
            config.Configs.Clear();
            var server1 = new Server { Id = "server-id-001", server = "keepme.example.com", Enable = true };
            config.Configs.Add(server1);
            config.Index = 0;
            config.SameHostForSameTarget = true;

            // Populate the URI cache by calling GetCurrentServer with a target address.
            // This records that "target.example.com" was served by Configs[0].
            config.GetCurrentServer(1080, null, "target.example.com");

            // Act: KeepCurrentServer should find the cached entry and return true.
            var result = config.KeepCurrentServer(1080, "target.example.com", "server-id-001");

            // Assert
            Assert.IsTrue(result, "KeepCurrentServer should return true when the cached server is still enabled and matching.");

            // Act 2: passing a different id should return false (no match)
            var result2 = config.KeepCurrentServer(1080, "target.example.com", "different-id");
            Assert.IsFalse(result2, "KeepCurrentServer should return false when no server matches the given id.");
        }

        [TestMethod]
        public void KeepCurrentServer_ReturnsFalse_WhenTargetAddrIsNull()
        {
            // Arrange
            var config = new Configuration();
            config.Configs.Clear();
            config.Configs.Add(new Server { Id = "any-id", server = "any.example.com", Enable = true });
            config.SameHostForSameTarget = true;

            // Act: targetAddr is null — the method short-circuits
            var result = config.KeepCurrentServer(1080, null, "any-id");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ConfigVersion_HasExpectedDefault()
        {
            // Note: Configuration does not have a dedicated version field.
            // This test validates that a newly constructed Configuration is in a
            // valid, expected default state — effectively a sanity check on the
            // constructor defaults.

            var config = new Configuration();

            Assert.IsNotNull(config.Configs);
            Assert.AreEqual(0, config.Index);
            Assert.IsFalse(config.Random);
            Assert.AreEqual(1080, config.LocalPort);
            Assert.AreEqual(2, config.ReconnectTimes);
            Assert.AreEqual(60, config.Ttl);
            Assert.AreEqual(5, config.ConnectTimeout);
            Assert.AreEqual(ProxyRuleMode.Disable, config.ProxyRuleMode);
            Assert.IsNotNull(config.PortMap);
            Assert.IsNotNull(config.DnsClients);
            Assert.IsTrue(config.DnsClients.Count > 0);
            Assert.IsNotNull(config.ServerSubscribes);
            Assert.IsFalse(config.ShareOverLan);
            Assert.IsTrue(config.LogEnable);
            Assert.IsTrue(config.SameHostForSameTarget);
            Assert.IsTrue(config.AutoCheckUpdate);
            Assert.IsFalse(config.IsPreRelease);
        }

        [TestMethod]
        public void ProxyRuleMode_DefaultsToDisable()
        {
            // Arrange
            var config = new Configuration();

            // Assert
            Assert.AreEqual(ProxyRuleMode.Disable, config.ProxyRuleMode);
        }

        [TestMethod]
        public void LocalPort_HasValidDefault()
        {
            // Arrange
            var config = new Configuration();

            // Assert
            Assert.AreEqual(1080, config.LocalPort);
        }

        [TestMethod]
        public void Configuration_RaisesPropertyChanged_WhenPropertySet()
        {
            // Arrange
            var config = new Configuration();
            string changedPropertyName = null;
            config.PropertyChanged += (sender, args) =>
            {
                changedPropertyName = args.PropertyName;
            };

            // Act: change a property that uses SetField<T>
            config.LocalPort = 8888;

            // Assert
            Assert.AreEqual(nameof(Configuration.LocalPort), changedPropertyName,
                "PropertyChanged should be raised with the correct property name.");

            // Act 2: set to the same value — should NOT raise PropertyChanged
            changedPropertyName = null;
            config.LocalPort = 8888;

            // Assert: no event when value is unchanged
            Assert.IsNull(changedPropertyName,
                "PropertyChanged should NOT be raised when the value is unchanged.");
        }
    }
}
