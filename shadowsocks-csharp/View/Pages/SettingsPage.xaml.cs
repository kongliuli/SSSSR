using Shadowsocks.ViewModel;
using System.Windows.Controls;

namespace Shadowsocks.View.Pages
{
    public partial class SettingsPage : Page
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsPage(SettingsViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;

            // Reload from disk every time the page is shown so external config changes are reflected.
            Loaded += (_, _) => _viewModel.Reload();
        }
    }
}
