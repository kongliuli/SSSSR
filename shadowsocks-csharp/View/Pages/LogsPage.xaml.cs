using Shadowsocks.Util;
using Shadowsocks.ViewModel;
using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shadowsocks.View.Pages
{
    /// <summary>
    /// Read-only scrolling log viewer. Mirrors the legacy <c>LogWindow</c> behaviour
    /// (tail the current log file, append incrementally, keep pinned to the bottom) but
    /// hosted as a Fluent page. The <see cref="LogsViewModel"/> owns the file reading and
    /// toolbar state; this code-behind just feeds new text into the real <see cref="TextBox"/>
    /// and manages scroll position, which can only be done against the live control.
    /// </summary>
    public partial class LogsPage : Page
    {
        private readonly LogsViewModel _viewModel;
        private readonly DispatcherTimer _timer;

        public LogsPage(LogsViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => _viewModel.Refresh();

            _viewModel.NewTextRead += OnNewTextRead;

            Loaded += (_, _) =>
            {
                _viewModel.Refresh();
                _timer.Start();
            };
            Unloaded += (_, _) => _timer.Stop();
        }

        /// <summary>
        /// Append (or replace, on <paramref name="reset"/>) freshly read log text and keep the
        /// view pinned to the bottom when auto-scroll is on, or when the user was already at the
        /// end. Always invoked on the UI thread (raised from the dispatcher-driven timer).
        /// </summary>
        private void OnNewTextRead(string text, bool reset)
        {
            var wasAtEnd = LogTextBox.IsScrolledToEnd();

            if (reset)
            {
                LogTextBox.Text = text;
            }
            else if (!string.IsNullOrEmpty(text))
            {
                LogTextBox.AppendText(text);
            }
            else
            {
                return;
            }

            if (_viewModel.AutoScroll || wasAtEnd || reset)
            {
                LogTextBox.ScrollToEnd();
            }
        }
    }
}
