using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.BehaviorTree.Editor;

[CustomEditor(typeof(BehaviorTreeAsset))]
public class BehaviorTreeAssetEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open in Editor", GUILayout.Height(30)))
            BehaviorTreeEditorWindow.OpenAsset((BehaviorTreeAsset)target);

        EditorGUILayout.Space(4);

        var asset = (BehaviorTreeAsset)target;
        var so = new SerializedObject(asset);
        var serializer = new BehaviorTreeSerializer(so);

        var rootGuid = serializer.GetRootGuid();
        var allNodes = serializer.GetAllNodes();
        int orphanCount = 0;
        string rootType = "None";

        foreach (var node in allNodes)
        {
            if (node.IsOrphan) orphanCount++;
            if (node.Guid == rootGuid) rootType = node.DisplayName;
        }

        EditorGUILayout.LabelField("Root", rootType);
        EditorGUILayout.LabelField("Nodes", allNodes.Count.ToString());
        if (orphanCount > 0)
            EditorGUILayout.LabelField("Orphans", orphanCount.ToString());
    }
}
