using BrightIdeasSoftware;
using mRemoteNG.App;
using mRemoteNG.Config.Putty;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.MultiUser;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.UI.Controls
{
    public partial class ConnectionTree : TreeListView, IConnectionTree
    {
        private readonly ConnectionTreeDragAndDropHandler _dragAndDropHandler = new ConnectionTreeDragAndDropHandler();
        private readonly PuttySessionsManager _puttySessionsManager = PuttySessionsManager.Instance;
	    private readonly StatusImageList _statusImageList = new StatusImageList();
        private readonly ConnectionTreeSearchTextFilter _connectionTreeSearchTextFilter = new ConnectionTreeSearchTextFilter();
        private readonly ConnectionTreeAccessFilter _accessFilter = new ConnectionTreeAccessFilter();
        private bool _searchFilterActive;
        private bool _nodeInEditMode;
        private bool _allowEdit;
        private ConnectionContextMenu _contextMenu;
        private ConnectionTreeModel _connectionTreeModel;

        public ConnectionInfo SelectedNode => (ConnectionInfo) SelectedObject;

        public NodeSearcher NodeSearcher { get; private set; }

        public IConfirm<ConnectionInfo> NodeDeletionConfirmer { get; set; } = new AlwaysConfirmYes();

        public IEnumerable<IConnectionTreeDelegate> PostSetupActions { get; set; } = new IConnectionTreeDelegate[0];

        public ITreeNodeClickHandler<ConnectionInfo> DoubleClickHandler { get; set; } = new TreeNodeCompositeClickHandler();

        public ITreeNodeClickHandler<ConnectionInfo> SingleClickHandler { get; set; } = new TreeNodeCompositeClickHandler();

        public ConnectionTreeModel ConnectionTreeModel
        {
            get { return _connectionTreeModel; }
            set
            {
                if (_connectionTreeModel == value)
                    return;

                UnregisterModelUpdateHandlers(_connectionTreeModel);
                _connectionTreeModel = value;
                PopulateTreeView(value);
            }
        }

        public ConnectionTree()
        {
            InitializeComponent();
            SetupConnectionTreeView();
            UseOverlays = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ActiveSessionTracker.Instance.SessionsChanged -= OnActiveSessionsChanged;
                MultiUserService.PermissionsChanged -= OnPermissionsChanged;
                components?.Dispose();
                _statusImageList?.Dispose();
            }
            base.Dispose(disposing);
        }


        #region ConnectionTree Setup
        private void SetupConnectionTreeView()
        {
            SmallImageList = _statusImageList.ImageList;
            AddColumns(_statusImageList.ImageGetter);
            LinkModelToView();
            _contextMenu = new ConnectionContextMenu(this);
            ContextMenuStrip = _contextMenu;
            SetupDropSink();
            SetEventHandlers();
        }

        private void AddColumns(ImageGetterDelegate imageGetterDelegate)
        {
            Columns.Add(new NameColumn(imageGetterDelegate));
        }

        private void LinkModelToView()
        {
            CanExpandGetter = item =>
            {
                var itemAsContainer = item as ContainerInfo;
                return itemAsContainer?.Children.Count > 0;
            };
            ChildrenGetter = item => ((ContainerInfo)item).Children;
        }

        private void SetupDropSink()
        {
            DropSink = new SimpleDropSink
            {
                CanDropBetween = true
            };
        }

        private void SetEventHandlers()
        {
            Collapsed += (sender, args) =>
            {
                var container = args.Model as ContainerInfo;
                if (container == null) return;
                container.IsExpanded = false;
				AutoResizeColumn(Columns[0]);
			};
            Expanded += (sender, args) =>
            {
                var container = args.Model as ContainerInfo;
                if (container == null) return;
                container.IsExpanded = true;
				AutoResizeColumn(Columns[0]);
			};
            SelectionChanged += tvConnections_AfterSelect;
            MouseDoubleClick += OnMouse_DoubleClick;
            MouseClick += OnMouse_SingleClick;
            CellToolTipShowing += tvConnections_CellToolTipShowing;
            ModelCanDrop += _dragAndDropHandler.HandleEvent_ModelCanDrop;
            ModelDropped += _dragAndDropHandler.HandleEvent_ModelDropped;
            BeforeLabelEdit += OnBeforeLabelEdit;
            AfterLabelEdit += OnAfterLabelEdit;
            FormatRow += OnFormatRow;
            ActiveSessionTracker.Instance.SessionsChanged += OnActiveSessionsChanged;
            MultiUserService.PermissionsChanged += OnPermissionsChanged;
        }

		/// <summary>
		/// Resizes the given column to ensure that all content is shown
		/// </summary>
	    private void AutoResizeColumn(ColumnHeader column)
	    {
		    if (InvokeRequired)
		    {
			    Invoke((MethodInvoker) (() => AutoResizeColumn(column)));
			    return;
		    }

		    var longestIndentationAndTextWidth = int.MinValue;
		    var horizontalScrollOffset = LowLevelScrollPosition.X;
		    const int padding = 10;

		    for (var i = 0; i < Items.Count; i++)
		    {
			    var rowIndentation = Items[i].Position.X;
			    var rowTextWidth = TextRenderer.MeasureText(Items[i].Text, Font).Width;

				longestIndentationAndTextWidth = Math.Max(rowIndentation + rowTextWidth, longestIndentationAndTextWidth);
		    }

		    column.Width = longestIndentationAndTextWidth +
		                   SmallImageSize.Width +
		                   horizontalScrollOffset +
		                   padding;
		}

        private void PopulateTreeView(ConnectionTreeModel newModel)
        {
            SetObjects(newModel.RootNodes);
            RefreshAccessFiltering();
            RegisterModelUpdateHandlers(newModel);
            NodeSearcher = new NodeSearcher(newModel);
            ExecutePostSetupActions();
			AutoResizeColumn(Columns[0]);
		}

        private void RegisterModelUpdateHandlers(ConnectionTreeModel newModel)
        {
            _puttySessionsManager.PuttySessionsCollectionChanged += OnPuttySessionsCollectionChanged;
            newModel.CollectionChanged += HandleCollectionChanged;
            newModel.PropertyChanged += HandleCollectionPropertyChanged;
        }

        private void UnregisterModelUpdateHandlers(ConnectionTreeModel oldConnectionTreeModel)
        {
            _puttySessionsManager.PuttySessionsCollectionChanged -= OnPuttySessionsCollectionChanged;

            if (oldConnectionTreeModel == null)
                return;

            oldConnectionTreeModel.CollectionChanged -= HandleCollectionChanged;
            oldConnectionTreeModel.PropertyChanged -= HandleCollectionPropertyChanged;
        }

        private void OnPuttySessionsCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            RefreshObjects(GetRootPuttyNodes().ToList());
        }

        private void HandleCollectionPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            // for some reason property changed events are getting triggered twice for each changed property. should be just once. cant find source of duplication
            // Removed "TO DO" from above comment. Per #142 it apperas that this no longer occurs with ObjectListView 2.9.1
            var property = propertyChangedEventArgs.PropertyName;
            if (property != nameof(ConnectionInfo.Name)
                && property != nameof(ConnectionInfo.OpenConnections)
                && property != nameof(ConnectionInfo.Icon))
            {
                return;
            }

            var senderAsConnectionInfo = sender as ConnectionInfo;
            if (senderAsConnectionInfo == null)
                return;

            RefreshObject(senderAsConnectionInfo);
			AutoResizeColumn(Columns[0]);
		}

        private void ExecutePostSetupActions()
        {
            foreach (var action in PostSetupActions)
            {
                action.Execute(this);
            }
        }
        #endregion

        #region ConnectionTree Behavior
        public RootNodeInfo GetRootConnectionNode()
        {
            return (RootNodeInfo)ConnectionTreeModel.RootNodes.First(item => item is RootNodeInfo);
        }

        public void Invoke(Action action)
        {
            Invoke((Delegate)action);
        }

        public void InvokeExpand(object model)
        {
            Invoke(() => Expand(model));
        }

        public void InvokeRebuildAll(bool preserveState)
        {
            Invoke(() => RebuildAll(preserveState));
        }

        public IEnumerable<RootPuttySessionsNodeInfo> GetRootPuttyNodes()
        {
            return Objects.OfType<RootPuttySessionsNodeInfo>();
        }

        public void AddConnection()
        {
            try
            {
                AddNode(new ConnectionInfo());
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("UI.Window.Tree.AddConnection() failed.", ex);
            }
        }

        public void AddFolder()
        {
            try
            {
                AddNode(new ContainerInfo());
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.strErrorAddFolderFailed, ex);
            }
        }

        private void AddNode(ConnectionInfo newNode)
        {
            if (SelectedNode?.GetTreeNodeType() == TreeNodeType.PuttyRoot || SelectedNode?.GetTreeNodeType() == TreeNodeType.PuttySession)
                return;

            // the new node will survive filtering if filtering is active
            _connectionTreeSearchTextFilter.SpecialInclusionList.Add(newNode);

            // use root node if no node is selected
            ConnectionInfo parentNode = SelectedNode ?? GetRootConnectionNode();
            DefaultConnectionInfo.Instance.SaveTo(newNode);
            DefaultConnectionInheritance.Instance.SaveTo(newNode.Inheritance);
            var selectedContainer = parentNode as ContainerInfo;
            var parent = selectedContainer ?? parentNode?.Parent;
            newNode.SetParent(parent);
            Expand(parent);
            SelectObject(newNode, true);
            EnsureModelVisible(newNode);
            _allowEdit = true;
            SelectedItem.BeginEdit();
        }

        public void DuplicateSelectedNode()
        {
            if (SelectedNode == null)
                return;

            var selectedNodeType = SelectedNode.GetTreeNodeType();
            if (selectedNodeType != TreeNodeType.Connection && selectedNodeType != TreeNodeType.Container)
                return;

            var newNode = SelectedNode.Clone();
            SelectedNode.Parent.AddChildBelow(newNode, SelectedNode);
            newNode.Parent.SetChildBelow(newNode, SelectedNode);
        }

        public void RenameSelectedNode()
        {
            if (SelectedItem != null)
            {
                _allowEdit = true;
                SelectedItem.BeginEdit();
            }
        }

        public void DeleteSelectedNode()
        {
            if (SelectedNode is RootNodeInfo || SelectedNode is PuttySessionInfo) return;
            if (!NodeDeletionConfirmer.Confirm(SelectedNode)) return;
            ConnectionTreeModel.DeleteNode(SelectedNode);
        }

        public void SortRecursive(ConnectionInfo sortTarget, ListSortDirection sortDirection)
        {
            if (sortTarget == null)
                sortTarget = GetRootConnectionNode();

            Runtime.ConnectionsService.BeginBatchingSaves();

            var sortTargetAsContainer = sortTarget as ContainerInfo;
            if (sortTargetAsContainer != null)
                sortTargetAsContainer.SortRecursive(sortDirection);
            else
                SelectedNode.Parent.SortRecursive(sortDirection);

            Runtime.ConnectionsService.EndBatchingSaves();
        }

        /// <summary>
        /// Expands all tree objects and recalculates the
        /// column widths.
        /// </summary>
        public override void ExpandAll()
        {
            base.ExpandAll();
            AutoResizeColumn(Columns[0]);
        }

        /// <summary>
        /// Filters tree items based on the given <see cref="filterText"/>
        /// </summary>
        /// <param name="filterText">The text to filter by</param>
        public void ApplyFilter(string filterText)
        {
            _searchFilterActive = true;
            UseFiltering = true;
            _connectionTreeSearchTextFilter.FilterText = filterText;
            ModelFilter = AccessFilteringRequired
                ? new CompositeAllModelFilter(_accessFilter, _connectionTreeSearchTextFilter)
                : (IModelFilter)_connectionTreeSearchTextFilter;
        }

        /// <summary>
        /// Removes all item filtering from the connection tree
        /// (except the hiding of nodes the logged in user is not allowed to access)
        /// </summary>
        public void RemoveFilter()
        {
            _searchFilterActive = false;
            if (AccessFilteringRequired)
            {
                UseFiltering = true;
                ModelFilter = _accessFilter;
                return;
            }
            UseFiltering = false;
            ResetColumnFiltering();
        }

        /// <summary>
        /// True when a user without administrator rights is logged in:
        /// nodes restricted to groups they don't belong to must be hidden.
        /// </summary>
        private static bool AccessFilteringRequired => MultiUserService.IsActive && !MultiUserService.IsAdmin;

        /// <summary>
        /// Re-applies the user/group access filter, keeping the current search filter (if any).
        /// </summary>
        public void RefreshAccessFiltering()
        {
            if (!MultiUserService.IsActive) return;
            if (_searchFilterActive)
                ApplyFilter(_connectionTreeSearchTextFilter.FilterText);
            else
                RemoveFilter();
        }

        private void OnPermissionsChanged(object sender, EventArgs e)
        {
            SafeBeginInvoke(() =>
            {
                RefreshAccessFiltering();
                RefreshVisibleItems();
            });
        }

        private void OnActiveSessionsChanged(object sender, EventArgs e)
        {
            SafeBeginInvoke(RefreshVisibleItems);
        }

        private void SafeBeginInvoke(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // handle destroyed while closing
            }
        }

        /// <summary>
        /// Redraws the visible rows so that the icons / colors reflect who is connected where.
        /// </summary>
        private void RefreshVisibleItems()
        {
            if (IsDisposed || ConnectionTreeModel == null) return;
            try
            {
                var visible = new List<object>();
                var count = GetItemCount();
                for (var i = 0; i < count; i++)
                {
                    var rowObject = GetItem(i)?.RowObject;
                    if (rowObject != null) visible.Add(rowObject);
                }
                if (visible.Count > 0)
                    RefreshObjects(visible);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("ConnectionTree.RefreshVisibleItems failed", ex, MessageClass.WarningMsg);
            }
        }

        private static readonly Color InUseByOthersColor = Color.OrangeRed;

        private void OnFormatRow(object sender, FormatRowEventArgs e)
        {
            var connectionInfo = e.Model as ConnectionInfo;
            if (connectionInfo == null || connectionInfo is ContainerInfo) return;
            if (!ActiveSessionTracker.Instance.IsInUseByOthers(connectionInfo)) return;
            e.Item.ForeColor = InUseByOthersColor;
        }

        private void HandleCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
			// disable filtering if necessary. prevents RefreshObjects from
			// throwing an exception
			var filteringEnabled = IsFiltering;
			var filter = ModelFilter;
			if (filteringEnabled)
			{
				ResetColumnFiltering();
			}

			RefreshObject(sender);
			AutoResizeColumn(Columns[0]);

			// turn filtering back on
			if (filteringEnabled)
			{
				ModelFilter = filter;
				UpdateFiltering();
			}
		}

        protected override void UpdateFiltering()
        {
            base.UpdateFiltering();
            AutoResizeColumn(Columns[0]);
        }

        private void tvConnections_AfterSelect(object sender, EventArgs e)
        {
            try
            {
                Windows.ConfigForm.SelectedTreeNode = SelectedNode;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("tvConnections_AfterSelect (UI.Window.ConnectionTreeWindow) failed", ex);
            }
        }

        private void OnMouse_DoubleClick(object sender, MouseEventArgs mouseEventArgs)
        {
            if (mouseEventArgs.Clicks < 2) return;
            OLVColumn column;
            var listItem = GetItemAt(mouseEventArgs.X, mouseEventArgs.Y, out column);
	        var clickedNode = listItem?.RowObject as ConnectionInfo;
            if (clickedNode == null) return;
            DoubleClickHandler.Execute(clickedNode);
        }

        private void OnMouse_SingleClick(object sender, MouseEventArgs mouseEventArgs)
        {
            if (mouseEventArgs.Clicks > 1) return;
            OLVColumn column;
            var listItem = GetItemAt(mouseEventArgs.X, mouseEventArgs.Y, out column);
            var clickedNode = listItem?.RowObject as ConnectionInfo;
            if (clickedNode == null) return;
            SingleClickHandler.Execute(clickedNode);
        }

        private void tvConnections_CellToolTipShowing(object sender, ToolTipShowingEventArgs e)
        {
            try
            {
                var nodeProducingTooltip = (ConnectionInfo)e.Model;
                var lines = new List<string>();

                var inUse = ActiveSessionTracker.Instance.DescribeSessionsOfOthers(nodeProducingTooltip);
                if (!string.IsNullOrEmpty(inUse))
                    lines.Add(inUse);

                if (MultiUserService.IsAdmin && MultiUserService.IsRestricted(nodeProducingTooltip))
                    lines.Add(string.Format(MultiUserText.RestrictedTo, MultiUserService.GetGroupNames(MultiUserService.GetAssignedGroups(nodeProducingTooltip))));

                if (Settings.Default.ShowDescriptionTooltipsInTree && !string.IsNullOrEmpty(nodeProducingTooltip.Description))
                    lines.Add(nodeProducingTooltip.Description);

                // setting text to null prevents the tooltip from being shown
                e.Text = lines.Count > 0 ? string.Join(Environment.NewLine + Environment.NewLine, lines) : null;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("tvConnections_MouseMove (UI.Window.ConnectionTreeWindow) failed", ex);
            }
        }

        private void OnBeforeLabelEdit(object sender, LabelEditEventArgs e)
        {
            if (_nodeInEditMode || !(sender is ConnectionTree))
                return;

            if (!_allowEdit || SelectedNode is PuttySessionInfo || SelectedNode is RootPuttySessionsNodeInfo)
            {
                e.CancelEdit = true;
                return;
            }

            _nodeInEditMode = true;
            _contextMenu.DisableShortcutKeys();
        }

        private void OnAfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            if (!_nodeInEditMode)
                return;

            try
            {
                _contextMenu.EnableShortcutKeys();
                ConnectionTreeModel.RenameNode(SelectedNode, e.Label);
                _nodeInEditMode = false;
                _allowEdit = false;
                // ensures that if we are filtering and a new item is added that doesn't match the filter, it will be filtered out
                _connectionTreeSearchTextFilter.SpecialInclusionList.Clear();
                UpdateFiltering();
                Windows.ConfigForm.SelectedTreeNode = SelectedNode;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("tvConnections_AfterLabelEdit (UI.Window.ConnectionTreeWindow) failed", ex);
            }
        }
        #endregion
    }
}
