using System;
using System.Collections.Generic;
using System.Linq;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEngine;

public class BehaviorTreeSerializer
{
    public SerializedObject SerializedObject { get; }
    public SerializedProperty RootProperty { get; }
    public SerializedProperty OrphansProperty { get; }

    private readonly Dictionary<string, string> _guidToPropertyPath = new();

    public BehaviorTreeSerializer(SerializedObject serializedObject)
    {
        SerializedObject = serializedObject;
        RootProperty = serializedObject.FindProperty("_root");
        OrphansProperty = serializedObject.FindProperty("_orphans");
        RebuildGuidCache();
    }

    public void RebuildGuidCache()
    {
        _guidToPropertyPath.Clear();

        if (RootProperty.managedReferenceValue != null)
            CacheNodeRecursive(RootProperty);

        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue != null)
                CacheNodeRecursive(orphan);
        }
    }

    private void CacheNodeRecursive(SerializedProperty nodeProp)
    {
        var guidProp = nodeProp.FindPropertyRelative("_guid");
        if (guidProp == null) return;

        if (string.IsNullOrEmpty(guidProp.stringValue))
            guidProp.stringValue = Guid.NewGuid().ToString();

        _guidToPropertyPath[guidProp.stringValue] = nodeProp.propertyPath;

        var childrenProp = nodeProp.FindPropertyRelative("Children");
        if (childrenProp != null)
        {
            for (int i = 0; i < childrenProp.arraySize; i++)
            {
                var child = childrenProp.GetArrayElementAtIndex(i);
                if (child.managedReferenceValue != null)
                    CacheNodeRecursive(child);
            }
            return;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
            CacheNodeRecursive(childProp);
    }

    public SerializedProperty? FindNodeProperty(string guid)
    {
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

    public List<NodeInfo> GetAllNodes()
    {
        var result = new List<NodeInfo>();

        if (RootProperty.managedReferenceValue != null)
            CollectNodesRecursive(RootProperty, false, result);

        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue != null)
                CollectNodesRecursive(orphan, true, result);
        }

        return result;
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

    private static void CollectNodesRecursive(SerializedProperty nodeProp, bool isOrphan, List<NodeInfo> result)
    {
        var node = nodeProp.managedReferenceValue;
        if (node == null) return;

        var type = node.GetType();
        var guidProp = nodeProp.FindPropertyRelative("_guid");
        var posProp = nodeProp.FindPropertyRelative("_position");
        if (guidProp == null || posProp == null) return;

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
                    CollectNodesRecursive(child, false, result);
            }
            return;
        }

        var childProp = nodeProp.FindPropertyRelative("Child");
        if (childProp?.managedReferenceValue != null)
            CollectNodesRecursive(childProp, false, result);
    }

    public static NodeCategory GetNodeCategory(Type type)
    {
        if (type == typeof(BTSubtree)) return NodeCategory.Subtree;
        if (type.IsSubclassOf(typeof(BTComposite))) return NodeCategory.Composite;
        if (type.IsSubclassOf(typeof(BTDecorator))) return NodeCategory.Decorator;
        if (type.IsSubclassOf(typeof(BTLeafCondition))) return NodeCategory.Condition;
        return NodeCategory.Action;
    }

    public List<(string parentGuid, string childGuid)> GetParentChildPairs()
    {
        var result = new List<(string, string)>();

        if (RootProperty.managedReferenceValue != null)
            CollectPairsRecursive(RootProperty, result);

        for (int i = 0; i < OrphansProperty.arraySize; i++)
        {
            var orphan = OrphansProperty.GetArrayElementAtIndex(i);
            if (orphan.managedReferenceValue != null)
                CollectPairsRecursive(orphan, result);
        }

        return result;
    }

    private static void CollectPairsRecursive(SerializedProperty nodeProp, List<(string, string)> result)
    {
        var parentGuid = nodeProp.FindPropertyRelative("_guid")?.stringValue;
        if (string.IsNullOrEmpty(parentGuid)) return;

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
                    CollectPairsRecursive(child, result);
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
                CollectPairsRecursive(childProp, result);
            }
        }
    }

    public List<string> GetChildGuids(string guid)
    {
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
        var pairs = GetParentChildPairs();
        foreach (var (parentGuid, cGuid) in pairs)
        {
            if (cGuid == childGuid) return parentGuid;
        }
        return null;
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

    private static readonly System.Reflection.FieldInfo? _compositeChildrenField =
        typeof(BTComposite).GetField("Children",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static readonly System.Reflection.FieldInfo? _decoratorChildField =
        typeof(BTDecorator).GetField("Child",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static bool CheckNodeForCycles(BTNode node, HashSet<BehaviorTreeAsset> visited)
    {
        if (node is BTSubtree subtree && subtree.SubtreeAsset != null)
        {
            if (CheckCycleRecursive(subtree.SubtreeAsset, visited))
                return true;
        }

        if (node is BTComposite)
        {
            if (_compositeChildrenField?.GetValue(node) is BTNode[] children)
            {
                foreach (var child in children)
                {
                    if (child != null && CheckNodeForCycles(child, visited))
                        return true;
                }
            }
        }
        else if (node is BTDecorator)
        {
            if (_decoratorChildField?.GetValue(node) is BTNode child)
            {
                if (CheckNodeForCycles(child, visited))
                    return true;
            }
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
