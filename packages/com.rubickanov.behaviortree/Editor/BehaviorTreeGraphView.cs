using System;
using System.Collections.Generic;
using System.Linq;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.BehaviorTree.Editor;

public class BehaviorTreeGraphView : GraphView
{
    public Action<BehaviorTreeNodeView?>? OnNodeSelected;
    public Action? OnGraphModified;

    private BehaviorTreeSerializer? _serializer;
    private BehaviorTreeSearchWindow? _searchWindow;
    private MiniMap _miniMap = default!;

    private struct ClipboardNode
    {
        public BTNode Node;
        public string OriginalGuid;
        public Vector2 Position;
    }

    private List<ClipboardNode>? _clipboard;
    private List<(string parentOrigGuid, string childOrigGuid)>? _clipboardEdges;
    private int _pasteCount;

    public BehaviorTreeGraphView()
    {
        Insert(0, new GridBackground());

        this.AddManipulator(new ContentZoomer());
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
            FindUssPath());
        if (styleSheet != null)
            styleSheets.Add(styleSheet);

        _miniMap = new MiniMap { anchored = true };
        _miniMap.SetPosition(new Rect(15, 30, 200, 140));
        _miniMap.visible = false;
        Add(_miniMap);

        graphViewChanged += OnGraphViewChanged;

