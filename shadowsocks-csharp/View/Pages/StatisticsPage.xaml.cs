using Shadowsocks.ViewModel;
using System.Windows.Controls;

namespace Shadowsocks.View.Pages
{
    public partial class StatisticsPage : Page
    {
        private readonly StatisticsViewModel _viewModel;

        public StatisticsPage(StatisticsViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;

            Loaded += (_, _) => _viewModel.Start();
            Unloaded += (_, _) => _viewModel.Stop();
        }
    }
}
