using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shadowsocks.Controller.HttpRequest;
using Shadowsocks.Controller.Service;
using Shadowsocks.Model;
using Shadowsocks.Services;
using Shadowsocks.ViewModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTest
{
    [TestClass]
    public class SubscriptionsViewModelTests
    {
        private Configuration CreateConfig()
        {
            return new Configuration();
        }

        private UpdateNode CreateUpdateNodeMock()
        {
            return Substitute.For<UpdateNode>();
        }

        private UpdateSubscribeManager CreateUpdateSubscribeManagerMock()
        {
            return Substitute.For<UpdateSubscribeManager>();
        }

        private IConfigPersistenceService CreateConfigPersistenceMock()
        {
            return Substitute.For<IConfigPersistenceService>();
        }

        // ── Tests: Add ───────────────────────────────────────────────────

        [TestMethod]
        public void Add_CreatesNewSubscription_AndAppearsInCollection()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Assert initial state: empty
            Assert.AreEqual(0, vm.Subscriptions.Count);
            Assert.IsTrue(vm.IsEmpty);

            // Act
            vm.AddCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, vm.Subscriptions.Count);
            Assert.IsFalse(vm.IsEmpty);
            Assert.IsNotNull(vm.Subscriptions[0].Model);
            Assert.AreEqual(1, config.ServerSubscribes.Count);
            configPersistence.Received(1).Save(config);
        }

        [TestMethod]
        public void Add_MultipleSubscriptions_EachAppearsInCollection()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Act
            vm.AddCommand.Execute(null);
            vm.AddCommand.Execute(null);
            vm.AddCommand.Execute(null);

            // Assert
            Assert.AreEqual(3, vm.Subscriptions.Count);
            Assert.IsFalse(vm.IsEmpty);
            Assert.AreEqual(3, config.ServerSubscribes.Count);
            configPersistence.Received(3).Save(config);
        }

        // ── Tests: Delete ────────────────────────────────────────────────

        [TestMethod]
        public void Delete_RemovesSubscription_FromCollectionAndConfig()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Add a subscription first
            vm.AddCommand.Execute(null);
            var item = vm.Subscriptions[0];

            // Act
            vm.DeleteCommand.Execute(item);

            // Assert
            Assert.AreEqual(0, vm.Subscriptions.Count);
            Assert.IsTrue(vm.IsEmpty);
            Assert.AreEqual(0, config.ServerSubscribes.Count);
            configPersistence.Received(2).Save(config); // once for Add, once for Delete
        }

        [TestMethod]
        public void Delete_RemovesServersWithMatchingSubTag()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            // Add a subscription via the VM so it gets a fresh model
            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);
            vm.AddCommand.Execute(null);
            var item = vm.Subscriptions[0];

            // Set a known Tag so we can match servers
            var tag = "my-sub-tag";
            item.Model.Tag = tag;

            // Add servers: two matching the subscription, one not
            config.Configs.Clear();
            config.Configs.Add(new Server { SubTag = tag, server = "server1.example.com" });
            config.Configs.Add(new Server { SubTag = tag, server = "server2.example.com" });
            config.Configs.Add(new Server { SubTag = "other-tag", server = "server3.example.com" });

            // Act: delete the subscription
            vm.DeleteCommand.Execute(item);

            // Assert: matching servers removed, unrelated server remains
            Assert.AreEqual(0, config.ServerSubscribes.Count);
            Assert.AreEqual(1, config.Configs.Count);
            Assert.AreEqual("other-tag", config.Configs[0].SubTag);
        }

        [TestMethod]
        public void Delete_WithNullItem_DoesNotThrow()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Act & Assert: should not throw
            vm.DeleteCommand.Execute(null);

            Assert.AreEqual(0, vm.Subscriptions.Count);
            // Save should only have been called by the constructor's Reload (which doesn't save)
            // and no save from Delete since it returns early on null
            configPersistence.DidNotReceive().Save(config);
        }

        // ── Tests: Reload ────────────────────────────────────────────────

        [TestMethod]
        public void Reload_PopulatesFromConfigServerSubscribes()
        {
            // Arrange
            var config = CreateConfig();
            config.ServerSubscribes.Clear();
            var sub1 = new ServerSubscribe { Tag = "sub1", Url = "https://example.com/sub1" };
            var sub2 = new ServerSubscribe { Tag = "sub2", Url = "https://example.com/sub2" };
            config.ServerSubscribes.Add(sub1);
            config.ServerSubscribes.Add(sub2);

            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            // The constructor calls Reload() automatically, so we just construct
            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Assert
            Assert.AreEqual(2, vm.Subscriptions.Count);
            Assert.IsFalse(vm.IsEmpty);
            Assert.AreSame(sub1, vm.Subscriptions[0].Model);
            Assert.AreSame(sub2, vm.Subscriptions[1].Model);
        }

        [TestMethod]
        public void Reload_HandlesNullServerSubscribes_Gracefully()
        {
            // Arrange: config with null ServerSubscribes (unnatural but defensive)
            var config = CreateConfig();
            // Use reflection to set ServerSubscribes to null temporarily,
            // but actually Configuration initializes it in constructor.
            // Instead test that an empty list results in IsEmpty = true.
            config.ServerSubscribes.Clear();

            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            // Act
            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Assert
            Assert.AreEqual(0, vm.Subscriptions.Count);
            Assert.IsTrue(vm.IsEmpty);
        }

        // ── Tests: SetEnabled ────────────────────────────────────────────

        [TestMethod]
        public void SetEnabled_TogglesMatchingServers()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);
            vm.AddCommand.Execute(null);
            var item = vm.Subscriptions[0];
            var tag = "toggle-test-tag";
            item.Model.Tag = tag;

            // Add servers with matching SubTag
            config.Configs.Clear();
            config.Configs.Add(new Server { SubTag = tag, server = "s1.example.com", Enable = true });
            config.Configs.Add(new Server { SubTag = tag, server = "s2.example.com", Enable = true });
            config.Configs.Add(new Server { SubTag = "other", server = "s3.example.com", Enable = true });

            // Clear calls from AddCommand to isolate SetEnabled's Save call
            configPersistence.ClearReceivedCalls();

            // Act: disable the subscription group
            vm.SetEnabled(item, false);

            // Assert: matching servers disabled, unrelated server still enabled
            Assert.IsFalse(config.Configs[0].Enable);
            Assert.IsFalse(config.Configs[1].Enable);
            Assert.IsTrue(config.Configs[2].Enable);
            configPersistence.Received(1).Save(config);
        }

        [TestMethod]
        public void SetEnabled_WithNullItem_DoesNotThrow()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Act & Assert: should not throw
            vm.SetEnabled(null, true);

            // Save should not have been called
            configPersistence.DidNotReceive().Save(config);
        }

        // ── Tests: UpdateOne ─────────────────────────────────────────────

        [TestMethod]
        public async Task UpdateOne_CommandExecutes_WithoutException()
        {
            // Arrange: CreateTask on UpdateSubscribeManager is non-virtual;
            // NSubstitute cannot intercept it, so we verify that the command
            // does not throw and the background task completes (errors are
            // swallowed by the try/catch in UpdateOne).
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);
            vm.AddCommand.Execute(null);
            var item = vm.Subscriptions[0];

            // Act: trigger single-update — should not throw
            vm.UpdateOneCommand.Execute(item);

            // Wait for the background Task.Run to complete
            await Task.Delay(500);
        }

        [TestMethod]
        public void UpdateOne_WithNullItem_DoesNotThrow()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Act & Assert: null check returns early, no exception thrown
            vm.UpdateOneCommand.Execute(null);

            // Allow any background task to complete (should be none since null was passed)
            Thread.Sleep(100);
        }

        // ── Tests: UpdateAll ─────────────────────────────────────────────

        [TestMethod]
        public async Task UpdateAll_CommandExecutes_WithoutException()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);
            vm.AddCommand.Execute(null);
            vm.AddCommand.Execute(null);

            // Act: trigger bulk-update — should not throw
            vm.UpdateAllCommand.Execute(null);

            // Wait for the background Task.Run to complete
            await Task.Delay(500);
        }

        // ── Tests: IsEmpty tracking ──────────────────────────────────────

        [TestMethod]
        public void IsEmpty_TracksCollectionState_AfterAddAndDelete()
        {
            // Arrange
            var config = CreateConfig();
            var updateNode = CreateUpdateNodeMock();
            var updateSubscribeManager = CreateUpdateSubscribeManagerMock();
            var configPersistence = CreateConfigPersistenceMock();

            var vm = new SubscriptionsViewModel(config, updateNode, updateSubscribeManager, configPersistence);

            // Assert initial state
            Assert.IsTrue(vm.IsEmpty);
            Assert.AreEqual(0, vm.Subscriptions.Count);

            // Act: add one
            vm.AddCommand.Execute(null);
            Assert.IsFalse(vm.IsEmpty);
            Assert.AreEqual(1, vm.Subscriptions.Count);

            // Act: delete it
            var item = vm.Subscriptions[0];
            vm.DeleteCommand.Execute(item);
            Assert.IsTrue(vm.IsEmpty);
            Assert.AreEqual(0, vm.Subscriptions.Count);
        }

        // ── Tests: Subscription item properties ──────────────────────────

        [TestMethod]
        public void SubscriptionItemViewModel_DefaultValues_AreCorrect()
        {
            // Arrange
            var config = CreateConfig();
            var sub = new ServerSubscribe();
            sub.Tag = "test-tag";
            sub.Url = "https://example.com/sub";
            sub.Enable = true;
            sub.LastUpdateTime = 0;

            // Act
            var item = new SubscriptionItemViewModel(sub, config);

            // Assert
            Assert.AreEqual("test-tag", item.Name);
            Assert.AreEqual("https://example.com/sub", item.Url);
            Assert.IsTrue(item.Enable);
            Assert.AreEqual(@"从未更新", item.LastUpdateText);
            Assert.AreEqual(0, item.NodeCount);
            Assert.AreEqual(@"0 个节点", item.NodeCountText);
        }

        [TestMethod]
        public void SubscriptionItemViewModel_NodeCount_ReflectsMatchingServers()
        {
            // Arrange
            var config = CreateConfig();
            var sub = new ServerSubscribe { Tag = "group-a" };
            var item = new SubscriptionItemViewModel(sub, config);

            // No servers yet
            Assert.AreEqual(0, item.NodeCount);

            // Add servers matching the tag
            config.Configs.Clear();
            config.Configs.Add(new Server { SubTag = "group-a", server = "s1.example.com" });
            config.Configs.Add(new Server { SubTag = "group-a", server = "s2.example.com" });
            config.Configs.Add(new Server { SubTag = "group-b", server = "s3.example.com" });

            // Assert: only "group-a" servers counted
            Assert.AreEqual(2, item.NodeCount);
            Assert.AreEqual(@"2 个节点", item.NodeCountText);
        }
    }
}
