using System;
using System.Collections.Generic;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public class BehaviorTreeNodePort : Port
{
    private class EdgeConnectorListener : IEdgeConnectorListener
    {
        private readonly BehaviorTreeGraphView _graphView;

        public EdgeConnectorListener(BehaviorTreeGraphView graphView)
        {
            _graphView = graphView;
        }

        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
            var screenPos = GUIUtility.GUIToScreenPoint(
                _graphView.LocalToWorld(position));
            var fromPort = edge.output ?? edge.input;
            _graphView.OpenSearchWindow(screenPos, fromPort);
        }

        public void OnDrop(GraphView graphView, Edge edge)
        {
            // Disconnect existing edges on single-capacity ports
            var edgesToDelete = new List<GraphElement>();

            if (edge.input?.capacity == Capacity.Single)
            {
                foreach (var conn in edge.input.connections)
                {
                    if (conn != edge)
                        edgesToDelete.Add(conn);
                }
            }

            if (edge.output?.capacity == Capacity.Single)
            {
                foreach (var conn in edge.output.connections)
                {
                    if (conn != edge)
                        edgesToDelete.Add(conn);
                }
            }

            if (edgesToDelete.Count > 0)
                graphView.DeleteElements(edgesToDelete);

            // Add the new edge visually
            graphView.AddElement(edge);

            // Serialize the connection (AddElement does NOT trigger graphViewChanged.edgesToCreate)
            _graphView.OnEdgeCreatedByDrop(edge);
        }
    }

    private BehaviorTreeNodePort(
        Orientation orientation,
        Direction direction,
        Capacity capacity,
        Type type) : base(orientation, direction, capacity, type)
    {
    }

    public static BehaviorTreeNodePort Create(
        Direction direction,
        Capacity capacity,
        BehaviorTreeGraphView graphView)
    {
        var listener = new EdgeConnectorListener(graphView);
        var port = new BehaviorTreeNodePort(Orientation.Vertical, direction, capacity, typeof(BTNode));
        port.m_EdgeConnector = new EdgeConnector<Edge>(listener);
        port.AddManipulator(port.m_EdgeConnector);
        port.portName = "";
        return port;
    }
}
