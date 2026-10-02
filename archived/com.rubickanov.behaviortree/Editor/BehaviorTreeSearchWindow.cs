using System;
using System.Collections.Generic;
using System.Linq;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Editor;

public class BehaviorTreeSearchWindow : ScriptableObject, ISearchWindowProvider
{
    private BehaviorTreeGraphView _graphView = default!;
    private BehaviorTreeSerializer _serializer = default!;

    public Port? FromPort { get; set; }
    public Vector2 CreationPosition { get; set; }

    private List<SearchTreeEntry>? _cachedEntries;

    public void Initialize(BehaviorTreeGraphView graphView, BehaviorTreeSerializer serializer)
    {
        _graphView = graphView;
        _serializer = serializer;
    }

    public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
    {
        _cachedEntries ??= BuildSearchTree();

        // When dragging from an input port (looking for a parent), filter to types that can have children
        if (FromPort?.direction == Direction.Input)
            return FilterParentOnly(_cachedEntries);

        return _cachedEntries;
    }

    private static List<SearchTreeEntry> FilterParentOnly(List<SearchTreeEntry> entries)
    {
        var filtered = new List<SearchTreeEntry>();
        SearchTreeGroupEntry? pendingGroup = null;

        foreach (var entry in entries)
        {
            if (entry is SearchTreeGroupEntry groupEntry)
            {
                if (groupEntry.level == 0)
                {
                    filtered.Add(groupEntry);
                    continue;
                }
                pendingGroup = groupEntry;
                continue;
            }

            if (entry.userData is Type type && CanHaveChildren(type))
            {
                if (pendingGroup != null)
                {
                    filtered.Add(pendingGroup);
                    pendingGroup = null;
                }
                filtered.Add(entry);
            }
        }

        return filtered;
    }

    private static bool CanHaveChildren(Type type)
    {
        return type.IsSubclassOf(typeof(BTComposite)) || type.IsSubclassOf(typeof(BTDecorator));
    }

    private static List<SearchTreeEntry> BuildSearchTree()
    {
        var entries = new List<SearchTreeEntry>
        {
            new SearchTreeGroupEntry(new GUIContent("Create Node"), 0)
        };

        var types = TypeCache.GetTypesDerivedFrom<BTNode>();
        var grouped = new SortedDictionary<string, List<(string name, Type type)>>();

        foreach (var type in types)
        {
            if (type.IsAbstract) continue;
            if (type == typeof(BTAction) || type == typeof(BTCondition)) continue;

            var attr = type.GetCustomAttributes(typeof(BTNodeDescriptionAttribute), false)
                .FirstOrDefault() as BTNodeDescriptionAttribute;

            string name = attr?.Name ?? type.Name;
            string category = attr?.Category ?? "Other";

            if (!grouped.ContainsKey(category))
                grouped[category] = new List<(string, Type)>();

            grouped[category].Add((name, type));
        }

        foreach (var (category, items) in grouped)
        {
            entries.Add(new SearchTreeGroupEntry(new GUIContent(category), 1));

            items.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            foreach (var (name, type) in items)
            {
                entries.Add(new SearchTreeEntry(new GUIContent(name))
                {
                    userData = type,
                    level = 2
                });
            }
        }

        return entries;
    }

    public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
    {
        var type = entry.userData as Type;
        if (type == null) return false;

        _graphView.CreateNodeFromSearch(type, CreationPosition, FromPort);
        FromPort = null;
        return true;
    }
}
