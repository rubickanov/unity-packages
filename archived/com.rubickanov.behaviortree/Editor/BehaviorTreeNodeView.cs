using Rubickanov.BehaviorTree.Runtime;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubickanov.BehaviorTree.Editor;

public class BehaviorTreeNodeView : UnityEditor.Experimental.GraphView.Node
{
    public string Guid { get; }
    public Port? InputPort { get; private set; }
    public Port? OutputPort { get; private set; }

    private readonly BehaviorTreeSerializer _serializer;
    private readonly BehaviorTreeGraphView _graphView;
    private Label? _descriptionLabel;

    public BehaviorTreeNodeView(
        BehaviorTreeSerializer.NodeInfo info,
        BehaviorTreeSerializer serializer,
        BehaviorTreeGraphView graphView,
        bool isRoot) : base()
    {
        Guid = info.Guid;
        _serializer = serializer;
        _graphView = graphView;
        viewDataKey = info.Guid;

        title = info.DisplayName;

        // USS class by category
        AddToClassList("bt-node");
        switch (info.NodeCategory)
        {
            case NodeCategory.Composite:
                AddToClassList("bt-node-composite");
                break;
            case NodeCategory.Decorator:
                AddToClassList("bt-node-decorator");
                break;
            case NodeCategory.Action:
                AddToClassList("bt-node-action");
                break;
            case NodeCategory.Condition:
                AddToClassList("bt-node-condition");
                break;
            case NodeCategory.Subtree:
                AddToClassList("bt-node-subtree");
                break;
        }

        if (info.IsOrphan)
            AddToClassList("bt-node-orphan");

        if (isRoot)
            AddToClassList("bt-node-root");

        // Description label
        if (!string.IsNullOrEmpty(info.Description))
        {
            _descriptionLabel = new Label(info.Description);
            _descriptionLabel.AddToClassList("node-description");
            mainContainer.Insert(1, _descriptionLabel);
        }

        // Collapse default #top — we don't use inputContainer/outputContainer,
        // their built-in styles fight back. Own containers are simpler.
        var top = this.Q("contents")?.Q("top");
        if (top != null) top.style.display = DisplayStyle.None;

        // Input port — own container, top of node
        InputPort = BehaviorTreeNodePort.Create(Direction.Input, Port.Capacity.Single, graphView);
        var inPortArea = new VisualElement { name = "port-input" };
        inPortArea.AddToClassList("bt-port-area");
        inPortArea.Add(InputPort);
        mainContainer.Insert(0, inPortArea);

        // Output port — own container, bottom of node
        Port.Capacity outCapacity;
        bool hasOutput = true;
        switch (info.NodeCategory)
        {
            case NodeCategory.Composite: outCapacity = Port.Capacity.Multi; break;
            case NodeCategory.Decorator: outCapacity = Port.Capacity.Single; break;
            default: hasOutput = false; outCapacity = default; break;
        }

        if (hasOutput)
        {
            OutputPort = BehaviorTreeNodePort.Create(Direction.Output, outCapacity, graphView);
            var outPortArea = new VisualElement { name = "port-output" };
            outPortArea.AddToClassList("bt-port-area");
            outPortArea.Add(OutputPort);
            mainContainer.Add(outPortArea);
        }

        RefreshExpandedState();
        RefreshPorts();

        SetPosition(new Rect(info.Position, Vector2.zero));
    }

    public override void SetPosition(Rect newPos)
    {
        base.SetPosition(newPos);
        _serializer.SetPosition(Guid, new Vector2(newPos.x, newPos.y));
    }

    public override void OnSelected()
    {
        base.OnSelected();
        _graphView.OnNodeSelected?.Invoke(this);
    }

    public override void OnUnselected()
    {
        base.OnUnselected();
        _graphView.schedule.Execute(() =>
        {
            if (_graphView.selection.Count == 0)
                _graphView.OnNodeSelected?.Invoke(null);
        });
    }

    public void UpdateState(BTStatus status)
    {
        RemoveFromClassList("bt-state-running");
        RemoveFromClassList("bt-state-success");
        RemoveFromClassList("bt-state-failure");

        switch (status)
        {
            case BTStatus.Running:
                AddToClassList("bt-state-running");
                break;
            case BTStatus.Success:
                AddToClassList("bt-state-success");
                break;
            case BTStatus.Failure:
                AddToClassList("bt-state-failure");
                break;
        }
    }

    public void ClearState()
    {
        RemoveFromClassList("bt-state-running");
        RemoveFromClassList("bt-state-success");
        RemoveFromClassList("bt-state-failure");
    }

    public void UpdateTitle(string displayName, string description)
    {
        title = displayName;
        if (_descriptionLabel != null)
            _descriptionLabel.text = description;
    }

    public void UpdatePortConnectedState()
    {
        SetPortConnected(InputPort);
        SetPortConnected(OutputPort);
    }

    public void ValidateNode(BehaviorTreeSerializer serializer)
    {
        RemoveFromClassList("bt-node-invalid");
        tooltip = "";

        var children = serializer.GetChildGuids(Guid);
        bool needsChildren = OutputPort != null;
        if (needsChildren && children.Count == 0)
        {
            AddToClassList("bt-node-invalid");
            tooltip = "Missing child connection";
            return;
        }

        // Check subtree for recursive reference
        var nodeProp = serializer.FindNodeProperty(Guid);
        if (nodeProp?.managedReferenceValue is BTSubtree subtree)
        {
            if (subtree.SubtreeAsset == null)
            {
                AddToClassList("bt-node-invalid");
                tooltip = "No subtree asset assigned";
            }
            else if (BehaviorTreeSerializer.HasSubtreeCycle(serializer.Asset, subtree.SubtreeAsset))
            {
                AddToClassList("bt-node-invalid");
                tooltip = "Recursive subtree reference detected";
            }
        }
    }

    private static void SetPortConnected(Port? port)
    {
        if (port == null) return;
        if (port.connected)
            port.AddToClassList("bt-port-connected");
        else
            port.RemoveFromClassList("bt-port-connected");
    }
}
