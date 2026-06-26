using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shadowsocks.Controller;
using Shadowsocks.Controller.HttpRequest;
using Shadowsocks.Controller.Service;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Services;

namespace UnitTest
{
    [TestClass]
    public class MainControllerTests
    {
        /// <summary>
        /// Builds a real MainController with empty config and mocked dependencies.
        /// Constructor side-effects: loads ServerTransferTotal (safe, disk read),
        /// iterates config servers (no-op for empty config).
        /// </summary>
        private static (
            MainController Controller,
            Configuration Config,
            UpdateNode UpdateNode,
            UpdateSubscribeManager SubscribeMgr,
            IConfigPersistenceService Persistence
            ) CreateController(Configuration config = null)
        {
            config ??= new Configuration();
            var updateNode = Substitute.For<UpdateNode>();
            var subscribeMgr = Substitute.For<UpdateSubscribeManager>();
            var persistence = Substitute.For<IConfigPersistenceService>();
            var controller = new MainController(config, updateNode, subscribeMgr, persistence);
            return (controller, config, updateNode, subscribeMgr, persistence);
        }

        #region Constructor

        [TestMethod]
        public void Constructor_SetsLiveConfigProperty()
        {
            // Arrange
            var config = new Configuration();

            // Act
            var (controller, _, _, _, _) = CreateController(config);

            // Assert: LiveConfig exposes the injected configuration
            Assert.IsNotNull(controller.LiveConfig);
            Assert.AreSame(config, controller.LiveConfig);
        }

        [TestMethod]
        public void Constructor_EmptyConfig_DoesNotThrow()
        {
            // Act & Assert: constructing with empty config completes without error
            var (controller, _, _, _, _) = CreateController();
            Assert.IsNotNull(controller);
        }

        #endregion

        #region Stop

        [TestMethod]
        public void Stop_FreshController_CompletesWithoutError()
        {
            // Arrange: fresh controller with no listeners started, SysProxyMode=NoModify
            var (controller, config, _, _, _) = CreateController();

            // Act
            controller.Stop();

            // Assert: no exception thrown; stopped flag internally set
            // Calling again should also not throw (idempotent check)
            controller.Stop(); // should return immediately
        }

        [TestMethod]
        public void Stop_CalledTwice_IsIdempotent()
        {
            // Arrange
            var (controller, _, _, _, _) = CreateController();

            // Act
            controller.Stop();
            controller.Stop(); // second call must not throw

            // Assert: no assertion needed — reaching here means no exception
        }

        #endregion

        #region SaveAndNotifyChanged

        [TestMethod]
        public void SaveAndNotifyChanged_CallsPersistenceService()
        {
            // Arrange
            var (controller, config, _, _, persistence) = CreateController();
            config.LocalPort = 5555; // mutate config before save

            // Act: SaveAndNotifyChanged persists BEFORE the dispatcher call;
            // the dispatcher will NRE when Application.Current is null in tests.
            try { controller.SaveAndNotifyChanged(); }
            catch (System.NullReferenceException) { /* WPF Application.Current unavailable */ }

            // Assert: persistence.Save was called with the config
            persistence.Received(1).Save(config);
        }

        #endregion

        #region ToggleSelectAutoCheckUpdate

        [TestMethod]
        public void ToggleSelectAutoCheckUpdate_Disable_SavesConfig()
        {
            // Arrange: default config has AutoCheckUpdate = true
            var (controller, config, _, _, persistence) = CreateController();
            Assert.IsTrue(config.AutoCheckUpdate, "Default should be true.");

            // Act
            controller.ToggleSelectAutoCheckUpdate(false);

            // Assert
            Assert.IsFalse(config.AutoCheckUpdate);
            persistence.Received(1).Save(config);
        }

