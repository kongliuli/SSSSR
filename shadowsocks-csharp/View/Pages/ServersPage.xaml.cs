using Shadowsocks.Model;
using Shadowsocks.ViewModel;
using System.Windows;
using System.Windows.Controls;

namespace Shadowsocks.View.Pages
{
    /// <summary>
    /// Fluent "Servers" page: master/detail server management migrated from the legacy
    /// <c>ServerConfigWindow</c>. Hosts a SubTag/Group node tree on the left and an editor
    /// for the selected <see cref="Server"/> on the right.
    /// </summary>
    public partial class ServersPage : Page
    {
        private readonly ServersViewModel _viewModel;

        public ServersPage(ServersViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>
        /// WPF's <see cref="TreeView.SelectedItem"/> is read-only, so the selection is pushed
        /// into the view-model from the change event instead of via a two-way binding.
        /// </summary>
        private void ServersTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is ServerTreeViewModel node)
            {
                _viewModel.SelectedNode = node;
            }
        }
    }
}
