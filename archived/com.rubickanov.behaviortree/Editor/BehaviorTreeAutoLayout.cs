using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Editor;

public static class BehaviorTreeAutoLayout
{
    private const float NodeWidth = 220f;
    private const float NodeHeight = 100f;
    private const float HorizontalSpacing = 30f;
    private const float VerticalSpacing = 100f;

    private const float OrphanGap = 60f;

    public static void Layout(BehaviorTreeSerializer serializer)
    {
        var rootGuid = serializer.GetRootGuid();
        var childMap = BuildChildMap(serializer);
        var positions = new Dictionary<string, Vector2>();
        float nextX = 0f;

        if (rootGuid != null)
            LayoutRecursive(rootGuid, 0, childMap, positions, ref nextX);

        // Layout orphan nodes below the main tree
        var allNodes = serializer.GetAllNodes();
        var orphanRoots = allNodes
            .Where(n => n.IsOrphan)
            .Select(n => n.Guid)
            .ToList();

        if (orphanRoots.Count > 0)
        {
            float maxY = positions.Count > 0
                ? positions.Values.Max(p => p.y)
                : 0f;
            float orphanY = maxY + NodeHeight + OrphanGap;
            float orphanX = 0f;

            foreach (var orphanGuid in orphanRoots)
            {
                if (childMap.ContainsKey(orphanGuid))
                {
                    LayoutRecursive(orphanGuid, 0, childMap, positions, ref orphanX);
                    OffsetSubtree(orphanGuid, childMap, positions, new Vector2(0, orphanY));
                }
                else
                {
                    positions[orphanGuid] = new Vector2(orphanX, orphanY);
                    orphanX += NodeWidth + HorizontalSpacing;
                }
            }
        }

        serializer.SetPositionBatch(positions);
    }

    private static void OffsetSubtree(
        string guid,
        Dictionary<string, List<string>> childMap,
        Dictionary<string, Vector2> positions,
        Vector2 baseOffset)
    {
        if (positions.TryGetValue(guid, out var pos))
            positions[guid] = new Vector2(pos.x, pos.y + baseOffset.y);

        if (childMap.TryGetValue(guid, out var children))
        {
            foreach (var childGuid in children)
                OffsetSubtree(childGuid, childMap, positions, baseOffset);
        }
    }

    private static Dictionary<string, List<string>> BuildChildMap(BehaviorTreeSerializer serializer)
    {
        var map = new Dictionary<string, List<string>>();
        var pairs = serializer.GetParentChildPairs();
        foreach (var (parentGuid, childGuid) in pairs)
        {
            if (!map.ContainsKey(parentGuid))
                map[parentGuid] = new List<string>();
            map[parentGuid].Add(childGuid);
        }
        return map;
    }

    private static float LayoutRecursive(
        string guid,
        int depth,
        Dictionary<string, List<string>> childMap,
        Dictionary<string, Vector2> positions,
        ref float nextX)
    {
        float y = depth * (NodeHeight + VerticalSpacing);

        if (!childMap.TryGetValue(guid, out var children) || children.Count == 0)
        {
            float x = nextX;
            nextX += NodeWidth + HorizontalSpacing;
            positions[guid] = new Vector2(x, y);
            return x;
        }

        float firstChildX = float.MaxValue;
        float lastChildX = float.MinValue;

        foreach (var childGuid in children)
        {
            float childX = LayoutRecursive(childGuid, depth + 1, childMap, positions, ref nextX);
            if (childX < firstChildX) firstChildX = childX;
            if (childX > lastChildX) lastChildX = childX;
        }

        float centerX = (firstChildX + lastChildX) / 2f;
        positions[guid] = new Vector2(centerX, y);
        return centerX;
    }
}
