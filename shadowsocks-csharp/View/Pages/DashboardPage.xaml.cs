using Shadowsocks.ViewModel;
using System.Windows.Controls;

namespace Shadowsocks.View.Pages
{
    public partial class DashboardPage : Page
    {
        private readonly DashboardViewModel _viewModel;

        public DashboardPage(DashboardViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;

            Loaded += (_, _) => _viewModel.Start();
            Unloaded += (_, _) => _viewModel.Stop();
        }
    }
}
