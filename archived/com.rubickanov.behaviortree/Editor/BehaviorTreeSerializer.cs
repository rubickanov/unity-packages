using System;
using System.Collections.Generic;
using System.Linq;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Editor;

public class BehaviorTreeSerializer
{
    public SerializedObject SerializedObject { get; }
    public SerializedProperty RootProperty { get; }
    public SerializedProperty OrphansProperty { get; }

    private readonly Dictionary<string, string> _guidToPropertyPath = new();
    private bool _cacheDirty = true;
    private List<NodeInfo>? _cachedNodes;
    private List<(string parentGuid, string childGuid)>? _cachedPairs;
    private Dictionary<string, string>? _cachedChildToParent;

    public BehaviorTreeSerializer(SerializedObject serializedObject)
    {
        SerializedObject = serializedObject;
        RootProperty = serializedObject.FindProperty("_root");
        OrphansProperty = serializedObject.FindProperty("_orphans");
        EnsureGuids();
        EnsureCaches();
    }

    private void InvalidateCaches()
    {
        _cacheDirty = true;
        _cachedNodes = null;
        _cachedPairs = null;
        _cachedChildToParent = null;
    }

    /// <summary>
    /// Assigns GUIDs to any node missing one and persists them. Kept separate from
    /// <see cref="EnsureCaches"/> so cache reads never mutate serialized data.
    /// </summary>
    private void EnsureGuids()
    {
        bool changed = false;
        var visited = new HashSet<object>(ReferenceComparer.Instance);

        if (RootProperty.managedReferenceValue != null)
            AssignGuidsRecursive(RootProperty, visited, ref changed);

        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue != null)
                AssignGuidsRecursive(orphan, visited, ref changed);
        }

        if (changed)
            SerializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignGuidsRecursive(SerializedProperty nodeProp, HashSet<object> visited, ref bool changed)
    {
        var node = nodeProp.managedReferenceValue;
        if (node == null || !visited.Add(node)) return;

        var guidProp = nodeProp.FindPropertyRelative("_guid");
        if (guidProp == null) return;

        if (string.IsNullOrEmpty(guidProp.stringValue))
        {
            guidProp.stringValue = Guid.NewGuid().ToString();
            changed = true;
        }

        var childrenProp = nodeProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                if (child.managedReferenceValue != null)
                    AssignGuidsRecursive(child, visited, ref changed);
            }
            return;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
            AssignGuidsRecursive(childProp, visited, ref changed);
    }

    private void EnsureCaches()
    {
        if (!_cacheDirty) return;
        _cacheDirty = false;

        _guidToPropertyPath.Clear();

        var visited = new HashSet<string>();
        if (RootProperty.managedReferenceValue != null)
            CacheNodeRecursive(RootProperty, visited);

        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue != null)
                CacheNodeRecursive(orphan, visited);
        }
    }

    public void RebuildGuidCache() => InvalidateCaches();

    private void CacheNodeRecursive(SerializedProperty nodeProp, HashSet<string> visited)
    {
        var guidProp = nodeProp.FindPropertyRelative("_guid");
        if (guidProp == null || string.IsNullOrEmpty(guidProp.stringValue)) return;
        if (!visited.Add(guidProp.stringValue)) return;

        _guidToPropertyPath[guidProp.stringValue] = nodeProp.propertyPath;

        var childrenProp = nodeProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                if (child.managedReferenceValue != null)
                    CacheNodeRecursive(child, visited);
            }
            return;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
            CacheNodeRecursive(childProp, visited);
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);
        int IEqualityComparer<object>.GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    public SerializedProperty? FindNodeProperty(string guid)
    {
        EnsureCaches();
        if (_guidToPropertyPath.TryGetValue(guid, out var path))
            return SerializedObject.FindProperty(path);
        return null;
    }

    public string? GetRootGuid()
    {
        if (RootProperty.managedReferenceValue == null) return null;
        var guidProp = RootProperty.FindPropertyRelative("_guid");
        return guidProp?.stringValue;
    }

    public IReadOnlyList<NodeInfo> GetAllNodes()
    {
        EnsureCaches();
        if (_cachedNodes != null) return _cachedNodes;

        _cachedNodes = new List<NodeInfo>();
        var visited = new HashSet<string>();

        if (RootProperty.managedReferenceValue != null)
            CollectNodesRecursive(RootProperty, false, _cachedNodes, visited);

        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue != null)
                CollectNodesRecursive(orphan, true, _cachedNodes, visited);
        }

        return _cachedNodes;
    }

    private static readonly Dictionary<Type, BTNodeDescriptionAttribute?> _descriptionCache = new();

    private static BTNodeDescriptionAttribute? GetDescriptionAttribute(Type type)
    {
        if (!_descriptionCache.TryGetValue(type, out var attr))
        {
            attr = type.GetCustomAttributes(typeof(BTNodeDescriptionAttribute), false)
                .FirstOrDefault() as BTNodeDescriptionAttribute;
            _descriptionCache[type] = attr;
        }
        return attr;
    }

    private static void CollectNodesRecursive(SerializedProperty nodeProp, bool isOrphan, List<NodeInfo> result, HashSet<string> visited)
    {
        var node = nodeProp.managedReferenceValue;
        if (node == null) return;

        var type = node.GetType();
        var guidProp = nodeProp.FindPropertyRelative("_guid");
        var posProp = nodeProp.FindPropertyRelative("_position");
        if (guidProp == null || posProp == null) return;
        if (string.IsNullOrEmpty(guidProp.stringValue) || !visited.Add(guidProp.stringValue)) return;

        var attr = GetDescriptionAttribute(type);

        string displayName = attr?.Name ?? type.Name;
        string description = attr?.Category ?? "";

        // For subtree nodes, show the referenced asset name
        if (node is BTSubtree subtree && subtree.SubtreeAsset != null)
            description = subtree.SubtreeAsset.name;

        result.Add(new NodeInfo
        {
            Guid = guidProp.stringValue,
            Type = type,
            DisplayName = displayName,
            Description = description,
            Position = posProp.vector2Value,
            IsOrphan = isOrphan,
            NodeCategory = GetNodeCategory(type)
        });

        var childrenProp = nodeProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                if (child.managedReferenceValue != null)
                    CollectNodesRecursive(child, false, result, visited);
            }
            return;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
            CollectNodesRecursive(childProp, false, result, visited);
    }

    public static NodeCategory GetNodeCategory(Type type)
    {
        if (type == typeof(BTSubtree)) return NodeCategory.Subtree;
        if (type.IsSubclassOf(typeof(BTComposite))) return NodeCategory.Composite;
        if (type.IsSubclassOf(typeof(BTDecorator))) return NodeCategory.Decorator;
        if (type.IsSubclassOf(typeof(BTLeafCondition))) return NodeCategory.Condition;
        return NodeCategory.Action;
    }

    public IReadOnlyList<(string parentGuid, string childGuid)> GetParentChildPairs()
    {
        EnsureCaches();
        if (_cachedPairs != null) return _cachedPairs;

        _cachedPairs = new List<(string, string)>();
        var visited = new HashSet<string>();

        if (RootProperty.managedReferenceValue != null)
            CollectPairsRecursive(RootProperty, _cachedPairs, visited);

        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue != null)
                CollectPairsRecursive(orphan, _cachedPairs, visited);
        }

        return _cachedPairs;
    }

    private static void CollectPairsRecursive(SerializedProperty nodeProp, List<(string, string)> result, HashSet<string> visited)
    {
        var parentGuid = nodeProp.FindPropertyRelative("_guid")?.stringValue;
        if (string.IsNullOrEmpty(parentGuid) || !visited.Add(parentGuid!)) return;

        var childrenProp = nodeProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                if (child.managedReferenceValue == null) continue;

                var childGuid = child.FindPropertyRelative("_guid")?.stringValue;
                if (!string.IsNullOrEmpty(childGuid))
                {
                    result.Add((parentGuid!, childGuid!));
                    CollectPairsRecursive(child, result, visited);
                }
            }
            return;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
        {
            var childGuid = childProp.FindPropertyRelative("_guid")?.stringValue;
            if (!string.IsNullOrEmpty(childGuid))
            {
                result.Add((parentGuid!, childGuid!));
                CollectPairsRecursive(childProp, result, visited);
            }
        }
    }

    public List<string> GetChildGuids(string guid)
    {
        EnsureCaches();
        var result = new List<string>();
        var nodeProp = FindNodeProperty(guid);
        if (nodeProp == null) return result;

        var childrenProp = nodeProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                if (child.managedReferenceValue == null) continue;
                var childGuid = child.FindPropertyRelative("_guid")?.stringValue;
                if (!string.IsNullOrEmpty(childGuid))
                    result.Add(childGuid!);
            }
            return result;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
        {
            var childGuid = childProp.FindPropertyRelative("_guid")?.stringValue;
            if (!string.IsNullOrEmpty(childGuid))
                result.Add(childGuid!);
        }

        return result;
    }

    public string? CreateNode(Type type, Vector2 position)
    {
        SerializedObject.Update();

        var node = (BTNode?)Activator.CreateInstance(type);
        if (node == null) return null;

        node.Guid = Guid.NewGuid().ToString();
        node.Position = position;

        if (RootProperty.managedReferenceValue == null)
        {
            RootProperty.managedReferenceValue = node;
        }
        else
        {
            int index = OrphansProperty.arraySize;
            OrphansProperty.InsertArrayElementAtIndex(index);
            OrphansProperty.GetArrayElementAtIndex(index).managedReferenceValue = node;
        }

        SerializedObject.ApplyModifiedProperties();
        RebuildGuidCache();
        return node.Guid;
    }

    public string? CreateNodeFromInstance(BTNode node, Vector2 position)
    {
        SerializedObject.Update();

        if (string.IsNullOrEmpty(node.Guid))
            node.Guid = Guid.NewGuid().ToString();
        node.Position = position;

        int index = OrphansProperty.arraySize;
        OrphansProperty.InsertArrayElementAtIndex(index);
        OrphansProperty.GetArrayElementAtIndex(index).managedReferenceValue = node;

        SerializedObject.ApplyModifiedProperties();
        RebuildGuidCache();
        return node.Guid;
    }

    public void DeleteNode(string guid)
    {
        SerializedObject.Update();

        ExtractChildrenToOrphans(guid);

        var rootGuid = GetRootGuid();
        if (rootGuid == guid)
        {
            RootProperty.managedReferenceValue = null;
            SerializedObject.ApplyModifiedProperties();
            RebuildGuidCache();
            return;
        }

        // Try to remove from orphans
        if (TryRemoveFromOrphans(guid))
        {
            SerializedObject.ApplyModifiedProperties();
            RebuildGuidCache();
            return;
        }

        // Remove from parent's children
        var parentGuid = FindParentGuid(guid);
        if (parentGuid != null)
        {
            RemoveChild(parentGuid, guid);
        }
    }

    private void ExtractChildrenToOrphans(string guid)
    {
        var nodeProp = FindNodeProperty(guid);
        if (nodeProp == null) return;

        var childrenProp = nodeProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                if (child.managedReferenceValue == null) continue;
                int idx = OrphansProperty.arraySize;
                OrphansProperty.InsertArrayElementAtIndex(idx);
                OrphansProperty.GetArrayElementAtIndex(idx).managedReferenceValue = child.managedReferenceValue;
            }
            childrenProp.ClearArray();
            return;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
        {
            int idx = OrphansProperty.arraySize;
            OrphansProperty.InsertArrayElementAtIndex(idx);
            OrphansProperty.GetArrayElementAtIndex(idx).managedReferenceValue = childProp.managedReferenceValue;
            childProp.managedReferenceValue = null;
        }
    }

    private bool TryRemoveFromOrphans(string guid)
    {
        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue == null) continue;
            var orphanGuid = orphan.FindPropertyRelative("_guid")?.stringValue;
            if (orphanGuid == guid)
            {
                OrphansProperty.DeleteArrayElementAtIndex(i);
                return true;
            }
        }
        return false;
    }

    private string? FindParentGuid(string childGuid)
    {
        if (_cachedChildToParent == null)
        {
            var pairs = GetParentChildPairs();
            _cachedChildToParent = new Dictionary<string, string>(pairs.Count);
            foreach (var (parentGuid, cGuid) in pairs)
                _cachedChildToParent[cGuid] = parentGuid;
        }

        return _cachedChildToParent.TryGetValue(childGuid, out var parent) ? parent : null;
    }

    public void AddChild(string parentGuid, string childGuid)
    {
        SerializedObject.Update();

        var parentProp = FindNodeProperty(parentGuid);
        var childProp = FindNodeProperty(childGuid);
        if (parentProp == null || childProp == null) return;

        var childNode = childProp.managedReferenceValue;
        if (childNode == null) return;

        // Remove from orphans if present
        TryRemoveFromOrphans(childGuid);

        // Remove from previous parent if any
        var prevParent = FindParentGuid(childGuid);
        if (prevParent != null)
        {
            RemoveChildInternal(prevParent, childGuid);
        }

        // Refresh parent prop after potential structural changes
        SerializedObject.ApplyModifiedProperties();
        SerializedObject.Update();
        RebuildGuidCache();
        parentProp = FindNodeProperty(parentGuid);
        if (parentProp == null) return;

        var childrenProp = parentProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            int index = childrenProp.arraySize;
            childrenProp.InsertArrayElementAtIndex(index);
            childrenProp.GetArrayElementAtIndex(index).managedReferenceValue = childNode;
        }
        else
        {
            var singleChild = parentProp.FindPropertyRelative("Child");
            if (singleChild != null)
            {
                if (singleChild.managedReferenceValue != null)
                {
                    var existingChild = singleChild.managedReferenceValue;
                    int idx = OrphansProperty.arraySize;
                    OrphansProperty.InsertArrayElementAtIndex(idx);
                    OrphansProperty.GetArrayElementAtIndex(idx).managedReferenceValue = existingChild;
                }
                singleChild.managedReferenceValue = childNode;
            }
        }

        SerializedObject.ApplyModifiedProperties();
        RebuildGuidCache();
    }

    public void RemoveChild(string parentGuid, string childGuid)
    {
        SerializedObject.Update();

        var childProp = FindNodeProperty(childGuid);
        var childNode = childProp?.managedReferenceValue;

        RemoveChildInternal(parentGuid, childGuid);

        // Move to orphans
        if (childNode != null)
        {
            int index = OrphansProperty.arraySize;
            OrphansProperty.InsertArrayElementAtIndex(index);
            OrphansProperty.GetArrayElementAtIndex(index).managedReferenceValue = childNode;
        }

        SerializedObject.ApplyModifiedProperties();
        RebuildGuidCache();
    }

    private void RemoveChildInternal(string parentGuid, string childGuid)
    {
        var parentProp = FindNodeProperty(parentGuid);
        if (parentProp == null) return;

        var childrenProp = parentProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                var guid = child.FindPropertyRelative("_guid")?.stringValue;
                if (guid == childGuid)
                {
                    childrenProp.DeleteArrayElementAtIndex(i);
                    return;
                }
            }
            return;
        }

        var singleChild = parentProp.FindPropertyRelative("Child");
        if (singleChild != null)
        {
            var guid = singleChild.FindPropertyRelative("_guid")?.stringValue;
            if (guid == childGuid)
                singleChild.managedReferenceValue = null;
        }
    }

    public void SetPosition(string guid, Vector2 position)
    {
        // Write directly to avoid full serialization round-trip per drag pixel
        var nodeProp = FindNodeProperty(guid);
        if (nodeProp == null) return;

        if (nodeProp.managedReferenceValue is BTNode node)
            node.Position = position;

        _dirtyPositions = true;
    }

    private bool _dirtyPositions;

    public void FlushPositions()
    {
        if (!_dirtyPositions) return;
        _dirtyPositions = false;

        SerializedObject.Update();
        SerializedObject.ApplyModifiedProperties();
    }

    public void SetPositionBatch(Dictionary<string, Vector2> positions)
    {
        SerializedObject.Update();
        foreach (var (guid, pos) in positions)
        {
            var nodeProp = FindNodeProperty(guid);
            var posProp = nodeProp?.FindPropertyRelative("_position");
            if (posProp != null)
                posProp.vector2Value = pos;
        }
        SerializedObject.ApplyModifiedProperties();
    }

    public void SortChildren(string parentGuid)
    {
        SerializedObject.Update();
        var parentProp = FindNodeProperty(parentGuid);
        if (parentProp == null) return;

        var childrenProp = parentProp.FindPropertyRelative("Children");
        if (childrenProp == null || childrenProp.arraySize <= 1) return;

        // Collect children with their X positions
        var childNodes = new List<(BTNode node, float x)>();
        for (int i = 0; i < childrenProp.arraySize; i++)
        {
            var child = childrenProp.GetArrayElementAtIndex(i);
            if (child.managedReferenceValue is BTNode node)
            {
                var posProp = child.FindPropertyRelative("_position");
                float x = posProp?.vector2Value.x ?? 0;
                childNodes.Add((node, x));
            }
        }

        childNodes.Sort((a, b) => a.x.CompareTo(b.x));

        // Rewrite
        for (int i = 0; i < childNodes.Count; i++)
            childrenProp.GetArrayElementAtIndex(i).managedReferenceValue = childNodes[i].node;

        SerializedObject.ApplyModifiedProperties();
        RebuildGuidCache();
    }

    public bool NeedsAutoLayout()
    {
        var nodes = GetAllNodes();
        return nodes.Count > 1 && nodes.All(n => n.Position == Vector2.zero);
    }

    public bool WouldCreateCycle(string parentGuid, string childGuid)
    {
        if (parentGuid == childGuid) return true;

        // DFS from parentGuid upward through the tree — if we reach childGuid, adding
        // childGuid→parentGuid edge would form a cycle. Equivalently, check if parentGuid
        // is reachable from childGuid via existing edges.
        var visited = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(childGuid);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == parentGuid) return true;
            if (!visited.Add(current)) continue;

            foreach (var child in GetChildGuids(current))
                stack.Push(child);
        }

        return false;
    }

    public BehaviorTreeAsset Asset => (BehaviorTreeAsset)SerializedObject.targetObject;

    public static bool HasSubtreeCycle(BehaviorTreeAsset currentAsset, BehaviorTreeAsset? referencedAsset)
    {
        if (referencedAsset == null) return false;
        var visited = new HashSet<BehaviorTreeAsset> { currentAsset };
        return CheckCycleRecursive(referencedAsset, visited);
    }

    private static bool CheckCycleRecursive(BehaviorTreeAsset asset, HashSet<BehaviorTreeAsset> visited)
    {
        if (!visited.Add(asset)) return true;
        if (asset.Root == null) return false;
        return CheckNodeForCycles(asset.Root, visited);
    }

    private static bool CheckNodeForCycles(BTNode node, HashSet<BehaviorTreeAsset> visited)
    {
        if (node is BTSubtree subtree && subtree.SubtreeAsset != null)
        {
            if (CheckCycleRecursive(subtree.SubtreeAsset, visited))
                return true;
        }

        if (node is BTComposite composite)
        {
            foreach (var child in composite.GetChildren())
            {
                if (child != null && CheckNodeForCycles(child, visited))
                    return true;
            }
        }
        else if (node is BTDecorator decorator)
        {
            var child = decorator.GetChild();
            if (child != null && CheckNodeForCycles(child, visited))
                return true;
        }

        return false;
    }

    public struct NodeInfo
    {
        public string Guid;
        public Type Type;
        public string DisplayName;
        public string Description;
        public Vector2 Position;
        public bool IsOrphan;
        public NodeCategory NodeCategory;
    }
}

public enum NodeCategory
{
    Composite,
    Decorator,
    Action,
    Condition,
    Subtree
}