        [TestMethod]
        public void ToggleSelectAutoCheckUpdate_Enable_SavesConfig()
        {
            // Arrange
            var (controller, config, _, _, persistence) = CreateController();
            config.AutoCheckUpdate = false; // start disabled

            // Act
            controller.ToggleSelectAutoCheckUpdate(true);

            // Assert
            Assert.IsTrue(config.AutoCheckUpdate);
            persistence.Received(1).Save(config);
        }

        #endregion

        #region SelectServerIndex

        [TestMethod]
        public void SelectServerIndex_UpdatesIndex()
        {
            // Arrange: add servers so the index is within range
            var config = new Configuration();
            config.Configs.Add(new Server { server = "srv1.example.com" });
            config.Configs.Add(new Server { server = "srv2.example.com" });
            var (controller, _, _, _, persistence) = CreateController(config);

            // Act: SelectServerIndex calls SaveAndNotifyChanged internally,
            // which will NRE when Application.Current is null.
            try { controller.SelectServerIndex(1); }
            catch (System.NullReferenceException) { /* WPF Application.Current unavailable */ }

            // Assert
            Assert.AreEqual(1, config.Index);
            persistence.Received(1).Save(config);
        }

        #endregion

        #region ClearTransferTotal

        [TestMethod]
        public void ClearTransferTotal_UnknownServerId_DoesNotThrow()
        {
            // Arrange
            var (controller, _, _, _, _) = CreateController();

            // Act: clearing transfer for a server that doesn't exist
            controller.ClearTransferTotal("nonexistent-id");

            // Assert: no exception thrown
        }

        #endregion

        #region AddServerBySsUrl — error handling

        [TestMethod]
        public void AddServerBySsUrl_InvalidUrl_ReturnsFalse()
        {
            // Arrange
            var (controller, _, _, _, _) = CreateController();

            // Act: pass garbage URL that doesn't start with ss:// or ssr://
            var result = controller.AddServerBySsUrl("not-a-valid-url");

            // Assert: returns false, no exception
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void AddServerBySsUrl_EmptyString_ReturnsFalse()
        {
            // Arrange
            var (controller, _, _, _, _) = CreateController();

            // Act: empty string
            var result = controller.AddServerBySsUrl("");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void AddServerBySsUrl_NullInput_ReturnsFalse()
        {
            // Arrange
            var (controller, _, _, _, _) = CreateController();

            // Act: null input — GetLines() extension should handle or throw
            try
            {
                var result = controller.AddServerBySsUrl(null);
                Assert.IsFalse(result);
            }
            catch (System.ArgumentNullException)
            {
                // Acceptable: null input may throw; main controller method
                // catches Exception and returns false, but GetLines() extension
                // may throw ArgumentNullException before reaching the try block.
            }
        }

        #endregion

        #region ToggleRuleMode

        [TestMethod]
        public void ToggleRuleMode_ChangesProxyRuleMode()
        {
            // Arrange
            var (controller, config, _, _, persistence) = CreateController();
            Assert.AreEqual(ProxyRuleMode.Disable, config.ProxyRuleMode, "Default should be Disable.");

            // Act: ToggleRuleMode calls SaveAndNotifyChanged internally
            try { controller.ToggleRuleMode(ProxyRuleMode.BypassLan); }
            catch (System.NullReferenceException) { /* WPF Application.Current unavailable */ }

            // Assert
            Assert.AreEqual(ProxyRuleMode.BypassLan, config.ProxyRuleMode);
            persistence.Received(1).Save(config);
        }

        #endregion

        #region ToggleSelectRandom

        [TestMethod]
        public void ToggleSelectRandom_EnablesAndSaves()
        {
            // Arrange: default Random = false
            var config = new Configuration { Random = false };
            var (controller, _, _, _, persistence) = CreateController(config);

            // Act: ToggleSelectRandom calls SaveAndNotifyChanged internally
            try { controller.ToggleSelectRandom(true); }
            catch (System.NullReferenceException) { /* WPF Application.Current unavailable */ }

            // Assert
            Assert.IsTrue(config.Random);
            persistence.Received(1).Save(config);
        }

        #endregion
    }
}
