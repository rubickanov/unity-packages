using System;
using System.Collections.Generic;
using System.Linq;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class BehaviorTreeSearchWindow : ScriptableObject, ISearchWindowProvider
{
    private BehaviorTreeGraphView _graphView = default!;
    private BehaviorTreeSerializer _serializer = default!;

    public Port? FromPort { get; set; }
    public Vector2 CreationPosition { get; set; }

    public void Initialize(BehaviorTreeGraphView graphView, BehaviorTreeSerializer serializer)
    {
        _graphView = graphView;
        _serializer = serializer;
    }

    public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
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
