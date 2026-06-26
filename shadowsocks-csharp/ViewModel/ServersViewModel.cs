using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Shadowsocks.Controller;
using Shadowsocks.Encryption;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Services;
using Shadowsocks.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Shadowsocks.ViewModel
{
    /// <summary>
    /// Backing view-model for the Fluent "Servers" page. Presents a master/detail layout:
    /// a SubTag / Group node tree on the left (built from the DI singleton configuration)
    /// and an editor for the selected <see cref="Server"/> on the right.
    /// <para>
    /// Migrated from the legacy <c>ServerConfigWindow</c> / <c>ServerConfigViewModel</c>.
    /// Node grouping and tree &lt;-&gt; list conversion are delegated to the existing
    /// <see cref="ServerConfigViewModel"/> so behaviour stays consistent with the old window.
    /// </para>
    /// Implements <see cref="IDropTarget"/> so nodes can be dragged between groups / reordered.
    /// </summary>
    public partial class ServersViewModel : ObservableObject, IDropTarget
    {
        private readonly Configuration _config;
        private readonly MainController _controller;
        private readonly IConfigPersistenceService _configPersistence;
        private readonly ServerConfigViewModel _tree = new();

        /// <summary>Candidate encryption methods for the editor combo box.</summary>
        public ObservableCollection<string> Methods { get; } = new();

        /// <summary>Candidate protocols for the editor combo box.</summary>
        public ObservableCollection<string> Protocols { get; } = new(new[]
        {
            @"origin",
            @"verify_deflate",
            @"auth_sha1_v4",
            @"auth_aes128_md5",
            @"auth_aes128_sha1",
            @"auth_chain_a",
            @"auth_chain_b",
            @"auth_chain_c",
            @"auth_chain_d",
            @"auth_chain_e",
            @"auth_chain_f",
            @"auth_akarin_rand",
            @"auth_akarin_spec_a"
        });

        /// <summary>Candidate obfs plugins for the editor combo box.</summary>
        public ObservableCollection<string> ObfsList { get; } = new(new[]
        {
            @"plain",
            @"http_simple",
            @"http_post",
            @"random_head",
            @"tls1.2_ticket_auth",
            @"tls1.2_ticket_fastauth"
        });

        /// <summary>Root nodes (SubTag / Group) of the server tree.</summary>
        public ObservableCollection<ServerTreeViewModel> Nodes => _tree.ServersTreeViewCollection;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsServerSelected))]
        private ServerTreeViewModel _selectedNode;

        [ObservableProperty] private string _statusText = string.Empty;

        /// <summary>QR code image for the selected server, or <c>null</c> when hidden.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsQrCodeVisible))]
        private System.Windows.Media.ImageSource _qrCodeImage;

        /// <summary>True when a QR code is currently generated and should be shown.</summary>
        public bool IsQrCodeVisible => QrCodeImage != null;

        /// <summary>True when a leaf (server) node is selected, so the editor should be shown.</summary>
        public bool IsServerSelected =>
            SelectedNode is { Type: ServerTreeViewType.Server, Server: not null };

        /// <summary>Convenience accessor: the selected server, or <c>null</c> for group/subtag nodes.</summary>
        public Server SelectedServer =>
            SelectedNode is { Type: ServerTreeViewType.Server } ? SelectedNode.Server : null;

        public ServersViewModel(Configuration config, MainController controller, IConfigPersistenceService configPersistence)
        {
            _config = config;
            _controller = controller;
            _configPersistence = configPersistence;

            foreach (var name in EncryptorFactory.RegisteredEncryptors.Keys
                         .Where(name => EncryptorFactory.GetEncryptorInfo(name).Display))
            {
                Methods.Add(name);
            }

            Reload();
        }

        partial void OnSelectedNodeChanged(ServerTreeViewModel value)
        {
            OnPropertyChanged(nameof(SelectedServer));
            // Drop any QR code shown for the previously selected server.
            QrCodeImage = null;
        }

        /// <summary>Rebuild the tree from the current in-memory configuration.</summary>
        public void Reload()
        {
            var configs = _config?.Configs ?? new List<Server>();
            // ReadServers groups by SubTag/Group exactly like the legacy window.
            // Pass a copy so the tree owns its own list and rebuilds cleanly.
            _tree.ReadServers(configs.ToList());
            OnPropertyChanged(nameof(Nodes));

            SelectedNode = FindFirstServer(Nodes);
        }

        private static ServerTreeViewModel FindFirstServer(IEnumerable<ServerTreeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Type == ServerTreeViewType.Server)
                {
                    return node;
                }

                var found = FindFirstServer(node.Nodes);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        #region Commands

        [RelayCommand]
        private void Add()
        {
            // Add a server into the currently selected group, mirroring the legacy AddButton logic.
            if (SelectedNode is { } st)
            {
                switch (st.Type)
                {
                    case ServerTreeViewType.Group:
                    {
                        var item = new ServerTreeViewModel
                        {
                            Type = ServerTreeViewType.Server,
                            Server = new Server { Group = st.Name }
                        };
                        st.Nodes.Add(item);
                        SelectedNode = item;
                        return;
                    }
                    case ServerTreeViewType.Server:
                    {
                        var parent = ServerTreeViewModel.FindParentNode(Nodes, st);
                        var item = new ServerTreeViewModel
                        {
                            Type = ServerTreeViewType.Server,
                            Server = new Server { Group = st.Server?.Group, SubTag = st.Server?.SubTag }
                        };
                        if (parent != null)
                        {
                            var index = parent.Nodes.IndexOf(st) + 1;
                            parent.Nodes.Insert(index, item);
                        }
                        else
                        {
                            Nodes.Add(item);
                        }
                        SelectedNode = item;
                        return;
                    }
                    case ServerTreeViewType.Subtag:
                        // Adding directly under a subtag is ambiguous; fall through to a fresh node.
                        break;
                }
            }

            // No suitable selection: create a brand-new server in a fresh tree entry.
            var newServer = new Server();
            var node = new ServerTreeViewModel
            {
                Type = ServerTreeViewType.Server,
                Server = newServer
            };
            Nodes.Add(node);
            SelectedNode = node;
        }

        [RelayCommand]
        private void Delete()
        {
            if (SelectedNode is null)
            {
                return;
            }

            ServerTreeViewModel.Remove(Nodes, SelectedNode);
            SelectedNode = FindFirstServer(Nodes);
        }

        [RelayCommand]
        private void Save()
        {
            var servers = ServerConfigViewModel.ServerTreeViewModelToList(Nodes).ToList();
            if (servers.Count == 0)
            {
                StatusText = @"至少需要一个服务器";
                return;
            }

            if (_config is null)
            {
                StatusText = @"配置不可用";
                return;
            }

            // Preserve the currently-selected server across the rebuild, like the old SaveConfig.
            string oldServerId = null;
            if (_config.Index >= 0 && _config.Index < _config.Configs.Count)
            {
                oldServerId = _config.Configs[_config.Index].Id;
            }

            _config.Configs = servers;
            if (oldServerId != null)
            {
                var currentIndex = servers.FindIndex(s => s.Id == oldServerId);
                if (currentIndex != -1)
                {
                    _config.Index = currentIndex;
                }
            }

            // Persist + notify so the running controller picks up the changes.
            // SaveAndNotifyChanged() calls Save internally and raises ConfigChanged.
            if (_controller is not null)
            {
                _controller.SaveAndNotifyChanged();
            }
            else
            {
                _configPersistence.Save(_config);
            }

            StatusText = $@"已保存 {servers.Count} 个服务器";
        }

        /// <summary>
        /// Import nodes from the clipboard. Each whitespace-separated token is parsed as an
        /// ss:// / ssr:// link via the existing <see cref="Server(string, string)"/> constructor;
        /// invalid lines are skipped. Imported servers are appended to the configuration, then
        /// saved and the tree refreshed.
        /// </summary>
        [RelayCommand]
        private void ImportFromClipboard()
        {
            if (_config is null)
            {
                StatusText = @"配置不可用";
                return;
            }

            string text;
            try
            {
                text = System.Windows.Clipboard.GetText();
            }
            catch (Exception)
            {
                StatusText = @"无法读取剪贴板";
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText = @"剪贴板为空";
                return;
            }

            var tokens = text.Split(new[] { '\r', '\n', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            var imported = 0;
            foreach (var token in tokens)
            {
                try
                {
                    var server = new Server(token, null);
                    _config.Configs.Add(server);
                    imported++;
                }
                catch (FormatException)
                {
                    // Skip lines that are not valid ss:// / ssr:// links.
                }
            }

            if (imported == 0)
            {
                StatusText = @"未找到可导入的有效链接";
                return;
            }

            // Persist + notify, then rebuild the tree from the updated configuration.
            if (_controller is not null)
            {
                _controller.SaveAndNotifyChanged();
            }
            else
            {
                _configPersistence.Save(_config);
            }

            Reload();
            StatusText = $@"已导入 {imported} 个服务器";
        }

        /// <summary>
        /// Generate and show a QR code for the selected server's SSR link, or hide it when no
        /// server is selected. Toggles visibility if a code is already showing.
        /// </summary>
        [RelayCommand]
        private void ShowQrCode()
        {
            if (IsQrCodeVisible)
            {
                QrCodeImage = null;
                return;
            }

            if (SelectedServer is not { } server)
            {
                StatusText = @"请先选择一个服务器";
                return;
            }

            try
            {
                QrCodeImage = QrCodeUtils.GenQrCode2(server.SsrLink, 300);
            }
            catch (Exception)
            {
                StatusText = @"二维码生成失败";
            }
        }

        /// <summary>Hide the QR code overlay.</summary>
        [RelayCommand]
        private void CloseQrCode()
        {
            QrCodeImage = null;
        }

        #endregion

        #region IDropTarget (drag to move between groups / reorder)

        /// <inheritdoc />
        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is not ServerTreeViewModel source || dropInfo.TargetItem is not ServerTreeViewModel target)
            {
                return;
            }

            // Disallow dropping a node onto itself or onto its own descendant.
            if (ReferenceEquals(source, target) || IsDescendant(source, target))
            {
                return;
            }

            // Source/target type validation matrix replicated from
            // ServerConfigWindow.ServersTreeView_OnItemDropping.
            //
            // Rule 1: Can't drop wrong type combos (Subtag→Group, Group→Server, etc.)
            // Rule 2: Can't drop server onto itself (handled above).
            // Rule 3: Can't nest beyond depth 2 (Subtag must be at root; Group→Group only same-parent siblings).
            // Rule 4: Can't drag across subscription boundaries (Group→Group requires same parent).
            switch (source.Type)
            {
                case ServerTreeViewType.Subtag:
                    // Subtags may only be reordered among root-level siblings.
                    if (ServerTreeViewModel.FindParentNode(Nodes, target) != null)
                    {
                        return; // Rule 1 + 3: target is nested, subtag can't go there.
                    }
                    if (target.Type == ServerTreeViewType.Subtag)
                    {
                        dropInfo.Effects = System.Windows.DragDropEffects.Move;
                        dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                    }
                    break;

                case ServerTreeViewType.Group:
                    if (target.Type == ServerTreeViewType.Subtag)
                    {
                        return; // Rule 1: can't drop a group onto a subtag.
                    }
                    if (target.Type == ServerTreeViewType.Group)
                    {
                        var targetParent = ServerTreeViewModel.FindParentNode(Nodes, target);
                        if (targetParent == null)
                        {
                            return; // Rule 3: target group at root has no parent for sibling reorder.
                        }
                        var sourceParent = ServerTreeViewModel.FindParentNode(Nodes, source);
                        if (!ReferenceEquals(sourceParent, targetParent))
                        {
                            return; // Rule 4: can't drag groups across subscription boundaries.
                        }
                        dropInfo.Effects = System.Windows.DragDropEffects.Move;
                        dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                    }
                    break;

                case ServerTreeViewType.Server:
                    if (target.Type == ServerTreeViewType.Subtag)
                    {
                        return; // Rule 1: can't drop a server onto a subtag.
                    }
                    dropInfo.Effects = System.Windows.DragDropEffects.Move;
                    dropInfo.DropTargetAdorner = target.Type == ServerTreeViewType.Server
                        ? DropTargetAdorners.Insert
                        : DropTargetAdorners.Highlight;
                    break;
            }
        }

        /// <inheritdoc />
        public void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is not ServerTreeViewModel source || dropInfo.TargetItem is not ServerTreeViewModel target)
            {
                return;
            }

            if (ReferenceEquals(source, target) || IsDescendant(source, target))
            {
                return;
            }

            var sourceParent = ServerTreeViewModel.FindParentNode(Nodes, source);
            var sourceCollection = sourceParent?.Nodes ?? Nodes;

            switch (target.Type)
            {
                case ServerTreeViewType.Group:
                {
                    if (source.Type == ServerTreeViewType.Server)
                    {
                        // Drop a server into a group -> reparent it and update Group/SubTag.
                        sourceCollection.Remove(source);
                        ApplyGroup(source, target);
                        target.Nodes.Add(source);
                        SelectedNode = source;
                    }
                    else if (source.Type == ServerTreeViewType.Group)
                    {
                        // Reorder a group beside another group within the same subtag parent.
                        var parent = ServerTreeViewModel.FindParentNode(Nodes, target);
                        if (parent != null)
                        {
                            MoveBeside(parent.Nodes, source, target, dropInfo);
                        }
                    }
                    break;
                }
                case ServerTreeViewType.Subtag:
                {
                    // Reorder subtag siblings at the root.
                    if (source.Type == ServerTreeViewType.Subtag)
                    {
                        MoveBeside(Nodes, source, target, dropInfo);
                    }
                    break;
                }
                case ServerTreeViewType.Server:
                {
                    // Drop a server next to another server -> same parent, copy Group/SubTag.
                    if (source.Type == ServerTreeViewType.Server)
                    {
                        var targetParent = ServerTreeViewModel.FindParentNode(Nodes, target);
                        var targetCollection = targetParent?.Nodes ?? Nodes;

                        sourceCollection.Remove(source);
                        if (source.Server != null && target.Server != null)
                        {
                            source.Server.Group = target.Server.Group;
                            source.Server.SubTag = target.Server.SubTag;
                        }

                        var index = targetCollection.IndexOf(target);
                        if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem))
                        {
                            index++;
                        }
                        index = Math.Clamp(index, 0, targetCollection.Count);
                        targetCollection.Insert(index, source);
                        SelectedNode = source;
                    }
                    break;
                }
            }
        }

        private void ApplyGroup(ServerTreeViewModel server, ServerTreeViewModel group)
        {
            if (server.Server is null)
            {
                return;
            }

            var emptyGroup = I18NUtil.GetAppStringValue(@"EmptyGroup");
            var emptySubtag = I18NUtil.GetAppStringValue(@"EmptySubtag");

            server.Server.Group = group.Name == emptyGroup ? string.Empty : group.Name;

            // Inherit the subtag from the group's parent (a subtag node) when one exists.
            var parent = ServerTreeViewModel.FindParentNode(Nodes, group);
            if (parent is { Type: ServerTreeViewType.Subtag })
            {
                server.Server.SubTag = parent.Name == emptySubtag ? string.Empty : parent.Name;
            }
        }

        private static void MoveBeside(ObservableCollection<ServerTreeViewModel> collection,
            ServerTreeViewModel source, ServerTreeViewModel target, IDropInfo dropInfo)
        {
            if (!collection.Contains(source) || !collection.Contains(target))
            {
                return;
            }

            collection.Remove(source);
            var index = collection.IndexOf(target);
            if (dropInfo.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem))
            {
                index++;
            }
            index = Math.Clamp(index, 0, collection.Count);
            collection.Insert(index, source);
        }

        private static bool IsDescendant(ServerTreeViewModel ancestor, ServerTreeViewModel node)
        {
            foreach (var child in ancestor.Nodes)
            {
                if (ReferenceEquals(child, node) || IsDescendant(child, node))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
