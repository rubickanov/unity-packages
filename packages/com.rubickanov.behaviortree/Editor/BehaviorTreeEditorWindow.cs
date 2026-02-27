using Rubickanov.BehaviorTree.Runtime;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class BehaviorTreeEditorWindow : EditorWindow
{
    private BehaviorTreeGraphView _graphView = default!;
    private BehaviorTreeInspectorView _inspectorView = default!;
    private BehaviorTreeSerializer? _serializer;
    private ObjectField _assetField = default!;
    private BehaviorTreeAsset? _asset;

    [MenuItem("Window/AI/Behavior Tree")]
    public static void ShowWindow()
    {
        var window = GetWindow<BehaviorTreeEditorWindow>();
        window.titleContent = new GUIContent("Behavior Tree");
    }

    public static void OpenAsset(BehaviorTreeAsset asset)
    {
        var window = GetWindow<BehaviorTreeEditorWindow>();
        window.titleContent = new GUIContent("Behavior Tree");
        window.LoadAsset(asset);
    }

    [OnOpenAsset]
    public static bool OnOpenAsset(int instanceID, int line)
    {
        var asset = EditorUtility.EntityIdToObject(instanceID) as BehaviorTreeAsset;
        if (asset == null) return false;
        OpenAsset(asset);
        return true;
    }

    public void CreateGUI()
    {
        var visualTree = LoadUxml();
        if (visualTree != null)
            visualTree.CloneTree(rootVisualElement);

        // Create graph view
        _graphView = new BehaviorTreeGraphView();
        _graphView.style.flexGrow = 1;
        _graphView.OnNodeSelected += OnNodeSelectionChanged;

        var graphContainer = rootVisualElement.Q<VisualElement>("graph-container");
        if (graphContainer != null)
        {
            graphContainer.Add(_graphView);
        }
        else
        {
            // Fallback: add directly
            rootVisualElement.Add(_graphView);
        }

        // Inspector view
        var inspectorScroll = rootVisualElement.Q<ScrollView>("inspector-scroll");
        _inspectorView = new BehaviorTreeInspectorView();
        inspectorScroll?.Add(_inspectorView);

        // Asset field
        _assetField = rootVisualElement.Q<ObjectField>("asset-field");
        if (_assetField != null)
        {
            _assetField.objectType = typeof(BehaviorTreeAsset);
            _assetField.RegisterValueChangedCallback(evt =>
            {
                var newAsset = evt.newValue as BehaviorTreeAsset;
                LoadAsset(newAsset);
            });
        }

        // Toolbar buttons
        var layoutBtn = rootVisualElement.Q<ToolbarButton>("btn-layout");
        layoutBtn?.RegisterCallback<ClickEvent>(_ =>
        {
            if (_serializer == null) return;
            BehaviorTreeAutoLayout.Layout(_serializer);
            _graphView.PopulateView(_serializer);
            _graphView.FrameAll();
        });

        var minimapBtn = rootVisualElement.Q<ToolbarButton>("btn-minimap");
        minimapBtn?.RegisterCallback<ClickEvent>(_ => _graphView.ToggleMiniMap());

        var centerBtn = rootVisualElement.Q<ToolbarButton>("btn-center");
        centerBtn?.RegisterCallback<ClickEvent>(_ => _graphView.FrameAll());

        // Node selection
        _graphView.RegisterCallback<MouseDownEvent>(_ =>
        {
            EditorApplication.delayCall += () =>
            {
                if (_graphView.selection.Count == 0)
                    OnNodeSelectionChanged(null);
            };
        });

        // Undo support
        Undo.undoRedoPerformed += OnUndoRedo;

        // Restore selection
        if (_asset != null)
            LoadAsset(_asset);
    }

    private static VisualTreeAsset? LoadUxml()
    {
        var guids = AssetDatabase.FindAssets("BehaviorTreeEditorWindow t:VisualTreeAsset");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("BehaviorTreeEditorWindow.uxml"))
                return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        }
        return null;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    private void OnSelectionChange()
    {
        var asset = Selection.activeObject as BehaviorTreeAsset;
        if (asset != null)
            LoadAsset(asset);
    }

    private void LoadAsset(BehaviorTreeAsset? asset)
    {
        _asset = asset;

        if (_assetField != null)
            _assetField.SetValueWithoutNotify(asset);

        if (asset == null)
        {
            _serializer = null;
            _inspectorView?.ClearSelection();
            return;
        }

        var so = new SerializedObject(asset);
        _serializer = new BehaviorTreeSerializer(so);

        // Assign GUIDs if needed, then apply
        so.ApplyModifiedPropertiesWithoutUndo();

        _graphView?.PopulateView(_serializer);
        _inspectorView?.ClearSelection();
    }

    private void OnNodeSelectionChanged(BehaviorTreeNodeView? nodeView)
    {
        if (_serializer == null || nodeView == null)
        {
            _inspectorView?.ClearSelection();
            return;
        }
        _inspectorView?.UpdateSelection(_serializer, nodeView.Guid);
    }

    private void OnUndoRedo()
    {
        if (_asset != null)
            LoadAsset(_asset);
    }

    private BehaviorTreeRunner? _cachedRunner;

    private void Update()
    {
        if (!Application.isPlaying)
        {
            _cachedRunner = null;
            return;
        }
        if (_serializer == null || _asset == null) return;

        // Re-lookup only if cache is stale
        if (_cachedRunner == null || _cachedRunner.Asset != _asset)
        {
            _cachedRunner = null;
            var runners = FindObjectsByType<BehaviorTreeRunner>(FindObjectsSortMode.None);
            foreach (var runner in runners)
            {
                if (runner.Asset == _asset)
                {
                    _cachedRunner = runner;
                    break;
                }
            }
        }

        if (_cachedRunner != null)
            _graphView?.UpdateRuntimeState(_cachedRunner.RuntimeRoot);
    }
}
