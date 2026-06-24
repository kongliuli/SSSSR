using Shadowsocks.ViewModel;
using System.Windows.Controls;

namespace Shadowsocks.View.Pages
{
    public partial class PortForwardingPage : Page
    {
        private readonly PortForwardingViewModel _viewModel;

        public PortForwardingPage(PortForwardingViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;

            // Refresh rules from the on-disk configuration each time the page is shown so it
            // reflects external changes (e.g. server list edits) made elsewhere.
            Loaded += (_, _) => _viewModel.Load();
        }
    }
}
