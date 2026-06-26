using GongSolutions.Wpf.DragDrop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shadowsocks.Controller;
using Shadowsocks.Enums;
using Shadowsocks.Model;
using Shadowsocks.Services;
using Shadowsocks.ViewModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace UnitTest
{
    [TestClass]
    public class ServersViewModelTests
    {
        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            if (Application.Current == null)
            {
                var app = new Application();
                // Add a merged dictionary containing the keys that I18NUtil looks up.
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    ["EmptyGroup"] = "Empty Group",
                    ["EmptySubtag"] = "Empty Subtag"
                });
            }
        }

        private static ServersViewModel CreateViewModel(
            Configuration config = null,
            MainController controller = null,
            IConfigPersistenceService persistence = null)
        {
            config ??= new Configuration();
            return new ServersViewModel(config, controller, persistence);
        }

        /// <summary>Recursively find the first node matching a predicate.</summary>
        private static ServerTreeViewModel FindNode(
            ObservableCollection<ServerTreeViewModel> nodes,
            System.Func<ServerTreeViewModel, bool> predicate)
        {
            foreach (var node in nodes)
            {
                if (predicate(node))
                    return node;
                var found = FindNode(node.Nodes, predicate);
                if (found != null)
                    return found;
            }
            return null;
        }

        #region Add tests

        [TestMethod]
        public void AddServer_NoSelection_CreatesRootServer()
        {
            var vm = CreateViewModel();
            Assert.IsNull(vm.SelectedNode);
            Assert.AreEqual(0, vm.Nodes.Count);

            vm.AddCommand.Execute(null);

            Assert.AreEqual(1, vm.Nodes.Count);
            Assert.IsNotNull(vm.SelectedNode);
            Assert.AreEqual(ServerTreeViewType.Server, vm.SelectedNode.Type);
            Assert.IsNotNull(vm.SelectedNode.Server);
        }

        [TestMethod]
        public void AddServer_GroupSelected_AddsChildServer()
        {
            var config = new Configuration();
            config.Configs.Add(new Server { Group = "G1" });
            config.Configs.Add(new Server { Group = "G2" });
            var vm = CreateViewModel(config);

            var groupNode = vm.Nodes[0].Nodes.First(n => n.Type == ServerTreeViewType.Group);
            int childCountBefore = groupNode.Nodes.Count;

            vm.SelectedNode = groupNode;
            vm.AddCommand.Execute(null);

            Assert.AreEqual(childCountBefore + 1, groupNode.Nodes.Count);
            Assert.IsNotNull(vm.SelectedNode);
            Assert.AreEqual(ServerTreeViewType.Server, vm.SelectedNode.Type);
        }

        [TestMethod]
        public void AddServer_ServerSelected_InsertsAfterSelected()
        {
            var config = new Configuration();
            var s1 = new Server { Group = "G1" };
            var s2 = new Server { Group = "G1" };
            config.Configs.Add(s1);
            config.Configs.Add(s2);
            config.Configs.Add(new Server { Group = "G2" });
            var vm = CreateViewModel(config);

            // Find the group containing s1 by checking children, avoiding Name/NameContains.
            var groupG1 = FindNode(vm.Nodes,
                n => n.Type == ServerTreeViewType.Group && n.Nodes.Any(c => c.Server == s1));
            Assert.IsNotNull(groupG1);
            var firstServer = groupG1.Nodes.First(n => n.Server == s1);
            vm.SelectedNode = firstServer;

            int countBefore = groupG1.Nodes.Count;
            int selectedIndex = groupG1.Nodes.IndexOf(firstServer);

            vm.AddCommand.Execute(null);

            Assert.AreEqual(countBefore + 1, groupG1.Nodes.Count);
            Assert.IsNotNull(vm.SelectedNode);
            Assert.AreEqual(selectedIndex + 1, groupG1.Nodes.IndexOf(vm.SelectedNode));
        }

        #endregion

        #region Delete tests

        [TestMethod]
        public void DeleteServer_RemovesSelectedNode()
        {
            var config = new Configuration();
            config.Configs.Add(new Server());
            var vm = CreateViewModel(config);

            var serverNode = FindNode(vm.Nodes, n => n.Type == ServerTreeViewType.Server);
            Assert.IsNotNull(serverNode);
            vm.SelectedNode = serverNode;

            vm.DeleteCommand.Execute(null);

            Assert.IsNull(FindNode(vm.Nodes, n => n.Type == ServerTreeViewType.Server));
        }

        [TestMethod]
        public void DeleteServer_ThenSelectsNextServer()
        {
            var config = new Configuration();
            var s1 = new Server { Group = "G1" };
            var s2 = new Server { Group = "G1" };
            config.Configs.Add(s1);
            config.Configs.Add(s2);
            config.Configs.Add(new Server { Group = "G2" });
            var vm = CreateViewModel(config);

            var groupG1 = FindNode(vm.Nodes,
                n => n.Type == ServerTreeViewType.Group && n.Nodes.Any(c => c.Server == s1));
            Assert.IsNotNull(groupG1);
            var firstServer = groupG1.Nodes.First(n => n.Server == s1);
            vm.SelectedNode = firstServer;

            vm.DeleteCommand.Execute(null);

            Assert.IsNotNull(vm.SelectedNode);
            Assert.AreSame(s2, vm.SelectedNode.Server);
        }

        #endregion

        #region Save tests

        [TestMethod]
        public void Save_TriggersPersistenceWhenControllerNull()
        {
            var config = new Configuration();
            config.Configs.Add(new Server { server = "example.com", Server_Port = 443 });

            var mockPersistence = Substitute.For<IConfigPersistenceService>();
            var vm = CreateViewModel(config, persistence: mockPersistence);

            vm.SaveCommand.Execute(null);

            mockPersistence.Received(1).Save(config);
            StringAssert.Contains(vm.StatusText, "已保存");
        }

        [TestMethod]
        public void Save_WritesServerListToConfig()
        {
            var config = new Configuration();
            config.Configs.Clear();
            var mockPersistence = Substitute.For<IConfigPersistenceService>();
            var vm = CreateViewModel(config, persistence: mockPersistence);

            vm.AddCommand.Execute(null);
            Assert.AreEqual(1, vm.Nodes.Count);

            vm.SaveCommand.Execute(null);

            Assert.AreEqual(1, config.Configs.Count);
            Assert.IsNotNull(config.Configs[0].Id);
        }

        #endregion

        #region Drag-over validation tests

        [TestMethod]
        public void DragOver_ServerOntoItself_RejectsDrop()
        {
            var vm = CreateViewModel();
            vm.AddCommand.Execute(null);
            var node = vm.Nodes[0];

            var dropInfo = Substitute.For<IDropInfo>();
            dropInfo.Data = node;
            dropInfo.TargetItem.Returns(node);

            vm.DragOver(dropInfo);

            Assert.AreNotEqual(DragDropEffects.Move, dropInfo.Effects);
        }

        [TestMethod]
        public void DragOver_GroupOntoSubtag_RejectsDrop()
        {
            var config = new Configuration();
            config.Configs.Add(new Server { Group = "G1" });
            config.Configs.Add(new Server { Group = "G2" });
            var vm = CreateViewModel(config);

            var subtagNode = vm.Nodes[0];
            Assert.AreEqual(ServerTreeViewType.Subtag, subtagNode.Type);
            var groupNode = subtagNode.Nodes.First(n => n.Type == ServerTreeViewType.Group);

            var dropInfo = Substitute.For<IDropInfo>();
            dropInfo.Data = groupNode;
            dropInfo.TargetItem.Returns(subtagNode);

            vm.DragOver(dropInfo);

            Assert.AreNotEqual(DragDropEffects.Move, dropInfo.Effects);
        }

        [TestMethod]
        public void DragOver_SubtagOntoNestedTarget_RejectsDrop()
        {
            var config = new Configuration();
            config.Configs.Add(new Server { Group = "G1" });
            config.Configs.Add(new Server { Group = "G2" });
            var vm = CreateViewModel(config);

            var subtagNode = vm.Nodes[0];
            var groupNode = subtagNode.Nodes.First(n => n.Type == ServerTreeViewType.Group);
            var serverNode = groupNode.Nodes.First(n => n.Type == ServerTreeViewType.Server);

            var dropInfo = Substitute.For<IDropInfo>();
            dropInfo.Data = subtagNode;
            dropInfo.TargetItem.Returns(serverNode);

            vm.DragOver(dropInfo);

            Assert.AreNotEqual(DragDropEffects.Move, dropInfo.Effects);
        }

        [TestMethod]
        public void DragOver_ServerOntoServer_AllowsMove()
        {
            var config = new Configuration();
            var s1 = new Server { Group = "G1", server = "alpha" };
            var s2 = new Server { Group = "G1", server = "beta" };
            config.Configs.Add(s1);
            config.Configs.Add(s2);
            config.Configs.Add(new Server { Group = "G2" });
            var vm = CreateViewModel(config);

            var groupG1 = FindNode(vm.Nodes,
                n => n.Type == ServerTreeViewType.Group && n.Nodes.Any(c => c.Server == s1));
            Assert.IsNotNull(groupG1);
            var server1 = groupG1.Nodes.First(n => n.Server == s1);
            var server2 = groupG1.Nodes.First(n => n.Server == s2);

            var dropInfo = Substitute.For<IDropInfo>();
            dropInfo.Data = server1;
            dropInfo.TargetItem.Returns(server2);

            vm.DragOver(dropInfo);

            Assert.AreEqual(DragDropEffects.Move, dropInfo.Effects);
        }

        [TestMethod]
        public void DragOver_GroupOntoGroupDifferentParent_RejectsDrop()
        {
            var config = new Configuration();
            var sStAG1 = new Server { SubTag = "ST-A", Group = "G1" };
            config.Configs.Add(sStAG1);
            config.Configs.Add(new Server { SubTag = "ST-A", Group = "G1-extra" });
            config.Configs.Add(new Server { SubTag = "ST-B", Group = "G2" });
            config.Configs.Add(new Server { SubTag = "ST-B", Group = "G2-extra" });
            var vm = CreateViewModel(config);

            // Find group G1 (under ST-A) by tracking a server reference.
            var groupA = FindNode(vm.Nodes,
                n => n.Type == ServerTreeViewType.Group && n.Nodes.Any(c => c.Server == sStAG1));
            Assert.IsNotNull(groupA);
            // Find group G2 (under ST-B): first find ST-B's subtag, then its first group.
            var groupB = FindNode(vm.Nodes,
                n => n.Type == ServerTreeViewType.Group
                    && n.Server == null
                    && FindNode(vm.Nodes, p => p.Nodes.Contains(n)) is { Type: ServerTreeViewType.Subtag } parent
                    && !parent.Nodes.Any(c => c.Server == sStAG1));
            // Simpler: just pick the second subtag's first group by index.
            var subtagB = vm.Nodes.Last();
            groupB = subtagB.Nodes.First(n => n.Type == ServerTreeViewType.Group);

            var dropInfo = Substitute.For<IDropInfo>();
            dropInfo.Data = groupA;
            dropInfo.TargetItem.Returns(groupB);

            vm.DragOver(dropInfo);

            Assert.AreNotEqual(DragDropEffects.Move, dropInfo.Effects);
        }

        #endregion

        #region Drop (reorder) tests

        [TestMethod]
        public void Drop_ServerIntoGroup_ReparentsServer()
        {
            var config = new Configuration();
            var rootServer = new Server { server = "root-srv" };
            config.Configs.Add(rootServer);
            config.Configs.Add(new Server { Group = "TargetGroup" });
            config.Configs.Add(new Server { Group = "Other" });
            var vm = CreateViewModel(config);

            var rootSrvNode = FindNode(vm.Nodes, n => n.Server == rootServer);
            Assert.IsNotNull(rootSrvNode);
            var groupNode = FindNode(vm.Nodes,
                n => n.Type == ServerTreeViewType.Group
                    && n.Nodes.Any(c => c.Server?.Group == "TargetGroup"));
            Assert.IsNotNull(groupNode);
            int groupChildCountBefore = groupNode.Nodes.Count;

            var dropInfo = Substitute.For<IDropInfo>();
            dropInfo.Data = rootSrvNode;
            dropInfo.TargetItem.Returns(groupNode);

            vm.Drop(dropInfo);

            Assert.IsTrue(groupNode.Nodes.Any(n => n.Server == rootServer),
                "Root server should now be a child of the target group.");
            Assert.AreEqual(groupChildCountBefore + 1, groupNode.Nodes.Count);
        }

        [TestMethod]
        public void Drop_ServerBesideServer_ReordersWithinParent()
        {
            var config = new Configuration();
            var sA = new Server { Group = "G1", server = "A" };
            var sB = new Server { Group = "G1", server = "B" };
            var sC = new Server { Group = "G1", server = "C" };
            config.Configs.Add(sA);
            config.Configs.Add(sB);
            config.Configs.Add(sC);
            config.Configs.Add(new Server { Group = "G2" });
            var vm = CreateViewModel(config);

            var groupG1 = FindNode(vm.Nodes,
                n => n.Type == ServerTreeViewType.Group && n.Nodes.Any(c => c.Server == sA));
            Assert.IsNotNull(groupG1);
            var nodeA = groupG1.Nodes.First(n => n.Server == sA);
            var nodeB = groupG1.Nodes.First(n => n.Server == sB);
            var nodeC = groupG1.Nodes.First(n => n.Server == sC);

            var dropInfo = Substitute.For<IDropInfo>();
            dropInfo.Data = nodeC;
            dropInfo.TargetItem.Returns(nodeA);
            dropInfo.InsertPosition.Returns((RelativeInsertPosition)0);

            vm.Drop(dropInfo);

            Assert.AreEqual(3, groupG1.Nodes.Count);
            Assert.AreSame(sC, groupG1.Nodes[0].Server);
            Assert.AreSame(sA, groupG1.Nodes[1].Server);
            Assert.AreSame(sB, groupG1.Nodes[2].Server);
        }

        #endregion
    }
}
