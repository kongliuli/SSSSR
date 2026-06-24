using CryptoBase;
using Microsoft.Win32;
using Shadowsocks.Controller;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Services;
using Shadowsocks.Util;
using SingleInstance;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Utils = Shadowsocks.Util.Utils;

namespace Shadowsocks
{
    internal static class Program
    {
        private static MainController _controller;
        private static MenuViewController _viewController;

        [STAThread]
        private static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Utils.GetExecutablePath()) ?? throw new InvalidOperationException());
            var identifier = $@"Global\{Controller.HttpRequest.UpdateChecker.Name}_{Directory.GetCurrentDirectory().GetClassicHashCode()}";
            using var singleInstance = new SingleInstanceService(identifier);
            if (!singleInstance.TryStartSingleInstance())
            {
                SendCommand(singleInstance, args.Length <= 0 ? Constants.ParameterMultiplyInstance : string.Join(' ', args));
                return;
            }
            using var d = singleInstance.Received.Subscribe(ArgumentsReceived);

            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            app.Exit += App_Exit;

            Global.LoadConfig();
            var config = Global.GuiConfig; // snapshot for DI registration

            I18NUtil.SetLanguage(config.LangName);
            ViewUtils.SetResource(app.Resources, @"../View/NotifyIconResources.xaml", 1);

            // Fluent (WPF-UI) theme. Appended after the i18n/notify-icon dictionaries so
            // I18NUtil's MergedDictionaries[0]/[1] indexing stays valid.
            ThemeUtil.ApplyFluentTheme(app, ThemeUtil.Resolve(config.ThemeMode));

            // Build the DI container, then resolve the core singletons from it instead of
            // newing them up. Global still mirrors the instances for legacy code.
            AppHost.Init(config);
            _controller = AppHost.Get<MainController>();
            Global.Controller = _controller; // bridge for legacy consumers

            // Logging
            Logging.DefaultOut = Console.Out;
            Logging.DefaultError = Console.Error;

            Utils.SetTls();

            _viewController = AppHost.Get<MenuViewController>();
            Global.ViewController = _viewController; // bridge for legacy consumers
            SystemEvents.SessionEnding += _viewController.Quit_Click;

            _controller.Reload();
            if (config.IsDefaultConfig())
            {
                var res = MessageBox.Show(
                $@"{I18NUtil.GetAppStringValue(@"DefaultConfigMessage")}{Environment.NewLine}{I18NUtil.GetAppStringValue(@"DefaultConfigQuestion")}",
                Controller.HttpRequest.UpdateChecker.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.OK);
                switch (res)
                {
                    case MessageBoxResult.Yes:
                    {
                        _controller.ShowConfigForm();
                        break;
                    }
                    case MessageBoxResult.No:
                    {
                        _controller.ShowSubscribeWindow();
                        break;
                    }
                    default:
                    {
                        StopController();
                        return;
                    }
                }
            }

            Reg.SetUrlProtocol(@"ssr");
            Reg.SetUrlProtocol(@"sub");

            singleInstance.StartListenServer();
            app.Run();
        }

        private static void StopController()
        {
            _viewController?.Quit_Click(default, default);
            _controller?.Stop();
            _controller = null;
            Global.Controller = null;
        }

        private static void App_Exit(object sender, ExitEventArgs e)
        {
            Reg.RemoveUrlProtocol(@"ssr");
            Reg.RemoveUrlProtocol(@"sub");
            StopController();
        }

        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                {
                    Logging.Info("os wake up");
                    if (_controller != null)
                    {
                        Task.Run(() =>
                        {
                            Thread.Sleep(10 * 1000);
                            try
                            {
                                _controller.Reload();
                                Logging.Info("controller started");
                            }
                            catch (Exception ex)
                            {
                                Logging.LogUsefulException(ex);
                            }
                        });
                    }
                    break;
                }
                case PowerModes.Suspend:
                {
                    if (_controller != null)
                    {
                        _controller.Stop();
                        Logging.Info("controller stopped");
                    }
                    Logging.Info("os suspend");
                    break;
                }
            }
        }

        private static int _exited;
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (Interlocked.Increment(ref _exited) == 1)
            {
                Logging.Log(LogLevel.Error, $@"{e.ExceptionObject}");
                MessageBox.Show(
                $@"{I18NUtil.GetAppStringValue(@"UnexpectedError")}{Environment.NewLine}{e.ExceptionObject}",
                Controller.HttpRequest.UpdateChecker.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private static void SendCommand(ISingleInstanceService service, string command)
        {
            try
            {
                service.SendMessageToFirstInstanceAsync(command).GetAwaiter().GetResult();
            }
            catch
            {
                // ignored
            }
        }

        private static void ArgumentsReceived((string, Action<string>) receive)
        {
            var (message, endFunc) = receive;
            var args = message
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet();

            if (args.Contains(Constants.ParameterMultiplyInstance))
            {
                MessageBox.Show(I18NUtil.GetAppStringValue(@"SuccessiveInstancesMessage1") + Environment.NewLine +
                                I18NUtil.GetAppStringValue(@"SuccessiveInstancesMessage2"),
                    I18NUtil.GetAppStringValue(@"SuccessiveInstancesCaption"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Application.Current.Dispatcher?.InvokeAsync(() =>
            {
                _viewController.ImportAddress(string.Join(Environment.NewLine, args));
            });

            endFunc(string.Empty);
        }
    }
}
