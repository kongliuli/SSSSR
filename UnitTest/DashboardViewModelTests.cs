using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shadowsocks.Controller;
using Shadowsocks.Controller.HttpRequest;
using Shadowsocks.Controller.Service;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Model.Transfer;
using Shadowsocks.Services;
using Shadowsocks.ViewModel;

namespace UnitTest
{
    [TestClass]
    public class DashboardViewModelTests
    {
        /// <summary>
        /// Creates a <see cref="MainController"/> substitute. The real constructor is
        /// called with the given <paramref name="config"/> so it initialises cleanly;
        /// no server SpeedLog fields are overwritten because the transfer-log Servers
        /// dictionary is fresh and empty.
        /// </summary>
        private static MainController CreateControllerStub(Configuration config)
        {
            return Substitute.For<MainController>(
                config,
                Substitute.For<UpdateNode>(),
                Substitute.For<UpdateSubscribeManager>(),
                Substitute.For<IConfigPersistenceService>());
        }

        // ─────────────────────────────────────────────────────────────────
        //  Constructor
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Constructor_InitializesSpeedSeriesWithTwoLines()
        {
            var config = new Configuration();
            var controller = CreateControllerStub(config);

            var vm = new DashboardViewModel(config, controller);

            Assert.IsNotNull(vm.SpeedSeries);
            Assert.AreEqual(2, vm.SpeedSeries.Length);
            Assert.AreEqual("下行", vm.SpeedSeries[0].Name);
            Assert.AreEqual("上行", vm.SpeedSeries[1].Name);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Proxy-mode status (SysProxyMode → IsDirect / IsPac / IsGlobal / StatusText)
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Start_WithPacMode_SetsIsPacAndShowsConnected()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Pac };
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            Assert.AreEqual("已连接", vm.StatusText);
            Assert.IsTrue(vm.IsPac);
            Assert.IsFalse(vm.IsDirect);
            Assert.IsFalse(vm.IsGlobal);
        }

        [TestMethod]
        public void Start_WithDirectMode_SetsIsDirectAndShowsNotEnabled()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Direct };
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            Assert.AreEqual("未启用系统代理", vm.StatusText);
            Assert.IsTrue(vm.IsDirect);
            Assert.IsFalse(vm.IsPac);
            Assert.IsFalse(vm.IsGlobal);
        }

        [TestMethod]
        public void Start_WithGlobalMode_SetsIsGlobalAndShowsConnected()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Global };
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            Assert.AreEqual("已连接", vm.StatusText);
            Assert.IsTrue(vm.IsGlobal);
            Assert.IsFalse(vm.IsDirect);
            Assert.IsFalse(vm.IsPac);
        }

        [TestMethod]
        public void Start_WithNoModifyMode_ShowsNotEnabled()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.NoModify };
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            Assert.AreEqual("未启用系统代理", vm.StatusText);
            Assert.IsFalse(vm.IsDirect);
            Assert.IsFalse(vm.IsPac);
            Assert.IsFalse(vm.IsGlobal);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Server selection (CurrentNodeName)
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Start_WithNoServers_ShowsNoNode()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Direct };
            config.Configs.Clear();
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            Assert.AreEqual("无节点", vm.CurrentNodeName);
        }

        [TestMethod]
        public void Start_WithServer_ReflectsServerName()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Pac };
            config.Configs.Clear();
            config.Configs.Add(new Server
            {
                server = "test.example.com",
                Server_Port = 443,
            });
            config.Index = 0;
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            // FriendlyName = host:port when no Remarks is set
            Assert.AreEqual("test.example.com:443", vm.CurrentNodeName);
        }

        [TestMethod]
        public void Start_WithRemarkedServer_UsesRemarkAsFriendlyName()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Pac };
            config.Configs.Clear();
            config.Configs.Add(new Server
            {
                server = "hidden.example.com",
                Server_Port = 9999,
                // URL-safe Base64 for "Test Server"
                Remarks_Base64 = "VGVzdCBTZXJ2ZXI",
            });
            config.Index = 0;
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            // Remarks takes priority over host:port in FriendlyName
            Assert.AreEqual("Test Server", vm.CurrentNodeName);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Server index changes
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Start_ServerIndexChanged_ReflectsNewServerName()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Pac };
            config.Configs.Clear();
            config.Configs.Add(new Server { server = "alpha.example.com", Server_Port = 1111 });
            config.Configs.Add(new Server { server = "beta.example.com",  Server_Port = 2222 });
            config.Index = 0;
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();
            Assert.AreEqual("alpha.example.com:1111", vm.CurrentNodeName);

            // Change selection and refresh
            config.Index = 1;
            vm.Start();
            Assert.AreEqual("beta.example.com:2222", vm.CurrentNodeName);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Speed statistics (SpeedLog → DownSpeedText / UpSpeedText / totals)
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Start_WithSpeedLogTotals_ReflectsTotalByteTexts()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Global };
            config.Configs.Clear();
            var server = new Server { server = "fast.example.com", Server_Port = 8080 };
            // Constructor sets total upload=1024 and download=2048 bytes
            server.SpeedLog = new ServerSpeedLog(upload: 1024, download: 2048);
            config.Configs.Add(server);
            config.Index = 0;
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            vm.Start();

            Assert.AreEqual("fast.example.com:8080", vm.CurrentNodeName);
            // Utils.FormatBytes(1024) = "1.0KB", FormatBytes(2048) = "2.0KB"
            Assert.AreEqual("1.0KB", vm.TotalUpText);
            Assert.AreEqual("2.0KB", vm.TotalDownText);
            // Avg speeds: SpeedLog exists, but no TransLog entries → 0Byte/s
            Assert.AreEqual("0Byte/s", vm.DownSpeedText);
            Assert.AreEqual("0Byte/s", vm.UpSpeedText);
        }

        [TestMethod]
        public void Start_WithNullSpeedLog_DoesNotCrash()
        {
            var config = new Configuration { SysProxyMode = ProxyMode.Global };
            config.Configs.Clear();
            config.Configs.Add(new Server
            {
                server = "nolog.example.com",
                Server_Port = 5555,
                SpeedLog = null!,
            });
            config.Index = 0;
            var controller = CreateControllerStub(config);
            var vm = new DashboardViewModel(config, controller);

            // Must not throw
            vm.Start();

            Assert.AreEqual("nolog.example.com:5555", vm.CurrentNodeName);
            // When SpeedLog is null the speed text fields remain at their
            // constructor defaults.
            Assert.AreEqual("0 B/s", vm.DownSpeedText);
            Assert.AreEqual("0 B/s", vm.UpSpeedText);
            Assert.AreEqual("0 B", vm.TotalDownText);
            Assert.AreEqual("0 B", vm.TotalUpText);
        }
    }
}
