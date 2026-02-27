using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public class BehaviorTreeGraphView : GraphView
{
    public Action<BehaviorTreeNodeView?>? OnNodeSelected;

    private BehaviorTreeSerializer? _serializer;
    private BehaviorTreeSearchWindow? _searchWindow;
    private MiniMap _miniMap = default!;

    private struct ClipboardNode
    {
        public BTNode Node;
        public string OriginalGuid;
        public Vector2 Position;
    }

    private static List<ClipboardNode>? s_clipboard;
    private static List<(string parentOrigGuid, string childOrigGuid)>? s_clipboardEdges;
    private static int s_pasteCount;

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

    private static string FindUssPath()
    {
        var guids = AssetDatabase.FindAssets("BehaviorTreeEditorStyles t:StyleSheet");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("BehaviorTreeEditorStyles.uss"))
                return path;
        }
        return "Assets/Code/Framework/BehaviorTree/Editor/BehaviorTreeEditorStyles.uss";
    }

    public void PopulateView(BehaviorTreeSerializer serializer)
    {
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
            var parentView = FindNodeView(parentGuid);
            var childView = FindNodeView(childGuid);
            if (parentView?.OutputPort == null || childView?.InputPort == null) continue;

            var edge = parentView.OutputPort.ConnectTo(childView.InputPort);
            AddElement(edge);
        }

        UpdatePortConnectedStates();

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

    public BehaviorTreeNodeView? FindNodeView(string guid)
    {
        return GetNodeByGuid(guid) as BehaviorTreeNodeView;
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        return ports.Where(endPort =>
            endPort.direction != startPort.direction &&
            endPort.node != startPort.node).ToList();
    }

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (_serializer == null) return change;

        // Handle removed elements
        if (change.elementsToRemove != null)
        {
            // Collect edges connected to nodes being removed
            var danglingEdges = new List<Edge>();
            foreach (var element in change.elementsToRemove)
            {
                if (element is not BehaviorTreeNodeView nodeView) continue;
                if (nodeView.InputPort != null)
                    foreach (var e in nodeView.InputPort.connections)
                        if (!change.elementsToRemove.Contains(e))
                            danglingEdges.Add(e);
                if (nodeView.OutputPort != null)
                    foreach (var e in nodeView.OutputPort.connections)
                        if (!change.elementsToRemove.Contains(e))
                            danglingEdges.Add(e);
            }

            foreach (var element in change.elementsToRemove)
            {
                if (element is BehaviorTreeNodeView nodeView)
                {
                    _serializer.DeleteNode(nodeView.Guid);
                    OnNodeSelected?.Invoke(null);
                }
                else if (element is Edge edge)
                {
                    var parentView = edge.output?.node as BehaviorTreeNodeView;
                    var childView = edge.input?.node as BehaviorTreeNodeView;
                    if (parentView != null && childView != null)
                    {
                        _serializer.RemoveChild(parentView.Guid, childView.Guid);
                        childView.AddToClassList("bt-node-orphan");
                    }
                }
            }

            // Explicitly remove dangling edges — GraphView doesn't clean them up
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

        // Handle created edges
        if (change.edgesToCreate != null)
        {
            foreach (var edge in change.edgesToCreate)
            {
                var parentView = edge.output?.node as BehaviorTreeNodeView;
                var childView = edge.input?.node as BehaviorTreeNodeView;
                if (parentView != null && childView != null)
                {
                    _serializer.AddChild(parentView.Guid, childView.Guid);
                    childView.RemoveFromClassList("bt-node-orphan");
                }
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

        // Rebuild view
        PopulateView(_serializer);

        // Select the new node
        var newNodeView = FindNodeView(guid);
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
            _serializer.AddChild(parentView.Guid, childView.Guid);
            childView.RemoveFromClassList("bt-node-orphan");
        }

        // port.connected is not yet updated at this point — defer to next frame
        schedule.Execute(() => UpdatePortConnectedStates());
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

        s_clipboard = new List<ClipboardNode>();
        s_pasteCount = 0;
        foreach (var view in selectedViews)
        {
            var nodeProp = _serializer.FindNodeProperty(view.Guid);
            if (nodeProp?.managedReferenceValue is not BTNode node) continue;

            var clone = ShallowClone(node);

            s_clipboard.Add(new ClipboardNode
            {
                Node = clone,
                OriginalGuid = view.Guid,
                Position = node.Position
            });
        }

        // Collect internal edges (both endpoints selected)
        s_clipboardEdges = new List<(string, string)>();
        var allPairs = _serializer.GetParentChildPairs();
        foreach (var (parentGuid, childGuid) in allPairs)
        {
            if (selectedGuids.Contains(parentGuid) && selectedGuids.Contains(childGuid))
                s_clipboardEdges.Add((parentGuid, childGuid));
        }

        return "bt-clipboard";
    }

    private bool CanPaste(string data)
    {
        return data == "bt-clipboard" && s_clipboard is { Count: > 0 };
    }

    private void PasteElements(string operationName, string data)
    {
        if (s_clipboard == null || s_clipboard.Count == 0 || _serializer == null) return;

        s_pasteCount++;
        var offset = new Vector2(30, 30) * s_pasteCount;
        var guidMap = new Dictionary<string, string>();
        var newGuids = new List<string>();

        foreach (var entry in s_clipboard)
        {
            var node = ShallowClone(entry.Node);
            var newGuid = _serializer.CreateNodeFromInstance(node, entry.Position + offset);
            if (newGuid == null) continue;

            guidMap[entry.OriginalGuid] = newGuid;
            newGuids.Add(newGuid);
        }

        // Rebuild internal edges
        if (s_clipboardEdges != null)
        {
            foreach (var (parentOrig, childOrig) in s_clipboardEdges)
            {
                if (guidMap.TryGetValue(parentOrig, out var newParent) &&
                    guidMap.TryGetValue(childOrig, out var newChild))
                {
                    _serializer.AddChild(newParent, newChild);
                }
            }
        }

        PopulateView(_serializer);

        ClearSelection();
        foreach (var guid in newGuids)
        {
            var nodeView = FindNodeView(guid);
            if (nodeView != null)
                AddToSelection(nodeView);
        }
    }

    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static BTNode ShallowClone(BTNode node)
    {
        var clone = (BTNode)MemberwiseCloneMethod.Invoke(node, null)!;
        clone.Guid = Guid.NewGuid().ToString();

        // Strip children — edges are handled separately
        if (clone is BTComposite)
            CompositeChildrenField?.SetValue(clone, Array.Empty<BTNode>());
        else if (clone is BTDecorator)
            DecoratorChildField?.SetValue(clone, null);

        return clone;
    }

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

    private static readonly System.Reflection.FieldInfo? CompositeChildrenField =
        typeof(BTComposite).GetField("Children",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static readonly System.Reflection.FieldInfo? DecoratorChildField =
        typeof(BTDecorator).GetField("Child",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static void CollectRuntimeStates(BTNode node, Dictionary<string, BTStatus> map)
    {
        if (string.IsNullOrEmpty(node.Guid)) return;
        map[node.Guid] = node.LastStatus;

        if (node is BTComposite composite)
        {
            if (CompositeChildrenField?.GetValue(composite) is BTNode[] children)
            {
                foreach (var child in children)
                    CollectRuntimeStates(child, map);
            }
        }
        else if (node is BTDecorator decorator)
        {
            if (DecoratorChildField?.GetValue(decorator) is BTNode child)
                CollectRuntimeStates(child, map);
        }
    }
}
