using Shadowsocks.Model;
using Shadowsocks.ViewModel;
using System;
using System.Windows.Controls;

namespace Shadowsocks.View.Pages
{
    public partial class SubscriptionsPage : Page
    {
        private readonly SubscriptionsViewModel _viewModel;
        private bool _configChangedHooked;

        public SubscriptionsPage(SubscriptionsViewModel vm)
        {
            _viewModel = vm;
            InitializeComponent();
            DataContext = vm;

            // 进入页面时刷新订阅列表（可能在后台被订阅更新改动过）
            Loaded += SubscriptionsPage_Loaded;
            // 离开页面时取消事件订阅，避免泄漏/重复刷新
            Unloaded += SubscriptionsPage_Unloaded;

            // 每张卡的启用开关切换时，停用/启用该订阅分组下的所有节点
            _viewModel.Subscriptions.CollectionChanged += Subscriptions_CollectionChanged;
            HookItems();
        }

        private void SubscriptionsPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _viewModel.Reload();
            HookConfigChanged();
        }

        private void SubscriptionsPage_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            UnhookConfigChanged();
        }

        /// <summary>
        /// 订阅在后台完成更新（写 LastUpdateTime / 加节点）后，<see cref="MainController"/> 会触发
        /// <c>ConfigChanged</c>，此时回到 UI 线程重建卡片列表。<see cref="Model.Global.Controller"/>
        /// 可能尚未初始化（null），做防御。
        /// </summary>
        private void HookConfigChanged()
        {
            if (_configChangedHooked)
            {
                return;
            }

            var controller = Global.Controller;
            if (controller is null)
            {
                return;
            }

            controller.ConfigChanged += Controller_ConfigChanged;
            _configChangedHooked = true;
        }

        private void UnhookConfigChanged()
        {
            if (!_configChangedHooked)
            {
                return;
            }

            var controller = Global.Controller;
            if (controller is not null)
            {
                controller.ConfigChanged -= Controller_ConfigChanged;
            }

            _configChangedHooked = false;
        }

        private void Controller_ConfigChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => _viewModel.Reload());
        }

        private void Subscriptions_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            HookItems();
        }

        private void HookItems()
        {
            foreach (var item in _viewModel.Subscriptions)
            {
                item.EnableChanged -= Item_EnableChanged;
                item.EnableChanged += Item_EnableChanged;
            }
        }

        private void Item_EnableChanged(object sender, bool enabled)
        {
            if (sender is SubscriptionItemViewModel item)
            {
                _viewModel.SetEnabled(item, enabled);
            }
        }
    }
}