        serializeGraphElements = SerializeElements;
        canPasteSerializedData = CanPaste;
        unserializeAndPaste = PasteElements;
    }

    private const string UssGuid = "b976e4244841b5158bbf3cee84cb591e";

    private static string FindUssPath()
    {
        var path = AssetDatabase.GUIDToAssetPath(UssGuid);
        return string.IsNullOrEmpty(path) ? "" : path;
    }

    public void PopulateView(BehaviorTreeSerializer serializer)
    {
        // Preserve transform across full rebuilds
        var savedPosition = viewTransform.position;
        var savedScale = viewTransform.scale;

        _serializer = serializer;

        // Clear existing
        graphViewChanged -= OnGraphViewChanged;
        DeleteElements(graphElements.ToList());
        graphViewChanged += OnGraphViewChanged;

        // Auto-layout if needed
        if (serializer.NeedsAutoLayout())
        {
            BehaviorTreeAutoLayout.Layout(serializer);
        }

        var rootGuid = serializer.GetRootGuid();
        var allNodes = serializer.GetAllNodes();

        // Create node views
        foreach (var nodeInfo in allNodes)
        {
            var nodeView = new BehaviorTreeNodeView(
                nodeInfo, serializer, this, nodeInfo.Guid == rootGuid);
            AddElement(nodeView);
        }

        // Create edges
        var pairs = serializer.GetParentChildPairs();
        foreach (var (parentGuid, childGuid) in pairs)
        {
            AddEdgeView(parentGuid, childGuid);
        }

        UpdatePortConnectedStates();

        // Restore transform
        UpdateViewTransform(savedPosition, savedScale);

        // Setup search window
        if (_searchWindow == null)
        {
            _searchWindow = ScriptableObject.CreateInstance<BehaviorTreeSearchWindow>();
        }
        _searchWindow.Initialize(this, serializer);

        nodeCreationRequest = ctx =>
        {
            SearchWindow.Open(new SearchWindowContext(ctx.screenMousePosition), _searchWindow);
        };
    }

    private BehaviorTreeNodeView? AddNodeViewForGuid(string guid)
    {
        if (_serializer == null) return null;

        var rootGuid = _serializer.GetRootGuid();
        var allNodes = _serializer.GetAllNodes();
        foreach (var nodeInfo in allNodes)
        {
            if (nodeInfo.Guid != guid) continue;
            var nodeView = new BehaviorTreeNodeView(
                nodeInfo, _serializer, this, nodeInfo.Guid == rootGuid);
            AddElement(nodeView);
            return nodeView;
        }
        return null;
    }

    private Edge? AddEdgeView(string parentGuid, string childGuid)
    {
        var parentView = FindNodeView(parentGuid);
        var childView = FindNodeView(childGuid);
        if (parentView?.OutputPort == null || childView?.InputPort == null) return null;

        var edge = parentView.OutputPort.ConnectTo(childView.InputPort);
        AddElement(edge);
        return edge;
    }

    public BehaviorTreeNodeView? FindNodeView(string guid)
    {
        return GetNodeByGuid(guid) as BehaviorTreeNodeView;
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        return ports.Where(endPort =>
        {
            if (endPort.direction == startPort.direction) return false;
            if (endPort.node == startPort.node) return false;

            // Cycle detection at port level
            if (_serializer != null)
            {
                var startNode = startPort.node as BehaviorTreeNodeView;
                var endNode = endPort.node as BehaviorTreeNodeView;
                if (startNode != null && endNode != null)
                {
                    var parentGuid = startPort.direction == Direction.Output ? startNode.Guid : endNode.Guid;
                    var childGuid = startPort.direction == Direction.Output ? endNode.Guid : startNode.Guid;
                    if (_serializer.WouldCreateCycle(parentGuid, childGuid))
                        return false;
                }
            }

            return true;
        }).ToList();
    }

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (_serializer == null) return change;

        // Handle removed elements — two-pass: edges first, then nodes
        if (change.elementsToRemove != null)
        {
            var nodesToDelete = new HashSet<string>();

            // Collect dangling edges before processing anything
            var danglingEdges = new List<Edge>();
            foreach (var element in change.elementsToRemove)
            {
                if (element is not BehaviorTreeNodeView nodeView) continue;
                nodesToDelete.Add(nodeView.Guid);
                if (nodeView.InputPort != null)
                    foreach (var e in nodeView.InputPort.connections)
                        if (!change.elementsToRemove.Contains(e))
                            danglingEdges.Add(e);
                if (nodeView.OutputPort != null)
                    foreach (var e in nodeView.OutputPort.connections)
                        if (!change.elementsToRemove.Contains(e))
                            danglingEdges.Add(e);
            }

            // Pass 1: process explicit edge removals (skip edges whose parent node is also being deleted)
            foreach (var element in change.elementsToRemove)
            {
                if (element is not Edge edge) continue;
                var parentView = edge.output?.node as BehaviorTreeNodeView;
                var childView = edge.input?.node as BehaviorTreeNodeView;
                if (parentView == null || childView == null) continue;

                // Skip if parent node is also being deleted — DeleteNode handles cleanup
                if (nodesToDelete.Contains(parentView.Guid)) continue;

                _serializer.RemoveChild(parentView.Guid, childView.Guid);
                childView.AddToClassList("bt-node-orphan");
            }

            // Pass 2: process node deletions (handles own children internally)
            foreach (var element in change.elementsToRemove)
            {
                if (element is BehaviorTreeNodeView nodeView)
                {
                    _serializer.DeleteNode(nodeView.Guid);
                    OnNodeSelected?.Invoke(null);
                }
            }

            // Clean up dangling edges visually — data already handled by DeleteNode
            if (danglingEdges.Count > 0)
            {
                foreach (var edge in danglingEdges)
                {
                    edge.input?.Disconnect(edge);
                    edge.output?.Disconnect(edge);
                }
                graphViewChanged -= OnGraphViewChanged;
                DeleteElements(danglingEdges);
                graphViewChanged += OnGraphViewChanged;
            }
        }

        // Handle created edges — with cycle detection
        if (change.edgesToCreate != null)
        {
            for (int i = change.edgesToCreate.Count - 1; i >= 0; i--)
            {
                var edge = change.edgesToCreate[i];
                var parentView = edge.output?.node as BehaviorTreeNodeView;
                var childView = edge.input?.node as BehaviorTreeNodeView;
                if (parentView == null || childView == null) continue;

                if (_serializer.WouldCreateCycle(parentView.Guid, childView.Guid))
                {
                    change.edgesToCreate.RemoveAt(i);
                    continue;
                }

                _serializer.AddChild(parentView.Guid, childView.Guid);
                childView.RemoveFromClassList("bt-node-orphan");
            }
        }

        // Handle moved elements — flush positions, sort children by X
        if (change.movedElements != null)
        {
            _serializer.FlushPositions();

            var affectedParents = new HashSet<string>();
            var pairs = _serializer.GetParentChildPairs();
            var childToParent = new Dictionary<string, string>(pairs.Count);
            foreach (var (parentGuid, childGuid) in pairs)
                childToParent[childGuid] = parentGuid;
            foreach (var element in change.movedElements)
            {
                if (element is BehaviorTreeNodeView nodeView &&
                    childToParent.TryGetValue(nodeView.Guid, out var parentGuid))
                {
                    affectedParents.Add(parentGuid);
                }
            }

            foreach (var parentGuid in affectedParents)
                _serializer.SortChildren(parentGuid);
        }

        UpdatePortConnectedStates();
        OnGraphModified?.Invoke();

        return change;
    }

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        if (_serializer == null) return;

        var mousePos = viewTransform.matrix.inverse.MultiplyPoint(evt.localMousePosition);
        evt.menu.AppendAction("Create Node", _ =>
        {
            var screenPos = GUIUtility.GUIToScreenPoint(
                this.LocalToWorld(evt.localMousePosition));
            OpenSearchWindow(screenPos, null);
        });

        evt.menu.AppendSeparator();
        base.BuildContextualMenu(evt);
    }

    public void OpenSearchWindow(Vector2 screenPosition, Port? fromPort)
    {
        if (_searchWindow == null || _serializer == null) return;

        _searchWindow.FromPort = fromPort;
        _searchWindow.CreationPosition = viewTransform.matrix.inverse.MultiplyPoint(
            this.WorldToLocal(GUIUtility.ScreenToGUIPoint(screenPosition)));

        SearchWindow.Open(new SearchWindowContext(screenPosition), _searchWindow);
    }

    public void CreateNodeFromSearch(Type type, Vector2 position, Port? fromPort)
    {
        if (_serializer == null) return;

        var guid = _serializer.CreateNode(type, position);
        if (guid == null) return;

        // Auto-connect if dragged from a port
        if (fromPort != null)
        {
            var fromNode = fromPort.node as BehaviorTreeNodeView;
            if (fromNode != null)
            {
                if (fromPort.direction == Direction.Output)
                    _serializer.AddChild(fromNode.Guid, guid);
                else
                    _serializer.AddChild(guid, fromNode.Guid);
            }
        }

        // Incremental add instead of full rebuild
        var newNodeView = AddNodeViewForGuid(guid);

        // Add edge view if auto-connected
        if (fromPort != null && newNodeView != null)
        {
            var fromNode = fromPort.node as BehaviorTreeNodeView;
            if (fromNode != null)
            {
                if (fromPort.direction == Direction.Output)
                    AddEdgeView(fromNode.Guid, guid);
                else
                    AddEdgeView(guid, fromNode.Guid);
            }
        }

        UpdatePortConnectedStates();

        // Select the new node
        if (newNodeView != null)
        {
            ClearSelection();
            AddToSelection(newNodeView);
        }
    }

    public void OnEdgeCreatedByDrop(Edge edge)
    {
        if (_serializer == null) return;

        var parentView = edge.output?.node as BehaviorTreeNodeView;
        var childView = edge.input?.node as BehaviorTreeNodeView;
        if (parentView != null && childView != null)
        {
            if (_serializer.WouldCreateCycle(parentView.Guid, childView.Guid))
            {
                // Reject — remove edge visually
                edge.input?.Disconnect(edge);
                edge.output?.Disconnect(edge);
                RemoveElement(edge);
                return;
            }

            _serializer.AddChild(parentView.Guid, childView.Guid);
            childView.RemoveFromClassList("bt-node-orphan");
        }

        // port.connected is not yet updated at this point — defer to next frame
        schedule.Execute(() => UpdatePortConnectedStates());
        OnGraphModified?.Invoke();
    }

    /// <summary>
    /// Removes edges visually without triggering OnGraphViewChanged, and serializes the removal.
    /// Used by <see cref="BehaviorTreeNodePort"/> to handle old edge replacement on drop.
    /// </summary>
    public void RemoveEdgesForDrop(List<Edge> edgesToRemove)
    {
        if (_serializer == null || edgesToRemove.Count == 0) return;

        // Serialize removals
        foreach (var edge in edgesToRemove)
        {
            var parentView = edge.output?.node as BehaviorTreeNodeView;
            var childView = edge.input?.node as BehaviorTreeNodeView;
            if (parentView != null && childView != null)
            {
                _serializer.RemoveChild(parentView.Guid, childView.Guid);
                childView.AddToClassList("bt-node-orphan");
            }
        }

        // Remove visually without triggering callback
        foreach (var edge in edgesToRemove)
        {
            edge.input?.Disconnect(edge);
            edge.output?.Disconnect(edge);
        }
        graphViewChanged -= OnGraphViewChanged;
        DeleteElements(edgesToRemove);
        graphViewChanged += OnGraphViewChanged;
    }

    public void ToggleMiniMap()
    {
        _miniMap.visible = !_miniMap.visible;
    }

    private void UpdatePortConnectedStates()
    {
        foreach (var element in graphElements)
        {
            if (element is BehaviorTreeNodeView nodeView)
            {
                nodeView.UpdatePortConnectedState();
                if (_serializer != null)
                    nodeView.ValidateNode(_serializer);
            }
        }
    }

    private string SerializeElements(IEnumerable<GraphElement> elements)
    {
        if (_serializer == null) return "";

        var selectedViews = elements.OfType<BehaviorTreeNodeView>().ToList();
        if (selectedViews.Count == 0) return "";

        var selectedGuids = new HashSet<string>(selectedViews.Select(v => v.Guid));

        _clipboard = new List<ClipboardNode>();
        _pasteCount = 0;
        foreach (var view in selectedViews)
        {
            var nodeProp = _serializer.FindNodeProperty(view.Guid);
            if (nodeProp?.managedReferenceValue is not BTNode node) continue;

            var clone = ShallowClone(node);

            _clipboard.Add(new ClipboardNode
            {
                Node = clone,
                OriginalGuid = view.Guid,
                Position = node.Position
            });
        }

        // Collect internal edges (both endpoints selected)
        _clipboardEdges = new List<(string, string)>();
        var allPairs = _serializer.GetParentChildPairs();
        foreach (var (parentGuid, childGuid) in allPairs)
        {
            if (selectedGuids.Contains(parentGuid) && selectedGuids.Contains(childGuid))
                _clipboardEdges.Add((parentGuid, childGuid));
        }

        return "bt-clipboard";
    }

    private bool CanPaste(string data)
    {
        return data == "bt-clipboard" && _clipboard is { Count: > 0 };
    }

    private void PasteElements(string operationName, string data)
    {
        if (_clipboard == null || _clipboard.Count == 0 || _serializer == null) return;

        _pasteCount++;
        var offset = new Vector2(30, 30) * _pasteCount;
        var guidMap = new Dictionary<string, string>();
        var newGuids = new List<string>();

        foreach (var entry in _clipboard)
        {
            var node = ShallowClone(entry.Node);
            var newGuid = _serializer.CreateNodeFromInstance(node, entry.Position + offset);
            if (newGuid == null) continue;

            guidMap[entry.OriginalGuid] = newGuid;
            newGuids.Add(newGuid);
        }

        // Rebuild internal edges
        var edgePairs = new List<(string parent, string child)>();
        if (_clipboardEdges != null)
        {
            foreach (var (parentOrig, childOrig) in _clipboardEdges)
            {
                if (guidMap.TryGetValue(parentOrig, out var newParent) &&
                    guidMap.TryGetValue(childOrig, out var newChild))
                {
                    _serializer.AddChild(newParent, newChild);
                    edgePairs.Add((newParent, newChild));
                }
            }
        }

        // Incremental add instead of full rebuild
        foreach (var guid in newGuids)
            AddNodeViewForGuid(guid);

        foreach (var (parent, child) in edgePairs)
            AddEdgeView(parent, child);

        UpdatePortConnectedStates();

        ClearSelection();
        foreach (var guid in newGuids)
        {
            var nodeView = FindNodeView(guid);
            if (nodeView != null)
                AddToSelection(nodeView);
        }
    }

    private static BTNode ShallowClone(BTNode node) => node.ShallowClone();

    private readonly Dictionary<string, BTStatus> _runtimeStateMap = new();

    public void UpdateRuntimeState(BTNode? runtimeRoot)
    {
        if (runtimeRoot == null) return;

        _runtimeStateMap.Clear();
        CollectRuntimeStates(runtimeRoot, _runtimeStateMap);
        var stateMap = _runtimeStateMap;

        foreach (var element in graphElements)
        {
            if (element is BehaviorTreeNodeView nodeView)
            {
                if (stateMap.TryGetValue(nodeView.Guid, out var status))
                    nodeView.UpdateState(status);
                else
                    nodeView.ClearState();
            }
        }
    }

    private static void CollectRuntimeStates(BTNode node, Dictionary<string, BTStatus> map)
    {
        if (string.IsNullOrEmpty(node.Guid)) return;
        map[node.Guid] = node.LastStatus;

        if (node is BTComposite composite)
        {
            foreach (var child in composite.GetChildren())
                CollectRuntimeStates(child, map);
        }
        else if (node is BTDecorator decorator)
        {
            var child = decorator.GetChild();
            if (child != null)
                CollectRuntimeStates(child, map);
        }
    }
}
