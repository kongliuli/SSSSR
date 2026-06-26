using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shadowsocks.Controller;
using Shadowsocks.Controller.HttpRequest;
using Shadowsocks.Controller.Service;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Services;
using Shadowsocks.ViewModel;
using System.Collections.Generic;
using System.Linq;

namespace UnitTest
{
    [TestClass]
    public class PortForwardingViewModelTests
    {
        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Create a ViewModel backed by a real <see cref="Configuration"/> and mocked
        /// <see cref="MainController"/> / <see cref="IConfigPersistenceService"/>.
        /// The config has an empty server list and an empty port map so that every
        /// test starts from a clean slate.
        /// </summary>
        private static (
            PortForwardingViewModel vm,
            IConfigPersistenceService persistence,
            Configuration config) CreateViewModel()
        {
            var config = new Configuration();
            config.Configs.Clear();                          // no default servers
            config.PortMap = new Dictionary<string, PortMapConfig>();

            var persistence = Substitute.For<IConfigPersistenceService>();
            persistence.Load().Returns(config);

            var controller = Substitute.For<MainController>(
                config,
                Substitute.For<UpdateNode>(),
                Substitute.For<UpdateSubscribeManager>(),
                persistence);

            var vm = new PortForwardingViewModel(controller, persistence);
            return (vm, persistence, config);
        }

        // ── tests ────────────────────────────────────────────────────────────

        [TestMethod]
        public void Load_PopulatesRulesFromConfig()
        {
            // Arrange
            var (vm, persistence, config) = CreateViewModel();
            config.PortMap = new Dictionary<string, PortMapConfig>
            {
                ["8080"] = new PortMapConfig { Enable = true, Type = PortMapType.Forward },
                ["9090"] = new PortMapConfig { Enable = false, Type = PortMapType.RuleProxy }
            };

            // Act
            vm.Load();

            // Assert
            Assert.AreEqual(2, vm.Rules.Count,
                "Should load both port-map entries into Rules.");
            Assert.AreEqual(8080, vm.Rules[0].LocalPort);
            Assert.AreEqual(PortMapType.Forward, vm.Rules[0].Type);
            Assert.AreEqual(9090, vm.Rules[1].LocalPort);
            Assert.AreEqual(PortMapType.RuleProxy, vm.Rules[1].Type);
            Assert.AreEqual($"已加载 2 条规则", vm.StatusText);
        }

        [TestMethod]
        public void Load_PopulatesServerOptionsFromConfig()
        {
            // Arrange
            var (vm, persistence, config) = CreateViewModel();
            config.Configs.Add(new Server { Id = "srv-1", Group = "grp-a", server = "s1.example.com", Remarks = "Server1" });
            config.Configs.Add(new Server { Id = "srv-2", Group = "grp-a", server = "s2.example.com", Remarks = "Server2" });
            config.Configs.Add(new Server { Id = "srv-3", Group = "grp-b", server = "s3.example.com", Remarks = "Server3" });

            // Act
            vm.Load();

            // Assert
            // Expect: (任意/分组) + 2 groups + 3 servers = 6 entries
            Assert.AreEqual(6, vm.ServerOptions.Count,
                "Should contain the empty entry, unique groups, and all servers.");

            // First entry is always the "any/group" placeholder
            Assert.AreEqual(string.Empty, vm.ServerOptions[0].Id);

            // Group entries
            var groupIds = vm.ServerOptions
                .Skip(1) // skip placeholder
                .Select(o => o.Id)
                .Where(id => vm.ServerOptions.FirstOrDefault(x => x.Id == id)?.Display?.StartsWith("#分组") == true)
                .ToList();
            Assert.IsTrue(groupIds.Contains("grp-a"), "grp-a should appear as a group entry.");
            Assert.IsTrue(groupIds.Contains("grp-b"), "grp-b should appear as a group entry.");

            // Server entries (after groups)
            var serverIds = vm.ServerOptions
                .Select(o => o.Id)
                .Where(id => id == "srv-1" || id == "srv-2" || id == "srv-3")
                .ToList();
            Assert.AreEqual(3, serverIds.Count, "All three servers should be in the pick list.");
        }

        [TestMethod]
        public void Types_ExposesExpectedPortMapTypeValues()
        {
            // Arrange
            var (vm, _, _) = CreateViewModel();

            // Assert
            Assert.AreEqual(3, vm.Types.Count);
            CollectionAssert.AreEqual(
                new[] { PortMapType.Forward, PortMapType.ForceProxy, PortMapType.RuleProxy },
                vm.Types.ToArray());
        }

