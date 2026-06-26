using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Services;
using Shadowsocks.ViewModel;
using System;

namespace UnitTest
{
    [TestClass]
    public class SettingsViewModelTests
    {
        private IConfigPersistenceService _mockPersistence;
        private Configuration _config;

        /// <summary>
        /// Each test starts with a fresh Configuration and mock persistence.
        /// The config uses ThemeMode = System so the ViewModel constructor
        /// does not trigger OnSelectedThemeChanged (which calls WPF
        /// ApplicationThemeManager.Apply and would throw in a test runner).
        /// </summary>
        [TestInitialize]
        public void SetUp()
        {
            _mockPersistence = Substitute.For<IConfigPersistenceService>();
            _config = new Configuration { ThemeMode = AppThemeMode.System };
            _mockPersistence.Load().Returns(_config);
        }

        // ── Constructor & Reload ──────────────────────────────────────

        [TestMethod]
        public void Constructor_LoadsConfigFromPersistence()
        {
            // Act
            var vm = new SettingsViewModel(null, _mockPersistence);

            // Assert
            Assert.IsNotNull(vm.Config);
            _mockPersistence.Received(1).Load();
        }

        [TestMethod]
        public void Constructor_SetsSelectedTheme_FromConfigThemeMode()
        {
            // Arrange – config already has ThemeMode = System
            var vm = new SettingsViewModel(null, _mockPersistence);

            // Assert – Themes[0] is the System option
            Assert.IsNotNull(vm.SelectedTheme);
            Assert.AreEqual(AppThemeMode.System, vm.SelectedTheme.Mode);
        }

        [TestMethod]
        public void Reload_RefreshesConfig_WhenPersistenceReturnsNewObject()
        {
            // Return a *new* Configuration on every Load() so the
            // ObservableProperty setter fires (it skips same-reference).
            var callCount = 0;
            _mockPersistence.Load().Returns(_ =>
            {
                callCount++;
                return new Configuration
                {
                    LocalPort = callCount == 1 ? 1080 : 9999,
                    ThemeMode = AppThemeMode.System
                };
            });

            var vm = new SettingsViewModel(null, _mockPersistence);
            Assert.AreEqual(1080, vm.Config.LocalPort, "Should start with default port.");

            // Act
            vm.Reload();

            // Assert
            Assert.AreEqual(9999, vm.Config.LocalPort,
                "Reload should replace Config with the freshly-loaded object.");
        }

        // ── Proxy port settings ───────────────────────────────────────

        [TestMethod]
        public void Config_LocalPort_CanBeSetAndRead()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            vm.Config.LocalPort = 8888;

            Assert.AreEqual(8888, vm.Config.LocalPort);
        }

        [TestMethod]
        public void Config_ProxyPort_CanBeSetAndRead()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            vm.Config.ProxyPort = 3128;

            Assert.AreEqual(3128, vm.Config.ProxyPort);
        }

        // ── Language ──────────────────────────────────────────────────

        [TestMethod]
        public void Config_LangName_CanBeChanged()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            vm.Config.LangName = "zh-CN";

            Assert.AreEqual("zh-CN", vm.Config.LangName);
        }

        [TestMethod]
        public void Languages_Collection_HasExpectedCultures()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            Assert.AreEqual(3, vm.Languages.Count);
            CollectionAssert.Contains(vm.Languages, "en-US");
            CollectionAssert.Contains(vm.Languages, "zh-CN");
            CollectionAssert.Contains(vm.Languages, "zh-TW");
        }

        // ── Theme ─────────────────────────────────────────────────────

        [TestMethod]
        public void Themes_Collection_HasThreeOptions()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            Assert.AreEqual(3, vm.Themes.Count);
            Assert.AreEqual(AppThemeMode.System, vm.Themes[0].Mode);
            Assert.AreEqual(AppThemeMode.Light, vm.Themes[1].Mode);
            Assert.AreEqual(AppThemeMode.Dark, vm.Themes[2].Mode);
        }

        [TestMethod]
        public void Config_ThemeMode_ChangesAreReflectedInConfig()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            // Set theme directly on Config (bypasses the WPF Apply call)
            vm.Config.ThemeMode = AppThemeMode.Dark;

            Assert.AreEqual(AppThemeMode.Dark, vm.Config.ThemeMode);
        }

        // ── Save ──────────────────────────────────────────────────────

        [TestMethod]
        public void Save_DoesNotThrow_WithNullController()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            // Act – Save uses _controller?., so null is safe
            vm.SaveCommand.Execute(null);

            // Assert – no exception means success.  StatusText should not
            // be the "配置不可用" error because Config is not null.
            Assert.AreNotEqual("配置不可用", vm.StatusText);
        }

        // ── AutoStartup ───────────────────────────────────────────────

        [TestMethod]
        public void AutoStartup_CanBeToggled()
        {
            var vm = new SettingsViewModel(null, _mockPersistence);

            Assert.IsFalse(vm.AutoStartup, "Default should be false when running under test.");

            vm.AutoStartup = true;
            Assert.IsTrue(vm.AutoStartup);

            vm.AutoStartup = false;
            Assert.IsFalse(vm.AutoStartup);
        }

        // ── Reset ─────────────────────────────────────────────────────

        [TestMethod]
        public void ResetCommand_ReloadsConfig()
        {
            // Return a new Configuration object on each Load() call so
            // the Config setter actually replaces it.
            _mockPersistence.Load().Returns(_ =>
                new Configuration { LocalPort = 1080, ThemeMode = AppThemeMode.System });

            var vm = new SettingsViewModel(null, _mockPersistence);

            // Change config, then reset
            vm.Config.LocalPort = 12345;
            Assert.AreEqual(12345, vm.Config.LocalPort);

            vm.ResetCommand.Execute(null);

            // After reset, Config is a fresh object with the default port.
            Assert.AreEqual(1080, vm.Config.LocalPort,
                "Reset should reload the config from persistence, restoring original values.");
        }
    }
}
