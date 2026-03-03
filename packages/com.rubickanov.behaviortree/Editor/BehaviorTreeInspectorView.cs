using System;
using System.Collections.Generic;
using System.Linq;
using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Rubickanov.BehaviorTree.Editor;

public class BehaviorTreeInspectorView : VisualElement
{
    public Action<string>? OnPropertyChanged;

    public void UpdateSelection(BehaviorTreeSerializer serializer, string guid)
    {
        Clear();

        var nodeProp = serializer.FindNodeProperty(guid);
        if (nodeProp == null) return;

        var node = nodeProp.managedReferenceValue;
        if (node == null) return;

        var type = node.GetType();
        var attr = type.GetCustomAttributes(typeof(BTNodeDescriptionAttribute), false)
            .FirstOrDefault() as BTNodeDescriptionAttribute;

        // Header
        var header = new Label(attr?.Name ?? type.Name);
        header.AddToClassList("panel-header");
        Add(header);

        // Description
        if (!string.IsNullOrEmpty(attr?.Description))
        {
            var descLabel = new Label(attr?.Description);
            descLabel.AddToClassList("inspector-description");
            Add(descLabel);
        }

        // Script field (double-click opens IDE)
        var script = FindScript(type);
        if (script != null)
        {
            var scriptField = new ObjectField("Script") { value = script };
            scriptField.objectType = typeof(MonoScript);
            scriptField.SetEnabled(false);
            scriptField.style.paddingLeft = 4;
            scriptField.style.paddingRight = 4;
            scriptField.style.paddingTop = 4;
            scriptField.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    AssetDatabase.OpenAsset(script);
                    evt.StopPropagation();
                }
            });
            Add(scriptField);
        }

        // Iterate properties, skip structural fields
        var iterator = nodeProp.Copy();
        var endProperty = nodeProp.GetEndProperty();

        if (!iterator.NextVisible(true))
            return;

        do
        {
            if (SerializedProperty.EqualContents(iterator, endProperty))
                break;

            string name = iterator.name;
            if (name is "Children" or "Child" or "_guid" or "_position")
                continue;

            var field = new PropertyField(iterator);
            field.style.paddingLeft = 4;
            field.style.paddingRight = 4;
            field.style.paddingTop = 2;
            field.Bind(serializer.SerializedObject);
            field.RegisterValueChangeCallback(_ => OnPropertyChanged?.Invoke(guid));
            Add(field);
        }
        while (iterator.NextVisible(false));
    }

    private static readonly Dictionary<Type, MonoScript?> s_scriptCache = new();

    private static MonoScript? FindScript(Type type)
    {
        if (s_scriptCache.TryGetValue(type, out var cached))
            return cached;

        MonoScript? result = null;
        var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == type)
            {
                result = script;
                break;
            }
        }

        s_scriptCache[type] = result;
        return result;
    }

    public void ClearSelection()
    {
        Clear();
        var label = new Label("Select a node to inspect");
        label.style.paddingLeft = 8;
        label.style.paddingTop = 8;
        label.style.color = new StyleColor(new UnityEngine.Color(0.6f, 0.6f, 0.6f));
        Add(label);
    }
}