        [TestMethod]
        public void Add_AppendsNewPortMapRowWithDefaults()
        {
            // Arrange
            var (vm, _, _) = CreateViewModel();

            // Act
            vm.AddCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, vm.Rules.Count, "One row should be appended.");
            var row = vm.Rules[0];
            Assert.AreEqual(0, row.LocalPort,
                "New row has placeholder port 0.");
            Assert.IsTrue(row.Enable,
                "New row is enabled by default.");
            Assert.AreEqual(PortMapType.Forward, row.Type,
                "New row defaults to Forward type.");
            Assert.AreSame(row, vm.SelectedRule,
                "Newly added row becomes the selected rule.");
            Assert.AreEqual("已添加新规则，请填写本地端口后保存", vm.StatusText);
        }

        [TestMethod]
        public void Delete_RemovesSelectedRuleAndClearsSelection()
        {
            // Arrange
            var (vm, _, _) = CreateViewModel();
            vm.AddCommand.Execute(null);             // adds one row
            var row = vm.Rules[0];
            vm.SelectedRule = row;

            // Act
            vm.DeleteCommand.Execute(null);

            // Assert
            Assert.AreEqual(0, vm.Rules.Count,
                "Rule should be removed from the collection.");
            Assert.IsNull(vm.SelectedRule,
                "Selection should be cleared after deletion.");
            Assert.AreEqual("已删除选中规则（保存后生效）", vm.StatusText);
        }

        [TestMethod]
        public void Delete_WithoutSelection_SetsStatusText()
        {
            // Arrange
            var (vm, _, _) = CreateViewModel();
            vm.SelectedRule = null;

            // Act
            vm.DeleteCommand.Execute(null);

            // Assert
            Assert.AreEqual("请先选择要删除的规则", vm.StatusText,
                "Should instruct the user to select a rule first.");
            // Rules collection should remain unchanged
        }

        [TestMethod]
        public void Save_InvalidPort_AbortsAndSetsErrorStatus()
        {
            // Arrange
            var (vm, persistence, _) = CreateViewModel();

            // Port 0 is invalid (< 1)
            vm.Rules.Add(new PortMapRow(0, new PortMapConfig()));

            // Act
            vm.SaveCommand.Execute(null);

            // Assert
            Assert.IsTrue(vm.StatusText.Contains("非法"),
                $"Expected '非法' in status, got: {vm.StatusText}");
            // Persistence Save should NOT be called when validation fails.
            persistence.DidNotReceive().Save(Arg.Any<Configuration>());
        }

        [TestMethod]
        public void Save_PortAbove65535_AbortsAndSetsErrorStatus()
        {
            // Arrange
            var (vm, persistence, _) = CreateViewModel();

            // Port 65536 is invalid (> 65535)
            vm.Rules.Add(new PortMapRow(65536, new PortMapConfig()));

            // Act
            vm.SaveCommand.Execute(null);

            // Assert
            Assert.IsTrue(vm.StatusText.Contains("非法"),
                $"Expected '非法' in status, got: {vm.StatusText}");
            // Persistence Save should NOT be called when validation fails.
            persistence.DidNotReceive().Save(Arg.Any<Configuration>());
        }

        [TestMethod]
        public void Save_DuplicatePort_AbortsAndSetsErrorStatus()
        {
            // Arrange
            var (vm, persistence, _) = CreateViewModel();

            // Two rows with the same valid port
            vm.Rules.Add(new PortMapRow(8080, new PortMapConfig()));
            vm.Rules.Add(new PortMapRow(8080, new PortMapConfig()));

            // Act
            vm.SaveCommand.Execute(null);

            // Assert
            Assert.IsTrue(vm.StatusText.Contains("重复"),
                $"Expected '重复' in status, got: {vm.StatusText}");
            // Persistence Save should NOT be called when duplicate detected.
            persistence.DidNotReceive().Save(Arg.Any<Configuration>());
        }

        // NOTE: Save_ValidData integration test omitted because MainController.SaveServersPortMap
        // is a non-virtual public method — NSubstitute cannot intercept it, and the real
        // implementation calls Application.Current.Dispatcher (null in test context).

        [TestMethod]
        public void PortMapRow_ToConfig_RoundTripsProperties()
        {
            // Arrange
            var row = new PortMapRow(443, new PortMapConfig
            {
                Enable = true,
                Type = PortMapType.ForceProxy,
                Id = "srv-x",
                Server_addr = "10.0.0.1",
                Server_port = 8443,
                Remarks = "test remark"
            });

            // Act
            var config = row.ToConfig();

            // Assert
            Assert.AreEqual(443, row.LocalPort,
                "LocalPort is NOT part of ToConfig() — it is the dictionary key.");
            Assert.IsTrue(config.Enable);
            Assert.AreEqual(PortMapType.ForceProxy, config.Type);
            Assert.AreEqual("srv-x", config.Id);
            Assert.AreEqual("10.0.0.1", config.Server_addr);
            Assert.AreEqual(8443, config.Server_port);
            Assert.AreEqual("test remark", config.Remarks);
        }
    }
}
